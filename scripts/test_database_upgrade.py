"""Regression tests for safe legacy SQLite adoption; fixtures use temporary databases.

Run: python -m unittest discover -s scripts -p test_database_upgrade.py -v
The workspace database is opened only through the script's default read-only path.
"""
from contextlib import closing, redirect_stdout
import hashlib
import importlib.util
import io
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "baseline_sqlite.py"
spec = importlib.util.spec_from_file_location("baseline_sqlite_under_test", SCRIPT)
baseline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(baseline)
REFERENCE_SQL = baseline.REFERENCE.read_text(encoding="utf-8-sig")


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class LegacyAdoptionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="lms-baseline-tests-")
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / "legacy.sqlite"

    def create_legacy(self, sql=REFERENCE_SQL):
        with closing(sqlite3.connect(self.path)) as db, db:
            db.executescript(sql)
            db.execute('DROP TABLE "__EFMigrationsHistory"')
            db.execute('INSERT INTO "Users" VALUES (11,?,?,?,?,?)',
                       ("fixture-lecturer", "Giảng viên dữ liệu", "fixture-hash", "Lecturer", 0))
            db.execute('INSERT INTO "Courses" VALUES (21,?,?,?,?,?)',
                       ("LEG101", "Khóa học được giữ lại", "Mô tả", "LEGACY01", 11))
            db.execute('INSERT INTO "Topics" VALUES (31,21,?)', ("Chủ đề cũ",))
            db.execute('INSERT INTO "LearningMaterials" VALUES (41,21,31,?,?,?,?)',
                       ("Bài cũ", "lesson.txt", "a" * 32 + ".txt", "Dữ liệu nguyên bản."))
        return self.path

    def adopt(self, apply=False):
        with redirect_stdout(io.StringIO()):
            return baseline.adopt(self.path, apply)

    def business_rows(self, path):
        with closing(sqlite3.connect(Path(path).as_uri() + "?mode=ro", uri=True)) as db, db:
            tables = db.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT IN ('__EFMigrationsHistory','__EFMigrationsLock') ORDER BY name").fetchall()
            return {name: db.execute("SELECT * FROM " + baseline.quoted(name) + " ORDER BY rowid").fetchall()
                    for (name,) in tables}

    def assert_rejected_without_changes(self):
        before = digest(self.path)
        with self.assertRaises((ValueError, sqlite3.Error)):
            self.adopt(apply=True)
        self.assertEqual(before, digest(self.path), "rejected database must remain byte-for-byte unchanged")
        self.assertEqual([], list(self.path.parent.glob("*.bak")))

    def test_default_verification_is_read_only(self):
        self.create_legacy()
        before = digest(self.path)
        rows = self.business_rows(self.path)
        self.assertIsNone(self.adopt())
        self.assertEqual(before, digest(self.path))
        self.assertEqual(rows, self.business_rows(self.path))
        self.assertEqual([], list(self.path.parent.glob("*.bak")))
        with closing(sqlite3.connect(self.path)) as db, db:
            self.assertIsNone(db.execute("SELECT 1 FROM sqlite_master WHERE name='__EFMigrationsHistory'").fetchone())

    def test_apply_creates_consistent_backup_and_preserves_data(self):
        self.create_legacy()
        before_rows = self.business_rows(self.path)
        backup = self.adopt(apply=True)
        self.assertIsInstance(backup, Path)
        self.assertTrue(backup.is_file())
        self.assertEqual(before_rows, self.business_rows(self.path))
        self.assertEqual(before_rows, self.business_rows(backup))
        expected, migration = baseline.reference_schema()
        with closing(sqlite3.connect(self.path)) as db, db:
            self.assertEqual(expected, baseline.signature(db))
            self.assertEqual([migration], db.execute('SELECT MigrationId,ProductVersion FROM "__EFMigrationsHistory"').fetchall())
            self.assertNotIn("SecurityVersion", [r[1] for r in db.execute('PRAGMA table_info("Users")')])
        with closing(sqlite3.connect(backup)) as db, db:
            self.assertIsNone(db.execute("SELECT 1 FROM sqlite_master WHERE name='__EFMigrationsHistory'").fetchone())

    def test_apply_is_idempotent_after_known_baseline(self):
        self.create_legacy()
        backup = self.adopt(apply=True)
        before = digest(self.path)
        self.assertIsNone(self.adopt(apply=True))
        self.assertEqual(before, digest(self.path))
        self.assertEqual([backup], list(self.path.parent.glob("*.bak")))

    def test_empty_expected_history_can_be_adopted(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,"ProductVersion" TEXT NOT NULL)')
        self.assertIsNotNone(self.adopt(apply=True))

    def test_rejects_extra_business_column(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('ALTER TABLE "Users" ADD COLUMN "Unexpected" TEXT')
        self.assert_rejected_without_changes()

    def test_rejects_missing_unique_index(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('DROP INDEX "IX_Users_Username"')
        self.assert_rejected_without_changes()

    def test_rejects_foreign_key_semantic_drift(self):
        self.create_legacy(REFERENCE_SQL.replace('ON DELETE RESTRICT', 'ON DELETE CASCADE', 1))
        self.assert_rejected_without_changes()

    def test_rejects_orphaned_rows(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('UPDATE "Courses" SET "LecturerId"=9999 WHERE "Id"=21')
        self.assert_rejected_without_changes()

    def test_rejects_custom_view(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('CREATE VIEW "UserNames" AS SELECT "Username" FROM "Users"')
        self.assert_rejected_without_changes()

    def test_rejects_custom_trigger(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('CREATE TRIGGER "CustomAudit" AFTER INSERT ON "Users" BEGIN SELECT 1; END')
        self.assert_rejected_without_changes()

    def test_rejects_extra_table_matching_old_like_wildcards(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('CREATE TABLE "ABEFShadow" ("Id" INTEGER PRIMARY KEY)')
        self.assert_rejected_without_changes()

    def test_rejects_unknown_ef_prefixed_table(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('CREATE TABLE "__EFAuxiliary" ("Id" INTEGER PRIMARY KEY)')
        self.assert_rejected_without_changes()

    def test_rejects_check_constraint_drift(self):
        self.create_legacy(REFERENCE_SQL.replace('"IsLocked" INTEGER NOT NULL', '"IsLocked" INTEGER NOT NULL CHECK ("IsLocked" IN (0,1))'))
        self.assert_rejected_without_changes()

    def test_rejects_table_collation_drift(self):
        self.create_legacy(REFERENCE_SQL.replace('"Username" TEXT NOT NULL', '"Username" TEXT COLLATE NOCASE NOT NULL'))
        self.assert_rejected_without_changes()

    def test_rejects_index_collation_drift(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('DROP INDEX "IX_Users_Username"')
            db.execute('CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username" COLLATE NOCASE)')
        self.assert_rejected_without_changes()

    def test_rejects_hidden_generated_column(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('ALTER TABLE "Users" ADD COLUMN "HiddenGenerated" TEXT GENERATED ALWAYS AS ("Username") VIRTUAL')
        self.assert_rejected_without_changes()

    def test_rejects_unknown_migration_history(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,"ProductVersion" TEXT NOT NULL)')
            db.execute('INSERT INTO "__EFMigrationsHistory" VALUES (?,?)', ("20000101000000_Unknown", "10.0.10"))
        self.assert_rejected_without_changes()

    def test_rejects_malformed_history_without_creating_backup(self):
        self.create_legacy()
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute('CREATE TABLE "__EFMigrationsHistory" ("Unexpected" INTEGER)')
        self.assert_rejected_without_changes()

    def test_missing_database_is_never_created(self):
        with self.assertRaises(FileNotFoundError):
            self.adopt(apply=True)
        self.assertFalse(self.path.exists())

    def test_non_sqlite_file_is_unchanged(self):
        self.path.write_bytes(b"Not a SQLite database")
        self.assert_rejected_without_changes()

    def test_cli_default_has_no_write_side_effect(self):
        self.create_legacy()
        before = digest(self.path)
        result = subprocess.run([sys.executable, str(SCRIPT), "--database", str(self.path)],
                                text=True, capture_output=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("VERIFIED", result.stdout)
        self.assertEqual(before, digest(self.path))
        self.assertEqual([], list(self.path.parent.glob("*.bak")))

    def test_workspace_database_default_read_only(self):
        original = ROOT / "BE" / "Services" / "Core" / "LMS.Core.API" / "lms-ai.sqlite"
        if not original.is_file():
            self.skipTest("No workspace SQLite file present")
        before = digest(original)
        result = subprocess.run([sys.executable, str(SCRIPT), "--database", str(original)],
                                text=True, capture_output=True, check=False)
        self.assertEqual(before, digest(original), "workspace database must never be changed by verification")
        self.assertEqual(0, result.returncode, result.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
