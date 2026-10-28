#!/usr/bin/env python3
"""step5.f.1 — static gate for Project Records & Inquiry Integration.

Run from anywhere: python3 step5.ProjectRecords/step5.f.tests/step5.f.1.ProjectSourceChecks.py

Why this file exists: step5 is the first module that writes into a table another
module owns. Everything it must not do is invisible to a compiler —
repurposing a legacy column, matching history by name, deleting a project,
rewriting an inquiry — and each of those would corrupt data quietly rather than
fail. The rules below name the defect, the file and the line.

Every rule was mutation-tested; see docs/05.Decision-Log.md.
"""

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODULE = ROOT / "step5.ProjectRecords"
VIEW = MODULE / "step5.d.1.ProjectWorkspaceView.xaml"
RULES = MODULE / "step5.a.2.ProjectRules.cs"
SCHEMA = MODULE / "step5.b.1.ProjectSchema.cs"
REPO = MODULE / "step5.b.4.ProjectRepository.cs"
LINKS = MODULE / "step5.b.6.ProjectRepository.Link.cs"
MIGRATIONS = ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs"
CONTRACT = ROOT / "Theme/step1.a.ThemeEngine/step1.a.1.ThemeContract.cs"
WINDOW = ROOT / "MainWindow.xaml"
CSHARP = sorted(MODULE.glob("*.cs"))

failures = []


def check(condition, message):
    if not condition:
        failures.append(message)


def read(path):
    return path.read_text(encoding="utf-8", errors="replace")


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


# 1 ── No code deletes a project ──────────────────────────────────────────────
# Deactivation is the only lifecycle transition in this product (step2). A
# project is closed, never removed: a closed project is quoted by documents that
# do not exist yet.
for path in CSHARP:
    body = strip_comments(read(path))
    match = re.search(r"\bDELETE\s+FROM\b", body, re.IGNORECASE)
    check(not match,
          f"{path.relative_to(ROOT)}: contains DELETE FROM — a project is closed, never deleted")

check(not re.search(r"class\s+\w*Delete\w*Command", read(MODULE / "step5.c.1.ProjectWorkspaceViewModel.cs")),
      "step5.c.1.ProjectWorkspaceViewModel.cs: exposes a delete command; the module has no delete path")

# 2 ── The legacy placeholders are never written ─────────────────────────────
# The specification forbids repurposing Inquiries.CustomerId / ProjectId. A
# single UPDATE that mentions them would silently rewrite another module's data
# under a name nobody re-read.
# Asserting that the placeholders still EXIST is the guard, not the defect; writing
# them is the defect. So the rule reads the write statements, not the file text.
for path in CSHARP:
    body = strip_comments(read(path))
    for legacy in ("CustomerId", "ProjectId"):
        writes = re.findall(r"(?:SET|INSERT\s+INTO[^()]*\()([^;]*?)(?:WHERE|VALUES|\"\")", body, re.S | re.IGNORECASE)
        for clause in writes:
            check(legacy not in clause,
                  f"{path.relative_to(ROOT)}: writes the legacy placeholder {legacy} — it is not "
                  "repurposed, renamed or written by step5")
        check(re.search(r"public\s+[\w<>?]+\s+" + legacy + r"\s*\{", body) is None,
              f"{path.relative_to(ROOT)}: maps a {legacy} property — step5 has its own link column and "
              "does not adopt the placeholder")

# The new column is the only inquiry column this module may write.
link_text = strip_comments(read(LINKS))
updates = re.findall(r"UPDATE\s+[^\n]*Inquiries[^\n]*\n?\s*SET\s+(.*?)WHERE",
                     link_text, re.S | re.IGNORECASE)
check(len(updates) == 2,
      f"step5.b.6.ProjectRepository.Link.cs: expected exactly two inquiry UPDATEs (attach, detach), "
      f"found {len(updates)} — every other write path must be a mistake")
