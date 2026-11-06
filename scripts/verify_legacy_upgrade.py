"""Exercise legacy adoption + real EF migrations on an isolated copy only.

Run after dotnet build BE/LMS.sln. Optional --database is opened read-only and
copied with SQLite backup; without it a synthetic legacy test DB is created.
"""
import argparse
from contextlib import closing
import hashlib
import os
from pathlib import Path
import secrets
import sqlite3
import subprocess
import sys
import tempfile

from baseline_sqlite import REFERENCE, adopt, quoted


ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def rows(db, columns=None):
    if columns is None:
        columns = {name: [row[1] for row in db.execute(f"PRAGMA table_info({quoted(name)})")]
                   for (name,) in db.execute("SELECT name FROM sqlite_master WHERE type='table' AND substr(name,1,7)!='sqlite_' AND name NOT IN ('__EFMigrationsHistory','__EFMigrationsLock')").fetchall()}
    return columns, {name: sorted(db.execute(f"SELECT {','.join(quoted(c) for c in names)} FROM {quoted(name)}").fetchall(), key=repr)
                     for name, names in columns.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", type=Path)
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    args = parser.parse_args()
    original_hash = digest(args.database) if args.database else None
    with tempfile.TemporaryDirectory(prefix="lms-migration-") as directory:
        target = Path(directory) / "copy.sqlite"
        with closing(sqlite3.connect(target)) as copied:
            if args.database:
                with closing(sqlite3.connect(args.database.resolve(strict=True).as_uri() + "?mode=ro", uri=True)) as source:
                    source.backup(copied)
            else:
                copied.executescript(REFERENCE.read_text(encoding="utf-8-sig"))
                copied.execute('DROP TABLE "__EFMigrationsHistory"')
                copied.execute('INSERT INTO Users (Id,Username,FullName,PasswordHash,Role,IsLocked) VALUES (1,?,?,?,?,0)',
                               ("migration_fixture", "Migration fixture", "fixture-not-a-login", "Lecturer"))
                copied.execute('INSERT INTO Courses (Code,Title,Description,JoinCode,LecturerId) VALUES (?,?,?,?,1)',
                               ("MIGRATE101", "Preserved course", "Legacy description", "TEST1234"))
                copied.commit()
            columns, before = rows(copied)
        backup_path = adopt(target, apply=True)
        if backup_path is None:
            backup_path = Path(directory) / "before-migrations.bak"
            with closing(sqlite3.connect(target)) as source, closing(sqlite3.connect(backup_path)) as backup:
                source.backup(backup)
        with closing(sqlite3.connect(backup_path)) as backup:
            if rows(backup, columns)[1] != before:
                raise AssertionError("Consistent backup differs from legacy data")
        environment = os.environ.copy()
        environment.update({
            "ASPNETCORE_ENVIRONMENT": "Development", "Database__Provider": "Sqlite", "Database__AutoMigrate": "true",
            "ConnectionStrings__Sqlite": "Data Source=" + str(target), "Storage__RootPath": str(Path(directory) / "Uploads"),
            "Demo__Seed": "false", "Bootstrap__AdminUsername": "", "Jwt__Key": secrets.token_hex(48),
            "Ai__Mode": "Disabled", "Logging__LogLevel__Default": "Warning",
            "DotEnv__Enabled": "false"
        })
        result = subprocess.run(["dotnet", "run", "--project", str(ROOT / "BE/Services/Core/LMS.Core.API"),
                                 "--configuration", args.configuration, "--no-build", "--no-launch-profile", "--", "--migrate-only"],
                                cwd=ROOT, env=environment, capture_output=True, text=True, timeout=120)
        if result.returncode:
            raise RuntimeError(f"Migration process failed ({result.returncode}): {result.stderr}")
        with closing(sqlite3.connect(target)) as upgraded:
            if rows(upgraded, columns)[1] != before:
                raise AssertionError("Legacy data changed during additive migration")
            migrations = ROOT / "BE/Services/Core/LMS.Core.Infrastructure/Persistence/Migrations/Sqlite"
            expected = {file.stem for file in migrations.glob("*.cs") if file.stem[:14].isdigit() and not file.stem.endswith(".Designer")}
            applied = {row[0] for row in upgraded.execute('SELECT MigrationId FROM "__EFMigrationsHistory"')}
            if applied != expected:
                raise AssertionError("Expected every versioned SQLite migration to be applied")
            if "SecurityVersion" not in columns["Users"] and upgraded.execute('SELECT 1 FROM Users WHERE SecurityVersion != 0').fetchone():
                raise AssertionError("Incorrect SecurityVersion default")
            if "Description" not in columns["Quizzes"] and upgraded.execute("SELECT 1 FROM Quizzes WHERE Description != ''").fetchone():
                raise AssertionError("Incorrect description default")
            if upgraded.execute("PRAGMA integrity_check").fetchone()[0] != "ok" or upgraded.execute("PRAGMA foreign_key_check").fetchone():
                raise AssertionError("Upgraded database failed integrity checks")
        if args.database and digest(args.database) != original_hash:
            raise AssertionError("Original database hash changed")
        count = sum(len(table) for table in before.values())
        print(f"PASS database upgrade: consistent backup, {len(expected)} EF migrations, {len(before)} tables / {count} prior rows preserved exactly, defaults/integrity/FKs valid; original database unchanged")


if __name__ == "__main__":
    main()
