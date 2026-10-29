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
# A DynamicResource may name a token (declared by the contract) or a style
# (declared by the theme dictionaries). Both are resolved the same way at
# runtime, and an undeclared name is silent: the property keeps its default, so
# the page renders with no error and no styling. The register pattern added the
# second kind, so the rule now checks both instead of dropping the check.
contract = read(CONTRACT)
declared = set(re.findall(r'"(\w+)"', contract))
for dictionary in ROOT.glob('Theme/Theme*/step1.e.Components/*.xaml'):
    declared |= set(re.findall(r'x:Key="(\w+)"', read(dictionary)))
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

# 13 ── Reachable: a page with no route is not a feature ──────────────────
# Round 39: the shell's visible navigation is the sidebar rail, not the tab
# headers (MainWindow.xaml collapses them deliberately). step5 added its TabItem
# and stopped there, and SelectedWorkspace silently refused index 11 — so the
# module existed, built, passed every rule above, and could not be opened. These
# rules tie the three things that have to agree: the tab strip, the index guard,
# and a rail entry that actually sets the index.
window = read(WINDOW)
entry_view = read(ROOT / "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.33.ShellNavEntry.xaml")
chrome = read(ROOT / "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs")

tab_starts = [match.start() for match in re.finditer(r"<TabItem\s", window)]
projects_tab = re.search(r'<TabItem\s+Header="_Projects"', window)
check(projects_tab is not None,
      "MainWindow.xaml: there is no _Projects tab in the workspace tab strip")
if projects_tab is not None and tab_starts:
    projects_at = projects_tab.start()
    index = sum(1 for start in tab_starts if start < projects_at)
    check('prj:ProjectWorkspaceView' in window[projects_at:projects_at + 120],
          "MainWindow.xaml: the _Projects tab does not host ProjectWorkspaceView")

    guard = re.search(r"if \(value is >= 0 and <= (\d+)\)", chrome)
    check(guard is not None,
          "step1.b.17.Customization.cs: the workspace index guard is not in the expected form")
    if guard is not None:
        bound = int(guard.group(1))
        check(bound >= index,
              f"step1.b.17.Customization.cs: the workspace index guard stops at {bound}, so setting "
              f"SelectedWorkspace = {index} is silently ignored and the Projects page can never open")
        check(bound == len(tab_starts) - 1,
              f"step1.b.17.Customization.cs: the guard admits up to {bound} but the tab strip has "
              f"{len(tab_starts)} pages (highest index {len(tab_starts) - 1})")

    check("public bool IsProjects" in chrome,
          "step1.b.17.Customization.cs: IsProjects is missing, so the rail entry has no active state")
    check("Changed(nameof(IsProjects))" in chrome,
          "step1.b.17.Customization.cs: IsProjects is never re-raised, so the rail never highlights")
    check("public ICommand ShowProjects" in chrome,
          "step1.b.17.Customization.cs: ShowProjects is missing")
    check(f"ShowProjects = new DelegateCommand(() => SelectedWorkspace = {index})" in chrome,
          "step1.b.17.Customization.cs: ShowProjects is declared but never initialised, so clicking "
          "the rail entry would throw")

    check('Command="{Binding ShowProjects}" IsSelected="{Binding IsProjects}"' in window,
          "MainWindow.xaml: no rail row opens Projects — the tab headers are collapsed, so without "
          "it the page is unreachable from the running application")

# ── No implicit selection ──────────────────────────────────────────────
# Owner, round 43: the inquiry panel was open with nothing clicked. The register
# selected its first row on every load, and CanUseInquiryLink is satisfied by any
# selection at all. A panel that opens by itself claims a project nobody chose.
load_text = read(MODULE / "step5.c.2.ProjectWorkspaceViewModel.Load.cs")
check("SelectedRow = keepId.HasValue" in load_text,
      "step5.c.2: the register still selects a row on load, so the inquiry panel opens before the "
      "operator clicks anything (round 43)")