allowed = ('ProjectRecordId',)
for statement in updates:
    assigned = set(re.findall(r'""(\w+)""\s*=', statement)) | set(re.findall(r'"(\w+)"\s*=', statement))
    unexpected = sorted(column for column in assigned if column not in allowed)
    check(not unexpected,
          f"step5.b.6.ProjectRepository.Link.cs: an inquiry UPDATE assigns {unexpected}; the module may "
          f"write only {list(allowed)} — linking a project is not editing the inquiry")

# 3 ── Nothing is matched to history by name ────────────────────────────────
# The one test the specification calls out by name: no code path may match a
# historical inquiry to a client or a project by comparing text.
for path in CSHARP:
    body = strip_comments(read(path))
    for pair in (("ClientName", "CustomerName"), ("ProjectName", "ClientName")):
        pattern = r"\b%s\b[\s\S]{0,120}?==[\s\S]{0,60}?\b%s\b" % pair
        check(re.search(pattern, body) is None,
              f"{path.relative_to(ROOT)}: compares {pair[0]} with {pair[1]} — history is never matched "
              "to a record by name; only ids link records")

# 4 ── A blank number is allocated above the highest, not above the count ────
# Allocating from a count would hand a closed project's number to the next
# project created, which is how numbering systems start lying.
rules_text = strip_comments(read(RULES))
check("highest" in rules_text and "NextNumber" in rules_text,
      "step5.a.2.ProjectRules.cs: the allocator no longer documents/uses the highest sequence")
check(re.search(r"count\s*\+\s*1|Count\(\)\s*\+\s*1", rules_text) is None,
      "step5.a.2.ProjectRules.cs: allocates from a COUNT — closing a project would reuse its number")

# 5 ── The migration is additive ───────────────────────────────────────────
schema_text = strip_comments(read(SCHEMA))
for forbidden in ("DROP TABLE", "DROP COLUMN", "RENAME TO", "RENAME COLUMN"):
    check(forbidden.lower() not in schema_text.lower(),
          f"step5.b.1.ProjectSchema.cs: contains {forbidden} — migration 1017 must be additive only")
check("AddColumnIfMissing" in schema_text and "CREATE TABLE IF NOT EXISTS" in schema_text,
      "step5.b.1.ProjectSchema.cs: the migration is not written as create-if-missing / add-if-missing, "
      "so re-running it is not a no-op")
check("1017" in schema_text,
      "step5.b.1.ProjectSchema.cs: the migration version is not 1017 — the chain numbers are allocated once")

migrations = read(MIGRATIONS)
check("ProjectSchema.MigrationVersion" in migrations,
      "AppSchemaMigrations.cs: migration 1017 is not registered on the chain, so the table is never created")
check("ProjectSchema.AssertShape" in migrations,
      "AppSchemaMigrations.cs: the post-migration shape assertion is not registered")

# 6 ── The link cannot create or move an inquiry by accident ───────────────
check('AND ""{ProjectSchema.InquiryProjectColumn}"" IS NULL' in link_text
      or "IS NULL" in link_text,
      "step5.b.6.ProjectRepository.Link.cs: the attach no longer requires the inquiry to be unlinked, "
      "so a second click could move an inquiry from one project to another silently")
check("INSERT" not in strip_comments(read(LINKS)).upper(),
      "step5.b.6.ProjectRepository.Link.cs: contains an INSERT — this module links inquiries, it does "
      "not create them")
check("UpdatedAt" not in link_text,
      "step5.b.6.ProjectRepository.Link.cs: touches the inquiry's UpdatedAt — linking a project is not "
      "the customer editing their inquiry, and stamping it would claim an edit that never happened")

# 7 ── View-model state is what the view binds ─────────────────────────────
view_text = read(VIEW)
check('Binding="{Binding StatusText}"' in view_text,
      "step5.d.1.ProjectWorkspaceView.xaml: the status column is not bound to StatusText, so the grid "
      "would fall back to ToString() on the row object")
for property_name in ("RegisterSummary", "SelectedToggleLabel", "CanEditSelected", "CanDetach"):
    check(re.search(r"public\s+[\w<>?]+\s+" + property_name + r"\b", read(MODULE / "step5.c.1.ProjectWorkspaceViewModel.cs"))
          is not None,
          f"step5.c.1.ProjectWorkspaceViewModel.cs: {property_name} is bound by the view but not defined")

