"""step1.d.18 — the control-spacing contract, checked everywhere it can break.

Why this exists (owner report, round 30b)
----------------------------------------
The Company page's header put a state chip, a reload button and "Save company"
8 px apart. The pills are 36–40 px tall, so 8 px reads as touching, and the same
8 px came with a 4 px bottom margin that pushed the row off centre. The owner's
question was the right one: *what in the code makes every new page do this?*

Two things did:

  * `MarginInline` is a TEXT token (8 px right, 4 px bottom). It was carrying
    every button row in the application: 24 clickable surfaces in horizontal rows
    used it, 14 more had no margin at all.
  * There was no rule. Each page spaced its own controls from memory.

A third gap had no token at all (owner report, round 31): a Grid has no gap
property, so two or three FIELDS in one row were placed in adjacent star columns
with, at most, a 4 px bottom margin — their boxes shared an edge. `UiGapFieldInline`
(16 left, 12 bottom) is the gutter for a field that follows another in its row.

The fix is a token per job and a gate that refuses the old shape:

  UiGapControl     left-anchored row: one 16 px gap to the RIGHT of each item
  UiGapControlEnd  right-anchored row: one 16 px gap to the LEFT of each item,
                   so the last control stays flush with the card edge
  UiGapFieldInline a field that follows another in the same Grid row: 16 left,
                   12 bottom; the first field in a row keeps MarginSection

What is checked
---------------
  1. every theme package declares both tokens, with the same value, and neither
     side of the gutter is under 12 px — 8 px between filled pills is the defect;
  2. no Button / RadioButton / CheckBox / ToggleButton is spaced with the text
     token anywhere;
  3. inside a horizontal StackPanel or WrapPanel, every surface (control or chip
     Border) carries the row's token, and the anchor decides which one;
  4. two fields side by side in one Grid row are separated by the field gutter —
     the field after the first carries `UiGapFieldInline`.

Grandfathered files
-------------------
The Theme Studio screens and the Inquiry module predate this rule and the owner's
standing instruction is not to restyle them. They are listed below with their
current counts: the counts may fall, never rise. Any file NOT listed here must
have zero violations, so the next module is covered from its first line.

Exit code 0 = pass. Portable: python3, no .NET SDK, no Windows.
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
# The layout scale lives in each package's Light and Dark dictionary; the
# *.Extras.xaml files carry decoration only and do not define it.
THEMES = sorted(path for path in ROOT.glob("Theme/Theme*/Theme*.xaml")
                if "UiGapCluster" in path.read_text(encoding="utf-8", errors="ignore"))

SCAN_FOLDERS = ("Inquiry", "step1.ThemeStudio", "step2.ClientManagement",
                "step3.UserManagement", "step4.CompanyProfile")
SHELL = ROOT / "MainWindow.xaml"

SURFACE = ("Button", "RadioButton", "CheckBox", "ComboBox", "ToggleButton", "TextBox", "Border")
CLICKABLE = ("Button", "RadioButton", "CheckBox", "ToggleButton")

# Frozen at round 30b. These may shrink; they may not grow.
GRANDFATHERED = {
    "Inquiry/Views/InquiryEditorView.xaml": 23,
    "Inquiry/Views/InquiryInboxView.xaml": 19,
    "Inquiry/Views/InquiryListView.xaml": 11,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.12.PreviewWindow.xaml": 5,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.14.WorklistPanel.xaml": 2,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.16.CustomizationPanel.xaml": 65,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.20.ChartGallery.xaml": 1,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.22.CustomizeWorkspace.xaml": 8,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.26.TextsStudioView.xaml": 14,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.27.ColorsStudioView.xaml": 12,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.30.IconsStudioView.xaml": 6,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.32.PagesStudioView.xaml": 10,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.8.ControlsPanel.xaml": 10,
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.9.TypeAndStatusPanel.xaml": 5,
}

FAILURES = []


def check(condition, message):
    if not condition:
        FAILURES.append(message)


def read(path):
    return path.read_text(encoding="utf-8", errors="ignore")


def attrs_of(tag):
    return dict(re.findall(r'([A-Za-z_][\w.:]*)\s*=\s*"([^"]*)"', tag))


def token_of(value):
    m = re.fullmatch(r"\{(?:Dynamic|Static)Resource ([A-Za-z0-9_.]+)\}", value or "")
    return m.group(1) if m else (value if value else None)


def read_tag(text, i):
    quote = None
    j = i + 1
    while j < len(text):
        c = text[j]
        if quote:
            if c == quote:
                quote = None
        elif c in "\"'":
            quote = c
        elif c == ">":
            return j + 1, text[i:j + 1]
        j += 1
    raise ValueError("unterminated tag")


def elements(text):
    """Every element with its parent, in document order.

    A regex over attributes is not enough: the defect lives in the RELATIONSHIP
    between a row and its children, and only a small scanner can see that.
    """
    stack, found, i = [], [], 0
    while i < len(text):
        if text.startswith("<!--", i):
            i = text.index("-->", i) + 3
            continue
        if text[i] == "<":
            end, raw = read_tag(text, i)
            if raw.startswith("</"):
                name = raw[2:-1].strip().split(":")[-1]
                if stack and stack[-1]["name"] == name:
                    stack.pop()
                i = end
                continue
            if raw.startswith("<!") or raw.startswith("<?"):
                i = end
                continue
            name = re.match(r"<([A-Za-z_][\w.:]*)", raw).group(1).split(":")[-1]
            element = {"name": name, "attrs": attrs_of(raw),
                       "parent": stack[-1] if stack else None}
            found.append(element)
            if not raw.rstrip().endswith("/>"):
                stack.append(element)
            i = end
            continue
        i += 1
    return found


def is_row(el):
    if el["name"] == "WrapPanel":
        return (el["attrs"].get("Orientation") or "Horizontal") == "Horizontal"
    return el["name"] == "StackPanel" and el["attrs"].get("Orientation") == "Horizontal"


def inside_button(el):
    node = el
    while node is not None:
        if node["name"] in CLICKABLE:
            return True
        node = node["parent"]
    return False


INPUTS = ("TextBox", "ComboBox", "PasswordBox", "DatePicker")


def holds_input(element, found, depth=0):
    """True when this element is, or directly wraps, an input control."""
    if element["name"] in INPUTS:
        return True
    if depth >= 2:
        return False
    return any(holds_input(child, found, depth + 1) for child in found.get(id(element), ()))


def local(name):
    """`Grid.ColumnDefinitions` is a property element, not a child of type Grid."""
    return name.split(".")[-1]


def field_gutters(found):
    """(element, reason) for two fields in one Grid row with no gutter between."""
    problems = []
    children = {}
    for element in found:
        children.setdefault(id(element["parent"]), []).append(element)

    for grid in found:
        if local(grid["name"]) != "Grid":
            continue
        kids = children.get(id(grid), ())
        columns = 0
        for kid in kids:
            if local(kid["name"]) == "ColumnDefinitions":
                columns += sum(1 for c in children.get(id(kid), ())
                               if local(c["name"]) == "ColumnDefinition")
        if columns < 2:
            continue

        rows = {}
        for kid in kids:
            if local(kid["name"]) == "ColumnDefinitions":
                continue
            row = int(kid["attrs"].get("Grid.Row") or 0)
            column = int(kid["attrs"].get("Grid.Column") or 0)
            rows.setdefault(row, []).append((column, kid))

        for row, items in rows.items():
            items.sort(key=lambda item: item[0])
            fields = [kid for _, kid in items if holds_input(kid, children)]
            if len(fields) < 2:
                continue
            for kid in fields[1:]:
                if token_of(kid["attrs"].get("Margin")) != "UiGapFieldInline":
                    problems.append((kid["name"],
                                     "shares a Grid row with another field and no gutter "
                                     "(needs UiGapFieldInline)"))
    return problems


def violations(path):
    """The list of (element, reason) that break the rule in one file."""
    found = []
    for el in elements(read(path)):
        if el["name"] in CLICKABLE and token_of(el["attrs"].get("Margin")) == "MarginInline":
            found.append((el["name"], "spaced with the text token MarginInline"))
        parent = el["parent"]
        if parent is None or not is_row(parent) or el["name"] not in SURFACE:
            continue
        if inside_button(parent):
            continue
        wanted = "UiGapControlEnd" if (parent["attrs"].get("HorizontalAlignment") or "") == "Right" \
            else "UiGapControl"
        actual = token_of(el["attrs"].get("Margin"))
        if actual != wanted:
            found.append((el["name"], f"in a horizontal row without {wanted} (has {actual})"))
    return found


def require_tokens():
    check(len(THEMES) == 18, f"expected 18 theme dictionaries, found {len(THEMES)}")
    values = {}
    for path in THEMES:
        text = read(path)
        for token, side in (("UiGapControl", 2), ("UiGapControlEnd", 0),
                            ("UiGapFieldInline", 0)):
            match = re.search(r'x:Key="%s">([\d.,\s]+)</Thickness>' % token, text)
            if not match:
                check(False, f"{path.name}: the contract is missing {token}")
                continue
            parts = [p.strip() for p in match.group(1).split(",")]
            check(len(parts) == 4, f"{path.name}: {token} must be a four-sided thickness")
            try:
                gap = float(parts[side])
            except (ValueError, IndexError):
                check(False, f"{path.name}: {token} is not numeric")
                continue
            check(gap >= 12,
                  f"{path.name}: {token} gives {gap} px between controls; under 12 px "
                  "two filled pills read as touching (the round-30b defect)")
            values.setdefault(token, set()).add(tuple(parts))
    for token, seen in values.items():
        check(len(seen) == 1,
              f"{token} must mean one thing in every theme, found {sorted(seen)}")


# The field gutter is a round-31 rule; the two Inquiry views predate it and the
# owner's standing instruction is not to restyle that module. Frozen at round 31.
FIELD_GRANDFATHERED = {
    "Inquiry/Views/InquiryInboxView.xaml": 4,
    "Inquiry/Views/InquiryListView.xaml": 2,
}


def require_field_gutters():
    counts = {}
    for path in targets():
        found = field_gutters(elements(read(path)))
        if found:
            counts[relative(path)] = found

    for name, found in sorted(counts.items()):
        allowed = FIELD_GRANDFATHERED.get(name)
        if allowed is None:
            for element, reason in found:
                check(False, f"{name}: <{element}> {reason}")
        else:
            check(len(found) <= allowed,
                  f"{name}: {len(found)} field row(s) without a gutter, up from the "
                  f"frozen {allowed} — a grandfathered file may get better, never worse")
    return counts


def require_spacing():
    counts, details = {}, {}
    for path in targets():
        found = violations(path)
        if found:
            counts[relative(path)] = len(found)
            details[relative(path)] = found

    for name, count in sorted(counts.items()):
        allowed = GRANDFATHERED.get(name)
        if allowed is None:
            for element, reason in details[name]:
                check(False, f"{name}: <{element}> {reason}")
        else:
            check(count <= allowed,
                  f"{name}: {count} spacing violation(s), up from the frozen {allowed} — "
                  "a grandfathered file may get better, never worse")
    return counts


def targets():
    paths = []
    for folder in SCAN_FOLDERS:
        paths += sorted((ROOT / folder).rglob("*.xaml"))
    paths.append(SHELL)
    return paths


def relative(path):
    return str(path.relative_to(ROOT))


def main():
    require_tokens()
    counts = require_spacing()
    require_field_gutters()

    if FAILURES:
        print(f"layout spacing checks: {len(FAILURES)} failure(s).")
        for failure in FAILURES:
            print("  FAIL: " + failure)
        return 1

    frozen = sum(GRANDFATHERED.values())
    print(f"layout spacing checks: PASS ({len(THEMES)} theme dictionaries, "
          f"{len(targets())} XAML files; {frozen} legacy surface(s) still frozen "
          f"in {len(GRANDFATHERED)} file(s); "
          f"{sum(FIELD_GRANDFATHERED.values())} legacy field row(s) in "
          f"{len(FIELD_GRANDFATHERED)} file(s)).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