check(": Rows.FirstOrDefault();" not in load_text.replace("? Rows.FirstOrDefault(row => row.Id == keepId.Value)", ""),
      "step5.c.2: the register falls back to the first row when nothing is kept, which is the "
      "implicit selection that opened the panel by itself")

# The rail entry must look like its neighbours: same base setters, same four
# selected-state cases. It is a copy (a UserControl is parsed before it joins the
# window, so it cannot resolve MainWindow's StaticResource), and a copy that is
# not checked is a copy that drifts.
def setters(text):
    base = text.split("<Style.Triggers>")[0]
    return dict(re.findall(r'Property="([A-Za-z.]+)"\s+Value="([^"]*)"', base))

shell_nav = re.search(r'<Style x:Key="ShellNavButton".*?</Style>', window, re.S)
check(shell_nav is not None, "MainWindow.xaml: the ShellNavButton style is gone")
if shell_nav is not None:
    original, copy = setters(shell_nav.group(0)), setters(entry_view)
    check(original == copy,
          "step1.b.33.ShellNavEntry.xaml: the rail entry's style no longer matches ShellNavButton — "
          f"differs on {sorted(set(original.items()) ^ set(copy.items()))}")

for flag in ("IsSwiss", "IsHolographic", "IsGlass"):
    check(f'Binding="{{Binding {flag}}}"' in entry_view,
          f"step1.b.33.ShellNavEntry.xaml: the {flag} selected state is missing, so this rail entry "
          "would be the only one that looks wrong in that theme")
    check(f'Binding="{{Binding {flag}}}"' in window,
          f"MainWindow.xaml: {flag} is used by no sibling rail entry — the theme flag was renamed")

# 14 ── Every command the view model offers is reachable from the page ───
# Round 40: AttachInquiryCommand — the whole point of the module — was declared,
# initialised, and bound in no view, so an inquiry could be taken out of a
# project but never put into one. A command nobody can invoke is dead code that
# looks like a feature.
view_text = read(VIEW)
# A command can be reached two ways: bound in the view, or executed by the
# view's code-behind on an event the view raises. The second is how ADV feeds
# SelectSheetCommand from a row click (ClickToCommandBehavior), and it is how
# SelectProjectCommand is fed here — the rule is "reachable", not "bound".
view_code_text = read(VIEW.with_suffix(VIEW.suffix + ".cs"))
view_model_text = "\n".join(read(path) for path in CSHARP)
declared = re.findall(r"public ICommand (\w+) \{ get; \}", view_model_text)
check(len(declared) >= 8, f"the view model declares only {len(declared)} commands — parameter map is wrong")
for command in declared:
    bound = (f"{{Binding {command}}}" in view_text
             or f"DataContext.{command}" in view_text
             or f"{{Binding {command}," in view_text
             or command in view_code_text)
    check(bound,
          f"step5.c: {command} is declared and initialised but bound in no control — the operator "
          "cannot reach it, and a dead command reads as a finished feature")

# 15 ── Selecting a row opens the inquiry panel, and the panel must know ───
# The gates below are what enable the panel's lists and buttons. They are
# computed from the selected row, so the setter for the selected row has to
# re-raise every one of them; otherwise the panel stays disabled after a click
# and keeps telling the operator to select the row they just selected.
selection = re.search(r"public ProjectListRow\? SelectedRow.*?\n        \}", view_model_text, re.S)
check(selection is not None, "step5.c: the SelectedRow property is not in the expected form")
if selection is not None:
    for gate in ("CanUseInquiryLink", "CanAttach", "CanDetach", "HasSelection"):
        check(f"OnPropertyChanged(nameof({gate}))" in selection.group(0),
              f"step5.c: SelectedRow does not raise {gate}, so a binding on it never sees the click")