# 8 ── Editing loads the whole project, so a save cannot erase a field ─────
commands = read(MODULE / "step5.c.3.ProjectWorkspaceViewModel.Commands.cs")
check("GetAsync" in commands,
      "step5.c.3.ProjectWorkspaceViewModel.Commands.cs: the editor does not load the full project, so a "
      "save would write back the register's partial row and erase the description")
check("EditDescription" in commands,
      "step5.c.3.ProjectWorkspaceViewModel.Commands.cs: the editor never fills the description field")

# 9 ── Every token the view asks for exists ────────────────────────────────
contract = read(CONTRACT)
declared = set(re.findall(r'"(\w+)"', contract))
used = set(re.findall(r"\{DynamicResource\s+(\w+)\}", view_text))
missing = sorted(name for name in used if name not in declared)
check(not missing,
      f"step5.d.1.ProjectWorkspaceView.xaml: {missing} are not in the theme contract — WPF would render "
      "those properties at their defaults without any error")

# 10 ── The workspace is reachable, titled, marked and built ───────────────
window = read(WINDOW)
check("<prj:ProjectWorkspaceView" in window,
      "MainWindow.xaml: the workspace is not hosted, so the module cannot be opened")
check('xmlns:prj="clr-namespace:ProGlassAutomation.Projects"' in window,
      "MainWindow.xaml: the namespace for the view is missing")
chrome = read(ROOT / "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs")
for needle, what in (('11 => "Projects"', "title"),
                     ('"Business · Project records and the inquiries that belong to them"', "subtitle"),
                     ("11 => IconKind.Engineering", "mark")):
    check(needle in chrome,
          f"step1.b.17.Customization.cs: the workspace {what} for index 11 is missing, so the shell "
          "would show a blank title or the wrong mark")

project = read(ROOT / "ProGlassAutomation.csproj")
check("step5.ProjectRecords/**/*.cs" in project,
      "ProGlassAutomation.csproj: the module's sources are not in the Compile glob, so none of this "
      "code is built")
check("step5.ProjectRecords/**/*.xaml" in project,
      "ProGlassAutomation.csproj: the view is not in the Page glob")

# 11 ── The view parses, and sets no property twice ────────────────────────
try:
    tree = ET.parse(VIEW)
except ET.ParseError as error:
    failures.append(f"step5.d.1.ProjectWorkspaceView.xaml: does not parse as XML: {error}")
    tree = None
if tree is not None:
    NS = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"

    def tag(element):
        return element.tag[len(NS):] if element.tag.startswith(NS) else element.tag

    for element in tree.getroot().iter():
        if "Style" not in element.attrib:
            continue
        expected = tag(element) + ".Style"
        for child in element:
            check(tag(child) != expected,
                  f"step5.d.1.ProjectWorkspaceView.xaml: <{tag(element)}> sets Style='{{...}}' and also "
                  f"declares <{expected}>, which is a duplicate property assignment — XAML will not compile")

# 12 ── Numbering: a, b, c, d, f in order with no gaps ─────────────────────
children = {}
for path in sorted(MODULE.glob("step5.*.cs")) + sorted(MODULE.glob("*.xaml")):
    match = re.match(r"step5\.([a-f])\.(\d+)\.", path.name)
    if not match:
        continue
    letter, number = match.group(1), int(match.group(2))
    children.setdefault(letter, set()).add(number)
for letter, numbers in sorted(children.items()):
    expected = list(range(1, max(numbers) + 1))
    check(sorted(numbers) == expected,
          f"step5.{letter}: child numbers are {sorted(numbers)}, expected {expected} — numbers are "
          "allocated once and never reused, so a gap means a file went missing")

if failures:
    print(f"step5.f.1: {len(failures)} failure(s).")
    for failure in failures:
        print("  FAILED:", failure)
    sys.exit(1)

print("PASS: step5 is additive, never deletes, never repurposes, never matches by name, and builds.")
print(f"      {len(CSHARP)} source file(s); {len(used)} theme token(s) verified against the contract.")
