"""Opt-in Gemini HTTP workflow using an isolated SQLite database and synthetic lesson.

Build Release first. Run with --run-live to allow at most two provider sends.
The API loads its normal private configuration; keys/tokens are never printed.
The real application database and Uploads are not used or modified.
"""
import argparse
from contextlib import closing
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import secrets
import socket
import sqlite3
import subprocess
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
API = ROOT / "BE/Services/Core/LMS.Core.API"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-live", action="store_true")
    args = parser.parse_args()
    if not args.run_live:
        parser.error("--run-live is required; this makes real Google API requests")
    assembly = API / "bin/Release/net10.0/LMS.Core.API.dll"
    if not assembly.exists():
        parser.error("Build the Release API first")
    directory = ROOT / "artifacts/verification" / ("gemini-live-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ"))
    directory.mkdir(parents=True, exist_ok=False)
    database = directory / "live-fixture.sqlite"
    password = "Live!Aa1" + secrets.token_hex(16)
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    environment = os.environ.copy()
    environment.update({
        "ASPNETCORE_ENVIRONMENT": "Development", "DotEnv__Enabled": "true",
        "Database__Provider": "Sqlite", "Database__AutoMigrate": "true",
        "ConnectionStrings__Sqlite": "Data Source=" + str(database),
        "Storage__RootPath": str(directory / "Uploads"), "Demo__Seed": "true",
        "Demo__Password": password, "Bootstrap__AdminUsername": "",
        "Jwt__Key": secrets.token_hex(48), "Ai__Mode": "Gemini",
        "Ai__MaxRetries": "1", "Ai__MaxProviderRequests": "2", "Ai__MaxGenerationChunks": "1",
        "Logging__LogLevel__Default": "Warning", "Logging__LogLevel__Microsoft": "Warning",
    })
    token = None

    def call(path, body=None, content_type="application/json", timeout=20):
        data = body if isinstance(body, bytes) else json.dumps(body).encode() if body is not None else None
        headers = {"Content-Type": content_type}
        if token:
            headers["Authorization"] = "Bearer " + token
        try:
            with urlopen(Request(base + path, data=data, headers=headers), timeout=timeout) as response:
                return response.status, json.load(response)
        except HTTPError as error:
            return error.code, json.loads(error.read())

    result = {"timestampUtc": datetime.now(timezone.utc).isoformat(), "provider": "Google Gemini", "maxProviderRequests": 2,
              "syntheticSourceOnly": True, "applicationDatabaseUntouched": True, "passed": False}
    with (directory / "host.log").open("w", encoding="utf-8") as host_log:
        process = subprocess.Popen(["dotnet", str(assembly), "--urls", base], cwd=API, env=environment,
                                   stdout=host_log, stderr=subprocess.STDOUT,
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        try:
            deadline = time.monotonic() + 45
            while True:
                try:
                    if call("/api/health/ready")[0] == 200:
                        break
                except (URLError, TimeoutError, OSError):
                    pass
                if process.poll() is not None or time.monotonic() > deadline:
                    raise RuntimeError("Isolated API did not become ready; inspect host.log")
                time.sleep(0.25)
            status, auth = call("/api/auth/login", {"username": "lecturer", "password": password})
            if status != 200:
                raise RuntimeError("Fixture lecturer login failed")
            token = auth["accessToken"]
            _, state = call("/api/ai/questions/status")
            result["model"] = state.get("model")
            if not state.get("enabled"):
                raise RuntimeError("Gemini key is not configured on the Backend")
            status, course = call("/api/courses", {"code": "LIVEPROBE", "title": "Isolated Gemini verification", "description": "Synthetic fixture"})
            if status != 201:
                raise RuntimeError("Fixture course creation failed")
            status, topic = call(f"/api/courses/{course['id']}/topics", {"name": "Database transactions"})
            if status != 201:
                raise RuntimeError("Fixture topic creation failed")
            lesson = ("A database transaction is a group of operations committed together. Atomicity means all operations succeed or none do. "
                      "Consistency preserves database rules. Isolation prevents concurrent transactions from interfering. "
                      "Durability means committed data survives a restart. A unique constraint prevents duplicate enrollment records. "
                      "The server validates quiz answers and computes scores; clients cannot choose their scores.")
            boundary = "LmsProbe" + secrets.token_hex(12)
            parts = []
            for name, value in {"topicId": topic["id"], "title": "Synthetic transaction lesson"}.items():
                parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n')
            parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="lesson.txt"\r\nContent-Type: text/plain\r\n\r\n{lesson}\r\n--{boundary}--\r\n')
            status, material = call(f"/api/courses/{course['id']}/materials", "".join(parts).encode(), "multipart/form-data; boundary=" + boundary)
            if status != 201:
                raise RuntimeError("Fixture upload failed")
            request = {"materialId": material["id"], "count": 3, "difficulty": "Medium"}
            status, plan = call("/api/ai/questions/plan", request)
            if status != 200:
                raise RuntimeError("Generation planning failed")
            result["plan"] = plan
            started = time.monotonic()
            status, generated = call("/api/ai/questions/generate", request, timeout=310)
            result.update(httpStatus=status, elapsedSeconds=round(time.monotonic() - started, 2))
            if status == 200:
                if not generated or any(q["status"] != "Draft" or q["source"] != "AI" or len(q["options"]) != 4 or
                                        sum(o["isCorrect"] for o in q["options"]) != 1 for q in generated):
                    raise RuntimeError("Provider result did not satisfy Draft validation")
                result["generatedQuestionCount"] = len(generated)
                (directory / "generated-drafts.json").write_text(json.dumps(generated, ensure_ascii=False, indent=2), encoding="utf-8")
            else:
                result["error"] = generated.get("error", "Generation failed")
            with closing(sqlite3.connect(database)) as db:
                row = db.execute('SELECT Status,RequestedQuestionCount,GeneratedQuestionCount,ChunkCount,ProviderRequestCount FROM AIGenerationLogs ORDER BY Id DESC LIMIT 1').fetchone()
                result["generationLog"] = dict(zip(("status", "requested", "generated", "chunks", "providerRequests"), row)) if row else None
                result["persistedQuestionCount"] = db.execute("SELECT COUNT(*) FROM Questions").fetchone()[0]
                if status == 200:
                    log = result["generationLog"]
                    if (not log or log["status"] != "Success" or log["generated"] != len(generated) or
                            result["persistedQuestionCount"] != len(generated) or not 1 <= log["providerRequests"] <= 2):
                        raise RuntimeError("Persisted Drafts or provider budget did not match the successful response")
                    result["passed"] = True
                elif result["persistedQuestionCount"]:
                    raise RuntimeError("Failure persisted partial questions")
        except Exception as error:
            result["passed"] = False
            # Only our fixed error messages and exception class are persisted; never raw requests or tokens.
            result["failureType"] = type(error).__name__
            if isinstance(error, RuntimeError):
                result["error"] = str(error)
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
    (directory / "result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=True, indent=2))
    print("Evidence: " + str(directory.relative_to(ROOT) / "result.json"))
    return 0 if result["passed"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