# 16 ── The header row reads as a table, and says where each column ends ───
# Owner, round 40: "why are there no grid lines in the top columns?" Round 50
# settled it as a page-level rule: the header band is ruled, and the body is not.
# The rules are drawn by the shared header template from the theme's own
# tokens, so every module draws the same hairline at the same weight.
view_tree = ET.parse(VIEW).getroot()
WPF = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
grids = [element for element in view_tree.iter() if element.tag == WPF + "DataGrid"]
check(len(grids) == 3, f"expected 3 grids in the module view, found {len(grids)}")
for grid in grids:
    check("ListGrid" in grid.attrib.get("Style", ""),
          "step5.d.1.ProjectWorkspaceView.xaml: a grid does not use the shared register style, so "
          "it has no ruled header band and no equal-width columns")
    check(grid.attrib.get("ColumnHeaderStyle", "") == "",
          "step5.d.1.ProjectWorkspaceView.xaml: a grid overrides the shared header style, which is "
          "how the five modules drifted apart in the first place")

# 17 ── The inquiry panel is ABSENT until a project row is clicked ───────
# Owner, round 41: "why this is not hidden by default, it should open when this
# list entry clicked." Round 44, with a screenshot: "centre should be full screen
# with left side sidebar… right side section should not be visible by default."
# Hiding the panel's *contents* was not enough: the card kept its 420 px column,
# so the register never had the full width. The CARD itself must collapse.
WPF_NS = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
parents = {child: parent for parent in view_tree.iter() for child in parent}


def gate_of(element):
    """The element's own Style text, if that Style collapses by default.
    A style set as a property element is tagged `ContentControl.Style`."""
    for child in element:
        if child.tag.endswith(".Style") or child.tag == WPF_NS + "Style":
            return " ".join(ET.tostring(child, encoding="unicode").split())
    return ""


def gating_ancestor(element):
    node = element
    while node is not None:
        text = gate_of(node)
        if "CanUseInquiryLink" in text:
            return node
        node = parents.get(node)
    return None


linked_grid = next((e for e in view_tree.iter()
                    if e.tag == WPF_NS + "DataGrid"
                    and "LinkedInquiries" in e.attrib.get("ItemsSource", "")), None)
check(linked_grid is not None, "the linked-inquiry grid is gone")
if linked_grid is not None:
    panel = gating_ancestor(linked_grid)
    check(panel is not None,
          "step5.d.1.ProjectWorkspaceView.xaml: the inquiry panel is not collapsed until a project "
          "is selected, so it is visible by default")
    if panel is not None:
        # It must be the CARD that collapses — the thing that occupies width —
        # not an inner block leaving an empty 420 px column behind.
        # The gated element must be the cell wrapper itself, and the width-carrying
        # card must live inside it — a gate on an inner block would leave an empty
        # 420 px column behind, which is exactly what round 43 shipped.
        check(parents.get(panel) is not None and parents[panel].tag == WPF_NS + "Grid",
              "step5.d.1.ProjectWorkspaceView.xaml: the gated element is not the cell wrapper of the "
              "panel column, so collapsing it cannot free the width")
        card = next((e for e in panel.iter()
                     if e.tag == WPF_NS + "ContentControl" and "Width" in e.attrib), None)
        check(card is not None,
              "step5.d.1.ProjectWorkspaceView.xaml: the gated block does not contain the panel card")
        card_style = gate_of(panel)
        check('Property="Visibility" Value="Collapsed"' in card_style,
              "step5.d.1.ProjectWorkspaceView.xaml: the panel card does not start collapsed")
        check('Binding="{Binding CanUseInquiryLink}" Value="True"' in card_style,
              "step5.d.1.ProjectWorkspaceView.xaml: the panel card is gated on something other than "
              "the project selection")
        if card is not None:
            check("Card}" in card.attrib.get("Style", ""),
                  "step5.d.1.ProjectWorkspaceView.xaml: the panel lost the card chrome when it was "
                  "gated; the card's own style must stay dynamic")
        # Everything the panel owns must live inside that one gate.
        inside = {id(e) for e in panel.iter()}
        candidate = next((e for e in view_tree.iter()
                          if e.tag == WPF_NS + "DataGrid"
                          and "CandidateInquiries" in e.attrib.get("ItemsSource", "")), None)
        check(candidate is not None and id(candidate) in inside,
              "step5.d.1.ProjectWorkspaceView.xaml: the unlinked list is outside the gated card")
        for command in ("AttachInquiryCommand", "DetachInquiryCommand"):
            holder = next((e for e in panel.iter() if command in str(e.attrib)), None)
            check(holder is not None,
                  f"step5.d.1.ProjectWorkspaceView.xaml: the {command} control is outside the gated "
                  "card, so it is offered with no project selected")

