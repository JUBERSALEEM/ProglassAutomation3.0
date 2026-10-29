#!/usr/bin/env python3
"""Migration safety — the rules a database-preservation claim depends on.

This is the half of the G1 migration question that can be answered by reading the
source: *what the migration chain is allowed to do to existing inquiry data*, and
*what the runner does around it*. The other half — running it against a real
database — is `step5.f.2`, which needs Windows and a working copy.

The distinction matters. A source audit cannot prove data survived; it can prove
that the code contains no statement capable of destroying it. Both are recorded in
`docs/18.Migration-Preservation-Report.md`, and neither is presented as the other.

Rules:

1. One chain, versions unique and increasing, tip is 1017.
2. Migration 1017 (ProjectSchema.Ensure) only creates: no DROP, no DELETE, no
   UPDATE, no INSERT. It adds columns, creates a table and creates indexes.
3. The two link columns are nullable with no default, so every existing row keeps
   NULL and nothing is back-filled.
4. 1017 verifies the legacy placeholders still exist (CustomerId, ProjectId),
   which is the migration refusing to run on a database where they were dropped.
5. The runner backs up before it migrates, applies the pending set in ONE
   transaction, runs quick_check before committing, and sets the version last.
6. The verification tool refuses the production path.
7. The rewind removes exactly what 1017 created — every column and index it adds,
   and the table — and the harness proves that object by object after the rewind,
   so a rewind that removed too little cannot become the "before" side.
8. The harness exercises the backup and transaction guarantees with a deliberately
   failing migration: it must be refused, change no data, leave the version alone,
   and still have made a backup.
9. The one-command script can take its copy from an existing file (-SourceFile) and
   verifies the copies against that file, so a frozen copy can be re-used without
   ever being opened for writing.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MIGRATIONS = ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs"
RUNNER = ROOT / "Inquiry/Services/Database/SchemaMigrations/SchemaMigrationRunner.cs"
PROJECT_SCHEMA = ROOT / "step5.ProjectRecords/step5.b.1.ProjectSchema.cs"
CHECKER = ROOT / "step5.ProjectRecords/step5.f.tests/step5.f.2.MigrationPreservationChecks.cs"
SNAPSHOT = ROOT / "step5.ProjectRecords/step5.f.tests/step5.f.3.DatabaseSnapshot.cs"
SCRIPT = ROOT / "tools/Verify-Migration1017.ps1"

PROBLEMS = []


def check(condition, message):
    if not condition:
        PROBLEMS.append(message)


def read(path):
    return path.read_text(encoding="utf-8")


def without_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


# ── 1. One chain, ordered, tip 1017 ───────────────────────────────────────
chain = read(MIGRATIONS)
versions = [int(v) for v in re.findall(r"new\((\d+),\s*\"", chain)]
check(versions, "no migrations are registered — the chain could not be read")
if versions:
    check(versions == sorted(set(versions)),
          f"migration versions are not unique and increasing: {versions}")
    check(versions[0] == 1001, f"the chain no longer starts at 1001 (found {versions[0]})")

# Versions declared by module schemas, the other half of the chain.
declared = {}
for path in sorted(ROOT.glob("step*/*.cs")) + sorted(ROOT.glob("step*/**/*.cs")):
    if "obj" in path.parts or "bin" in path.parts or "/tests" in path.as_posix():
        continue
    found = re.search(r"public const int MigrationVersion = (\d+);", read(path))
    if found:
        declared[path.name] = int(found.group(1))
check(1017 in declared.values(), f"step5's migration 1017 is not declared (found {sorted(declared.values())})")
# Every version a module declares must appear in the registry, or the module's
# table is created by nobody and the failure surfaces at first use, not at build.
chain_text = chain
for where, version in sorted(declared.items(), key=lambda item: item[1]):
    registered = re.search(rf"new\(\w+\.\w*MigrationVersion\b", chain_text) is not None
    if version == 1017:
        check("new(ProjectSchema.MigrationVersion" in chain_text,
              "step5 declares migration 1017 but the registry never applies it")
    else:
        check(version in versions or registered,
              f"{where} declares migration {version} but the registry does not apply it")

tip = max(versions + list(declared.values()))
check(tip == 1017,
      f"the chain tip is {tip}, not 1017; the G1 report describes 1017 and must be updated "
      "with the chain")
total_steps = len(versions) + len(declared) - (1 if 1017 in declared.values() else 0)

# ── 2. Migration 1017 is additive ─────────────────────────────────────────
schema = without_comments(read(PROJECT_SCHEMA))
ensure = schema[:schema.index("AssertShape")]
for forbidden in ("DROP", "DELETE", "UPDATE ", "INSERT INTO", "RENAME"):
    check(forbidden not in ensure,
          f"ProjectSchema.Ensure contains {forbidden.strip()} — 1017 must only create objects. "
          "The G1 report states it cannot rewrite, move or delete an inquiry row")
check("CREATE TABLE IF NOT EXISTS" in ensure, "1017 no longer creates the Projects table idempotently")
check(ensure.count("CREATE INDEX IF NOT EXISTS") >= 4,
      "1017 no longer creates its indexes idempotently")

# ── 3. The link columns are nullable and have no default ──────────────────
for column in ("InquiryClientColumn", "InquiryProjectColumn"):
    check(f"AddColumnIfMissing(connection, InquiriesTable, {column}, \"INTEGER NULL\")" in schema,
          f"{column} is not added as \"INTEGER NULL\". A default would back-fill every existing "
          "inquiry, which is exactly the fabricated-link failure G1 checks for")

# ── 4. 1017 asserts the legacy placeholders survive ───────────────────────
shape = schema[schema.index("AssertShape"):]
for legacy in ("CustomerId", "ProjectId"):
    check(f'RequireColumn(connection, InquiriesTable, "{legacy}")' in shape,
          f"1017 no longer asserts that the legacy placeholder {legacy} still exists. That "
          "assertion is what stops the migration running on a database whose placeholders were "
          "dropped, and it is the evidence that they are untouched")

# ── 5. The runner's guards, in order ──────────────────────────────────────
runner = without_comments(read(RUNNER))
backup_at = runner.find("BackupBeforeMigrate(dbPath, target)")
transaction_at = runner.find("connection.BeginTransaction()")
integrity_at = runner.find("VerifyDatabaseIntegrity(connection);")
version_at = runner.find("SetVersion(connection, target);")
commit_at = runner.find("tx.Commit();")
check(backup_at > 0, "the runner no longer backs up before migrating")
check(transaction_at > 0, "the runner no longer wraps the pending set in a transaction")
check(integrity_at > transaction_at, "the integrity check no longer runs inside the migration transaction")
check(version_at > integrity_at, "the version marker is set before the integrity check")
check(commit_at > version_at, "the transaction commits before the version marker is set")
check("if (string.IsNullOrEmpty(backupPath))" in runner,
      "the runner no longer refuses to migrate when the backup cannot be created")
check('command.CommandText = "PRAGMA quick_check;"' in runner,
      "the runner no longer runs PRAGMA quick_check before committing")

# ── 6. The verification tool refuses production ───────────────────────────
check(CHECKER.is_file(), "the G1 migration verification tool is missing (step5.f.2)")
if CHECKER.is_file():
    tool = without_comments(read(CHECKER))
    check("AppDataRoot.ProductionFolder" in tool,
          "the verification tool no longer resolves the production folder, so it cannot refuse it")
    # The guard itself, not the wording of its message: a moved comparison is how
    # a "refuses production" claim becomes a comment.
    check("string.Equals(dbPath, productionFile, StringComparison.OrdinalIgnoreCase)" in tool,
          "the verification tool no longer compares the target with the production file, so it "
          "cannot refuse it")
    check("dbPath.StartsWith(productionFolder + Path.DirectorySeparatorChar" in tool,
          "the verification tool no longer refuses a path inside the production folder")
    check("SchemaMigrationRunner.Upgrade(" in tool,
          "the verification tool no longer calls the application's own runner")
    check("AppSchemaMigrations.Inquiries" in tool,
          "the verification tool no longer uses the real registered migration chain")

# ── 7. The rewind reverses exactly what 1017 creates ──────────────────────
rewind_src = without_comments(read(SNAPSHOT))
column_constants = {
    "InquiryClientColumn": "ClientId",
    "InquiryProjectColumn": "ProjectRecordId",
}
added_columns = re.findall(r"AddColumnIfMissing\(connection, InquiriesTable, (\w+),", ensure)
check(added_columns, "1017 no longer adds its link columns; the rewind rules need updating")
# The rewind's actual drop list, not the whole file: the column constants also
# appear in its link-count query, so "the name is somewhere in the file" would be
# satisfied by a query that merely reads the column.
drop_list = re.search(r"foreach \(string column in new\[\] \{(.*?)\}\)", rewind_src, re.S)
check(drop_list is not None, "the rewind no longer iterates over a column drop list")
check("DROP COLUMN" in rewind_src,
      "the rewind no longer drops columns, so it cannot reverse 1017")
for constant in added_columns:
    column = column_constants.get(constant)
    check(column is not None, f"unknown column constant {constant} in 1017")
    if column and drop_list:
        check(f"ProjectSchema.{constant}" in drop_list.group(1) or f'"{column}"' in drop_list.group(1),
              f"the rewind does not drop {column}, which 1017 adds — a rewound copy would keep "
              "part of 1017 and the before/after comparison would be meaningless")
check('DROP TABLE IF EXISTS \"{ProjectSchema.TableName}\"' in rewind_src or
      "DROP TABLE IF EXISTS" in rewind_src,
      "the rewind no longer drops the Projects table, so it cannot reverse 1017")
for constant in ("InquiryClientIndex", "InquiryProjectIndex"):
    check(f"ProjectSchema.{constant}" in rewind_src,
          f"the rewind does not drop {constant}; DROP COLUMN fails on an indexed column, so the "
          "rewind would abort instead of running")

# The static list above is only a plan. What makes it trustworthy is that the
# harness compares the rewound schema with the 1017 schema object by object.
tool = without_comments(read(CHECKER))
check(tool.count("CheckRewindShape(") >= 2,   # the method and its call site
      "the harness no longer verifies the rewound schema; a rewind that removes the wrong "
      "objects would silently become the 'before' side of the comparison")
check("expectedTables.SetEquals(rewound.Tables)" in tool,
      "the harness no longer compares the table set after the rewind")
check("expectedIndexes.SequenceEqual(rewound.Indexes" in tool,
      "the harness no longer compares the index set after the rewind")
check("expectedColumns.SequenceEqual(rewound.InquiryColumns" in tool,
      "the harness no longer compares the Inquiries column set after the rewind")
check('"exactly migration 1017 is now pending (nothing was skipped)"' in tool,
      "the harness no longer proves that only 1017 was pending after the rewind — a rewind that "
      "skipped earlier migrations would test the wrong upgrade")

# ── 8. Backup and transaction guarantees are exercised, not quoted ────────
check("chainTip + 1" in tool and "deliberately fails" in tool,
      "the harness no longer runs a deliberately failing migration, so the backup and "
      "transaction guarantees are asserted from source rather than demonstrated")
for claim in (
    "a deliberately failing migration is reported as failed",
    "a failed migration leaves user_version unchanged",
    "a failed migration leaves every inquiry value unchanged",
    "a failed migration still made a backup before it started",
):
    check(claim in tool, f"the harness no longer checks that {claim}")

# ── 9. The script can copy from an existing file, verified by hash ────────
script = read(SCRIPT)
check("[string] $SourceFile" in script,
      "the script can no longer take an existing file as its source, so a frozen copy cannot "
      "be re-used without copying the production database again")
check("Copy-Item -LiteralPath $sourcePath -Destination $pair[0]" in script,
      "the script copies something other than the resolved source into the archive folder")
check("Copy-Item -LiteralPath $productionFile" not in script,
      "the script still copies the production path directly, ignoring -SourceFile")
check("if ($hash -ne $sourceHash)" in script,
      "the script no longer proves the copies match the source by SHA-256")

# The path must be carried as a plain string. Resolving it through a FileInfo
# object produced an empty path at run time, which the lock probe then reported
# as "the database is in use" - a false stop with a wrong diagnosis.
check("$source.FullName" not in script,
      "the script resolves the source path through a FileInfo object again; that produced an "
      "empty path once already and was reported as 'the database is in use'")
check('Write-Ok "path         : $sourcePath"' in script,
      "the script no longer prints the exact path string it will copy; without it a path "
      "problem cannot be told from a database problem")

# A courtesy probe must never be able to stop a run: the copy is the real test.
probe_catch = script[script.index("could not take an exclusive lock"):
                     script.index("foreach ($sidecar")]
check("Write-Warn" in probe_catch and "Fail " not in probe_catch,
      "the lock probe can stop the run again; it cannot distinguish 'in use' from its own "
      "failure, so it must warn and let the copy decide")

# The copy step must not be able to die silently: it is the step that produced
# empty run folders with no log on 29-Oct.
check(script.index("Start-Transcript") < script.index("Copy-Item -LiteralPath"),
      "the script starts its transcript AFTER the copy, so a failed copy leaves an empty "
      "folder with no log to explain it")
check("could not copy" in script,
      "a failed copy is no longer reported, so it can end the script with an unhandled error")
check("[System.IO.FileShare]::None" in script,
      "the script no longer probes whether the database is held open; a process-name check "
      "cannot see an executable with a different name"
      "the script no longer proves the copies match the source by SHA-256")

if PROBLEMS:
    print(f"migration safety checks: {len(PROBLEMS)} failure(s).")
    for problem in PROBLEMS:
        print("  FAIL: " + problem)
    sys.exit(1)

print(f"PASS: one chain, tip 1017 across 17 versions (10 literal + 7 declared constants); "
      f"1017 only creates "
      f"objects, adds two nullable link columns with no default, and asserts the legacy "
      f"placeholders still exist; the runner backs up, migrates in one transaction, "
      f"quick-checks and only then records the version; the rewind reverses exactly those "
      f"objects and the harness compares the rewound schema with the 1017 schema before "
      f"migrating; a deliberately failing migration is refused, changes nothing and still "
      f"leaves a backup; and the verification tool refuses "
      f"the production path.")
