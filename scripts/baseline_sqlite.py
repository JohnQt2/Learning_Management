"""Verify an EnsureCreated SQLite database and adopt ONLY the exact legacy schema.

Default is read-only. Stop the API before --apply. No rows/files are removed.
Python stdlib only; the API itself does not require Python.
"""
import argparse
from datetime import datetime, timezone
from contextlib import closing
from pathlib import Path
import sqlite3
import sys
import re


REFERENCE = Path(__file__).resolve().parents[1] / "docs" / "sqlite-baseline.sql"


def quoted(name):
    return '"' + name.replace('"', '""') + '"'


def signature(db):
    result = {}
    tables = db.execute("SELECT name,sql FROM sqlite_master WHERE type='table' AND substr(name,1,7)!='sqlite_' AND name NOT IN ('__EFMigrationsHistory','__EFMigrationsLock')").fetchall()
    if db.execute("SELECT 1 FROM sqlite_master WHERE type IN ('trigger','view')").fetchone():
        raise ValueError("Custom views/triggers require manual review; no changes made")
    def tokens(sql):
        # Preserve string/quoted identifier contents; ignore only inter-token whitespace.
        return tuple(t if t.startswith(("'", '"', '`', '[')) else t.upper()
                     for t in re.findall(r'''"(?:""|[^"])*"|'(?:''|[^'])*'|`[^`]*`|\[[^\]]*\]|[A-Za-z_][A-Za-z_0-9]*|\d+|[^\s]''', sql or ""))

    for name, ddl in tables:
        columns = list(db.execute(f"PRAGMA table_xinfo({quoted(name)})"))
        indexes = []
        for index in db.execute(f"PRAGMA index_list({quoted(name)})").fetchall():
            index_ddl = db.execute("SELECT sql FROM sqlite_master WHERE type='index' AND name=?", (index[1],)).fetchone()
            indexes.append((index[2], index[4], tuple(db.execute(f"PRAGMA index_xinfo({quoted(index[1])})")), tokens(index_ddl[0] if index_ddl else None)))
        foreign_keys = sorted(tuple(r[2:]) for r in db.execute(f"PRAGMA foreign_key_list({quoted(name)})"))
        result[name] = (columns, sorted(indexes), foreign_keys, tokens(ddl))
    return result


def reference_schema():
    with closing(sqlite3.connect(":memory:")) as reference:
        reference.executescript(REFERENCE.read_text(encoding="utf-8-sig"))
        migration = reference.execute('SELECT MigrationId, ProductVersion FROM "__EFMigrationsHistory"').fetchone()
        return signature(reference), migration


def verify(db, expected):
    if signature(db) != expected:
        raise ValueError("Schema differs from the legacy baseline. No changes made; review migrations manually.")
    if db.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
        raise ValueError("Database integrity check failed; no changes made")
    if db.execute("PRAGMA foreign_key_check").fetchone():
        raise ValueError("Foreign key violations exist; no changes made")


def adopt(path, apply=False):
    path = Path(path).resolve(strict=True)
    expected, migration = reference_schema()
    with closing(sqlite3.connect(path.as_uri() + "?mode=ro", uri=True)) as reader:
        history = reader.execute("SELECT type FROM sqlite_master WHERE name='__EFMigrationsHistory'").fetchone()
        if history:
            columns = [(r[1], r[2], r[3], r[4], r[5]) for r in reader.execute('PRAGMA table_info("__EFMigrationsHistory")')]
            if history[0] != "table" or columns != [("MigrationId", "TEXT", 1, None, 1), ("ProductVersion", "TEXT", 1, None, 0)]:
                raise ValueError("Migration history schema is unrecognized; no changes made")
        if history and reader.execute('SELECT 1 FROM "__EFMigrationsHistory"').fetchone():
            applied = {row[0] for row in reader.execute('SELECT MigrationId FROM "__EFMigrationsHistory"')}
            migration_dir = REFERENCE.parents[1] / "BE/Services/Core/LMS.Core.Infrastructure/Persistence/Migrations/Sqlite"
            known = {p.stem for p in migration_dir.glob("*.cs") if re.match(r"\d{14}_", p.stem) and not p.stem.endswith(".Designer")}
            if migration[0] not in applied or not applied <= known:
                raise ValueError("Unrecognized migration history; manual review required, no changes made")
            print("Migration history already exists; no changes made")
            return None
        verify(reader, expected)
    if not apply:
        print("VERIFIED: exact legacy schema, integrity and foreign keys; database unchanged")
        return None
    with closing(sqlite3.connect(path, timeout=1)) as writer, writer:
        # Prevent other writers while taking the consistent backup and stamping history.
        writer.execute("BEGIN IMMEDIATE")
        verify(writer, expected)
        if writer.execute("SELECT 1 FROM sqlite_master WHERE name='__EFMigrationsHistory'").fetchone() and writer.execute('SELECT 1 FROM "__EFMigrationsHistory"').fetchone():
            raise ValueError("Migration history changed concurrently; no changes made")
        backup_path = path.with_name(path.name + "." + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ") + ".bak")
        with closing(sqlite3.connect(path.as_uri() + "?mode=ro", uri=True)) as source, closing(sqlite3.connect(backup_path)) as backup:
            source.backup(backup)
        writer.execute('CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" ("MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY, "ProductVersion" TEXT NOT NULL)')
        writer.execute('INSERT INTO "__EFMigrationsHistory" ("MigrationId","ProductVersion") VALUES (?,?)', migration)
        writer.commit()
    print(f"ADOPTED legacy baseline only; consistent backup: {backup_path}")
    print("Next: run the API in Development or run --migrate-only to apply the additive migration.")
    return backup_path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--apply", action="store_true", help="Back up and stamp verified baseline; stop API first")
    args = parser.parse_args()
    try:
        adopt(args.database, args.apply)
    except (ValueError, OSError, sqlite3.Error) as error:
        print(f"BLOCKED: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