# The column the card sits in must be Auto, or collapsing it still reserves width.
row_of_panel = None
for element in view_tree.iter():
    if element.tag == WPF_NS + "Grid":
        columns = [c for c in element if c.tag.endswith("ColumnDefinitions")]
        if columns:
            widths = [d.attrib.get("Width") for d in columns[0]
                      if d.tag.endswith("ColumnDefinition")]
            if any(d.attrib.get("Grid.Column") == "1" for d in element) and widths:
                if any("LinkedInquiries" in str(c.attrib) for c in element.iter()):
                    row_of_panel = widths
check(row_of_panel is not None,
      "step5.d.1.ProjectWorkspaceView.xaml: the register and the panel no longer share a grid")
if row_of_panel is not None:
    check(row_of_panel[-1] == "Auto",
          f"step5.d.1.ProjectWorkspaceView.xaml: the panel's column is {row_of_panel[-1]}, not Auto, "
          "so the register does not take the full width when the panel is hidden")

# The hint is dead once the card itself is hidden — nothing may sit in its place.
hint = next((e for e in view_tree.iter()
             if any("Select a project to see" in str(value) for value in e.attrib.values())
             or "Select a project to see" in "".join(e.itertext())), None)
check(hint is None,
      "step5.d.1.ProjectWorkspaceView.xaml: the select-a-project hint is back; it can only be seen "
      "from inside a panel that is meant to be absent")

# 17b ── Nothing selects a row except a click ────────────────────────────
# A selection is what opens the panel. So no code path may select a row on the
# operator's behalf — not on load, and not after a save.
load_text = read(MODULE / "step5.c.2.ProjectWorkspaceViewModel.Load.cs")
check("SelectedRow = keepId.HasValue" in load_text,
      "step5.c.2: the register still selects a row on load, so the inquiry panel opens before the "
      "operator clicks anything")
commands_text = read(MODULE / "step5.c.3.ProjectWorkspaceViewModel.Commands.cs")
check("ReloadAsync(result.Project" not in commands_text,
      "step5.c.3: a save selects the row it wrote, which pops the inquiry panel open on a row "
      "nobody clicked (round 44)")

# 18 ── A disposed CancellationTokenSource is never used again ───────────
# Owner screenshot, round 42: "The project could not be saved: The
# CancellationTokenSource has been disposed." The save had SUCCEEDED; the
# refresh behind it cancelled a source the previous refresh had already
# disposed. The defect is ownership: the field must be cleared before the
# source is disposed, so nothing can reach it afterwards.
load_text = read(MODULE / "step5.c.2.ProjectWorkspaceViewModel.Load.cs")
check("_loadCts?.Cancel()" not in load_text,
      "step5.c.2: a cancellation source is cancelled straight out of the field — it may already be "
      "disposed, which throws ObjectDisposedException (round 42)")
check("_loadCts = null;" in load_text,
      "step5.c.2: the field is never cleared, so a disposed source stays reachable")
for part in ("var previous = _loadCts;", "catch (ObjectDisposedException)"):
    check(part in load_text,
          f"step5.c.2: the cancellation handover is missing '{part}' — cancel and dispose must be "
          "guarded, and the field cleared first")
finally_block = re.search(r"finally\s*\{.*?\n            \}", load_text, re.S)
check(finally_block is not None, "step5.c.2: the load's finally block is not in the expected form")
if finally_block is not None:
    body = finally_block.group(0)
    clears = body.find("_loadCts = null;")
    disposes = body.find("cts.Dispose();")
    check(clears != -1 and (disposes == -1 or clears < disposes),
          "step5.c.2: the source is disposed while still referenced by the field")

# 19 ── A save that succeeded is never reported as a failed save ─────────
# The row is already written when the refresh runs. If the refresh throws, the
# operator must not be told the save failed — they would retype and duplicate.
commands = read(MODULE / "step5.c.3.ProjectWorkspaceViewModel.Commands.cs")
save = re.search(r"public async Task SaveAsync\(\).*?\n        \}", commands, re.S)
check(save is not None, "step5.c.3: SaveAsync is not in the expected form")
if save is not None:
    body = save.group(0)
    check(body.count("try") >= 2,
          "step5.c.3: SaveAsync refreshes the register inside the same try as the write, so a "
          "refresh failure is reported as a save failure")
    check("could not be refreshed" in body,
          "step5.c.3: SaveAsync has no sentence for 'saved, but the register could not be refreshed'")
    check('AppLog.Error("step5.projects.refresh-after-save"' in body,
          "step5.c.3: a refresh failure after a successful save is not logged")

# 20 ── A second click on the open row closes the inquiry panel ─────────
# Owner, round 48, pointing at the right-hand panel: "single click open this,
# next click close this… go to sheet store module there is a list page when i
# click on it sidebar open of details… and when i again click on the list then
# this sidebar close."
#
# ADV does exactly this, and this rule holds the same shape:
#   SheetStoreViewModel.Part1.cs:338   SelectSheetCommand toggles
#                                      SelectedSheet = ReferenceEquals(...) ? null : sheet
#   SheetStoreView.xaml:439            ClickToCommandBehavior on the ROW feeds it
#   Part1.cs:248                       IsRowSelected => SelectedSheet != null
# A DataGrid does NOT behave this way by itself: clicking the selected row
# changes nothing, so without the toggle the panel would stay open forever.
register = next((e for e in view_tree.iter()
                 if e.tag == WPF_NS + "DataGrid"
                 and "Rows" in e.attrib.get("ItemsSource", "")), None)
check(register is not None, "the register grid is gone")
if register is not None:
    check("SelectedItem" in register.attrib,
          "step5.d.1.ProjectWorkspaceView.xaml: the register's selection is not bound, so nothing can "
          "drive the panel")

    # The click belongs to the ROW, as it does in ADV — not to the grid, where a
    # header or scrollbar click would be indistinguishable from a row click.
    row_style = next((e for e in register
                      if e.tag.endswith("RowStyle")), None)
    check(row_style is not None,
          "step5.d.1.ProjectWorkspaceView.xaml: the register has no RowStyle, so no row can report a "
          "click and the panel can never be closed by clicking the list again")
    if row_style is not None:
        setters = [e for e in row_style.iter() if e.tag.endswith("EventSetter")]
        check(any(s.attrib.get("Event") == "PreviewMouseLeftButtonDown" for s in setters),
              "step5.d.1.ProjectWorkspaceView.xaml: the row does not listen for a click, so the panel "
              "opens but never closes")
        check(any(s.attrib.get("Handler") == "OnRegisterRowClick" for s in setters),
              "step5.d.1.ProjectWorkspaceView.xaml: the row's click handler is not OnRegisterRowClick")

view_code = read(VIEW.with_suffix(VIEW.suffix + ".cs"))
check("private void OnRegisterRowClick(object sender, MouseButtonEventArgs e)" in view_code,
      "step5.d.1.ProjectWorkspaceView.xaml.cs: the row's click handler is not implemented")
check("SelectProjectCommand" in view_code,
      "step5.d.1.ProjectWorkspaceView.xaml.cs: the click does not go through SelectProjectCommand, so "
      "the toggle rule is not the one the view model owns")
check("toggle.Execute(project)" in " ".join(view_code.split()),
      "step5.d.1.ProjectWorkspaceView.xaml.cs: the handler names the command but never executes it — "
      "the click would do nothing at all")
check("e.Handled = true;" in view_code,
      "step5.d.1.ProjectWorkspaceView.xaml.cs: the click must be consumed, or the grid re-selects the "
      "row that was just closed and the panel reopens at once")

vm = read(MODULE / "step5.c.1.ProjectWorkspaceViewModel.cs")
check("public ICommand SelectProjectCommand { get; }" in vm,
      "step5.c.1: there is no SelectProjectCommand — the register click has nothing to invoke")
check("SelectProjectCommand = new DelegateCommand<ProjectListRow>" in vm,
      "step5.c.1: SelectProjectCommand is declared but never initialised, so a click would throw")
toggle = re.search(r"public void ToggleSelection\(ProjectListRow\? row\)\s*\{\s*([^}]+)\}",
                   " ".join(vm.split()), re.S)
check(toggle is not None, "step5.c.1: ToggleSelection is gone — nothing can close the panel")
if toggle is not None:
    body = toggle.group(1)
    check("ReferenceEquals(row, _selectedRow)" in body,
          "step5.c.1: the toggle does not compare the clicked row with the open one")
    check("? null : row" in body,
          "step5.c.1: clicking the open row does not clear the selection, so the panel cannot close")

# 21 ── The register uses the shared list pattern, not a copy of it ──────
# Round 49 asked for centring; round 50 asked for one pattern instead of five
# per-module copies. The styles now live in the theme (ListGrid / ListText and
# its two variants) and every module names them, so the alignment cannot drift
# between modules and a new list page gets it for free.
view_src = read(VIEW)
grid_count = view_src.count('Style="{DynamicResource ListGrid}"')
check(grid_count == 3,
      f"step5.d.1: {grid_count} of the module's 3 grids use the shared register style; the rest "
      "fall back to WPF defaults and lose the header band, the centring and the equal widths")
for binding, style in (("ProjectNumber", "ListMonoText"), ("ProjectName", "ListStrongText"),
                       ("ClientText", "ListText"), ("StatusText", "ListText"),
                       ("UpdatedText", "ListMonoText"), ("InquiryNumber", "ListMonoText"),
                       ("CustomerName", "ListText"), ("ReceivedText", "ListMonoText")):
    column = re.search(rf'<DataGridTextColumn[^>]*Binding="{{Binding {binding}}}"[^>]*', view_src)
    check(column is not None, f"step5.d.1: the {binding} column is gone")
    if column is not None:
        check(f'ElementStyle="{{DynamicResource {style}}}"' in column.group(0),
              f"step5.d.1: {binding} does not use {style}, so its cell is not centred, not trimmed "
              "and shows no tooltip")

# 22 ── No column carries a hand-typed width ────────────────────────────
# The pattern gives every column an equal share (ColumnWidth="*"), so a value is
# never clipped by a pixel count that guessed wrong — which is exactly what a
# 130px timestamp column did (rounds 46 and 49). A width here would override the
# share and bring the clipping back.
widths = re.findall(r'<DataGridTextColumn[^>]*\bWidth="([^"]+)"', view_src)
check(not widths,
      f"step5.d.1: {sorted(set(widths))} — a column still sets its own width, which overrides the "
      'shared equal-width rule; drop the attribute and let ListGrid size the column')

if failures:
    print(f"step5.f.1: {len(failures)} failure(s).")
    for failure in failures:
        print("  FAILED:", failure)
    sys.exit(1)

print("PASS: step5 is additive, never deletes, never repurposes, never matches by name, and builds.")
print(f"      {len(CSHARP)} source file(s); {len(used)} theme token(s) verified against the contract.")
