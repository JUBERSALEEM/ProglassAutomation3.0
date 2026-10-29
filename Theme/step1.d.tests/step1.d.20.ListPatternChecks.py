"""The shared list-page pattern: defined once per theme, followed by every module.

Portable source check; never compilation or rendered-UI evidence.

Round 50, in the owner's words: "make a global design pattern so upcoming module
pages strictly follow these shared style ... column section top panel heading
should be bold in grid line and center aligned. list related text should not have
grid line but professionally spacing and center aligned ... so each list page can
have equal width so each column fit properly ... on mouse on inside row column
full text should be visible in popup".

Before this, five modules each carried their own copy of the same grid chrome —
five header templates, five cell templates, five sets of text styles — so every
fix had to be made five times and the copies had already drifted (the project
register was left-aligned under centred headers, the client register carried a
hard-coded tooltip, three registers clipped their own timestamps).

The rules below are written to bind a page that does not exist yet: the module
views are DISCOVERED, not listed, so the day a sixth module adds a register it
has to use the pattern or this fails.
"""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
WPF = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
PROBLEMS = []


def check(condition, message):
    if not condition:
        PROBLEMS.append(message)


def read(path):
    return path.read_text(encoding="utf-8")


# ── 1. Every installed theme defines the pattern ──────────────────────────
# A theme package owns its own visuals (docs/00 rule K), so the pattern cannot
# live in one shared file: each package defines it in token terms and the tokens
# make it look like that package.
REQUIRED = {
    "ListGrid": "DataGrid",
    "ListColumnHeader": "DataGridColumnHeader",
    "ListCell": "DataGridCell",
    "ListText": "TextBlock",
    "ListMonoText": "TextBlock",
    "ListStrongText": "TextBlock",
    "ListBubble": "ToolTip",
}

packages = sorted(p for p in (ROOT / "Theme").glob("Theme*") if p.is_dir())
check(len(packages) == 9, f"expected the 9 installed packages, found {len(packages)}")

theme_definitions = {}
for package in packages:
    definitions = {}
    for dictionary in sorted(package.glob("step1.e.Components/*.xaml")):
        text = read(dictionary)
        for key, target in REQUIRED.items():
            match = re.search(rf'<Style x:Key="{key}"[^>]*TargetType="([^"]+)"', text)
            if match:
                definitions[key] = (match.group(1), text[text.index(f'x:Key="{key}"'):])
    theme_definitions[package.name] = definitions
    missing = sorted(set(REQUIRED) - set(definitions))
    check(not missing, f"{package.name}: the shared list pattern is missing {missing}")
    for key, (target, _) in definitions.items():
        check(target == REQUIRED[key],
              f"{package.name}: {key} targets {target}, not {REQUIRED[key]} — a keyed style that "
              "does not target the control it is used on is silently ignored")


def body(package, key):
    """The style's text, cut at its own closing tag."""
    entry = theme_definitions.get(package, {}).get(key)
    if entry is None:
        return ""
    text = entry[1]
    return text[:text.index("</Style>")]


# ── 2. The header band: bold, centred, ruled ──────────────────────────────
# "column section top panel heading should be bold in grid line and center
# aligned". The rule is drawn in the header and nowhere else, at the theme's own
# hairline weight and colour.
for package in theme_definitions:
    header = body(package, "ListColumnHeader")
    if not header:
        continue
    check('Property="FontWeight" Value="{DynamicResource FontWeightHeading}"' in header,
          f"{package}: the column header is not bold")
    check('Property="HorizontalContentAlignment" Value="Center"' in header,
          f"{package}: the column header is not centred")
    # The label is drawn by the template, so presence of a FontWeight setter is
    # no longer the proof. The proof is the TextBlock that actually renders it.
    check('Text="{TemplateBinding Content}"' in header,
          f"{package}: the header band leaves its label to the generated TextBlock, so the "
          "heading weight and centring depend on what the header happens to inherit")
    check('TextAlignment="Center"' in header and 'HorizontalAlignment="Stretch"' in header,
          f"{package}: the header label is not centred across the whole column, so the band's "
          "right rule hugs the text instead of sitting on the column edge")
    check('FontWeight="{DynamicResource FontWeightHeading}"' in header,
          f"{package}: the rendered header label is not heading-bold")
    check('Background="{DynamicResource BorderStrong}"' in header,
          f"{package}: the header band draws no rule — the owner asked for the heading to sit "
          "in a grid line")
    check('Width="{DynamicResource StrokeWidth}"' in header,
          f"{package}: the header rule's width is not the theme's own hairline")
    check("PART_LeftHeaderGripper" in header and "PART_RightHeaderGripper" in header,
          f"{package}: without the resize grippers the columns cannot be resized by hand")
    check('Property="BorderThickness" Value="0"' in header,
          f"{package}: the package's own header frame is still on, and its control weight "
          "(2px, 4px in some packages) boxes the band")


# ── 3. The body: no grid lines, centred, evenly sized ─────────────────────
# "list related text should not have grid line but professionally spacing and
# center aligned ... each list page can have equal width so each column fit".
for package in theme_definitions:
    grid = body(package, "ListGrid")
    if not grid:
        continue
    check('Property="GridLinesVisibility" Value="None"' in grid,
          f"{package}: the register draws body grid lines — the owner asked for none")
    check('Property="ColumnWidth" Value="*"' in grid,
          f"{package}: the register does not divide its width equally between the columns, so a "
          "column is sized by guesswork and one of them always ends up clipped")
    check('Property="MinColumnWidth" Value="{DynamicResource ControlMinWidth}"' in grid,
          f"{package}: without a floor a column can be dragged down to nothing")
    check('Property="CellStyle" Value="{StaticResource ListCell}"' in grid,
          f"{package}: the register does not carry the shared cell style")
    check('Property="ColumnHeaderStyle" Value="{StaticResource ListColumnHeader}"' in grid,
          f"{package}: the register does not carry the shared header style")
    check('VerticalGridLinesBrush=' not in grid and 'HorizontalGridLinesBrush=' not in grid,
          f"{package}: a rule-brush override is set on the register; the header carries the rules")

    cell = body(package, "ListCell")
    check('Property="HorizontalContentAlignment" Value="Center"' in cell,
          f"{package}: the cell does not centre its content")
    check('BasedOn="{StaticResource {x:Type DataGridCell}}"' in cell,
          f"{package}: the cell style does not derive from the package's own cell, so it loses "
          "the theme's selection and focus chrome")
    check('Property="BorderBrush" Value="{DynamicResource TransparentBrush}"' in cell,
          f"{package}: the cell paints a border of its own, which boxes every data row")

    value = body(package, "ListText")
    check('Property="TextAlignment" Value="Center"' in value,
          f"{package}: register values are not centred under their centred headers")
    check('Property="TextTrimming" Value="CharacterEllipsis"' in value,
          f"{package}: a long value does not trim, so it either clips mid-letter or pushes the "
          "column wider")
    check("TruncatedTextToolTip.Enabled" in value,
          f"{package}: a trimmed value shows no tooltip, so the rest of it is unreachable")
    for derived in ("ListMonoText", "ListStrongText"):
        check(f'BasedOn="{{StaticResource ListText}}"' in body(package, derived),
              f"{package}: {derived} does not derive from ListText, so it drifts from the pattern")


# ── 4. Every module register follows it ───────────────────────────────────
# Discovered, not listed: the next module's page is covered the day it lands.
#
# A register is a read-only table of records. An entry grid — the inquiry
# editor's BOQ lines, where each cell is typed into — is deliberately NOT one:
# it needs per-column widths and editable cells, so it states IsReadOnly="False"
# and is exempt. A register never has to say anything: the shared style already
# makes it read-only, which is why using that style on an entry grid is itself an
# error (the cells would stop accepting typing).
def module_views():
    views = []
    for path in sorted(ROOT.rglob("*.xaml")):
        if {"bin", "obj"} & set(path.parts):
            continue
        relative = path.relative_to(ROOT)
        if relative.parts[0] == "Theme" or relative.name in {"App.xaml", "MainWindow.xaml"}:
            continue
        if "step1.ThemeStudio" in str(relative):
            continue  # the studio's own analytics tables are not module registers
        if "<DataGrid " in read(path):
            views.append(path)
    return views


def columns_of(grid):
    holder = grid.find(WPF + "DataGrid.Columns")
    return list(holder) if holder is not None else []


views = module_views()
check(len(views) >= 5,
      f"expected the five module screens with a grid, found {len(views)}; a screen that stopped "
      "being found would silently stop being checked")

registers = 0
for path in views:
    name = path.relative_to(ROOT)
    text = read(path)
    tree = ET.parse(path).getroot()

    for grid in tree.iter(WPF + "DataGrid"):
        editable = grid.attrib.get("IsReadOnly") == "False"
        uses_pattern = "ListGrid" in grid.attrib.get("Style", "")

        if editable:
            check(not uses_pattern,
                  f"{name}: an entry grid (IsReadOnly=\"False\") uses the register style, which is "
                  "read-only — the cells would stop accepting typing")
            continue

        registers += 1
        check(uses_pattern,
              f"{name}: a read-only grid does not use the shared ListGrid style")
        if not uses_pattern:
            continue

        for attribute in ("CellStyle", "ColumnHeaderStyle"):
            check(attribute not in grid.attrib,
                  f"{name}: the register overrides {attribute} again, which is how the five modules "
                  "drifted apart")
        for local in re.finditer(r'<Style x:Key="([^"]+)" TargetType="(DataGridCell|DataGridColumnHeader|'
                                 r'DataGridRow|DataGrid)"', text):
            check(False,
                  f"{name}: the page defines its own {local.group(2)} style ({local.group(1)}); the "
                  "pattern in the theme is the only copy")

        columns = columns_of(grid)
        check(bool(columns), f"{name}: the register declares no columns")
        for column in columns:
            kind = column.tag.split("}")[-1]
            heading = column.attrib.get("Header", "")
            template = column.find(WPF + f"{kind}.Header")
            check("Width" not in column.attrib,
                  f'{name}: column "{heading}" sets Width="{column.attrib.get("Width")}"; the equal '
                  "share in ListGrid is what makes a column fit, and a fixed number is what clipped "
                  "the timestamps in the first place")
            if kind == "DataGridTextColumn":
                check(template is None,
                      f'{name}: column "{heading}" carries a hand-written header template; '
                      'Header="…" is the pattern')
                style = column.attrib.get("ElementStyle", "")
                check(any(f"List{suffix}Text" in style for suffix in ("", "Mono", "Strong")),
                      f'{name}: column "{heading}" has no pattern element style, so its value is '
                      "not centred, not trimmed and shows no tooltip")
            else:
                check(template is None,
                      f'{name}: column "{heading}" carries a hand-written header template. '
                      'The band draws Header="…" itself; a nested element is printed as its '
                      "type name and can never carry the heading weight")
                check(heading.strip() != "",
                      f'{name}: a template column has no header; an empty one reads as a broken band')

check(registers >= 5,
      f"expected at least five registers across the modules, found {registers}")

# ── 5. The tooltip is real, and it is a themed bubble ─────────────────────
# A setter that names a behaviour nobody wrote is not an error in XAML: it is a
# tooltip that never appears. Round 52 also found the reverse trap — an
# `ElementStyle="{DynamicResource …}"` is not an error either, and does nothing,
# because ElementStyle is a plain CLR property and DynamicResource can only drive
# a DependencyProperty. That is why the cells stayed left-aligned on screen while
# every source check passed. Both traps are now rules.
for path in views:
    name = path.relative_to(ROOT)
    text = read(path)
    check('ElementStyle="{DynamicResource' not in text,
          f"{name}: ElementStyle is a plain CLR property on DataGridColumn, so a "
          "DynamicResource there is silently ignored — the column keeps the default text "
          "style and nothing about the pattern reaches the cell. Use StaticResource.")
    for column in re.finditer(r"<DataGridTextColumn\b[^>]*ElementStyle=\"(\{StaticResource (\w+)\})\"", text):
        check("List" in column.group(2),
              f"{name}: a column's element style is {column.group(2)}, not one of the pattern's")

behaviour = ROOT / "Theme/step1.g.Customization/step1.g.8.TruncatedTextToolTip.cs"
check(behaviour.is_file(), "the truncation tooltip behaviour is gone")
if behaviour.is_file():
    source = read(behaviour)
    # The bubble is set on the ToolTip property, so WPF's tooltip service opens
    # and closes it. A ToolTip element in that position with StaysOpen="False"
    # makes the service throw NotSupportedException on the dispatcher, i.e. the
    # first time the pointer rests on a clipped cell — a fatal log entry for a
    # cosmetic feature. Nothing in this file may set it.
    import re as _re
    code = _re.sub(r"//[^\n]*", "", _re.sub(r"/\*.*?\*/", "", source, flags=_re.S))
    check("StaysOpen" not in code,
          "the truncation tooltip sets StaysOpen. WPF refuses a ToolTip element assigned to the "
          "ToolTip property when StaysOpen is false and throws on the dispatcher as soon as the "
          "pointer hovers a clipped cell")
    for part in ("EnabledProperty", "MouseEnter", "SizeChanged", "IsClipped", "FormattedText",
                 "block.ToolTip = Build(block)"):
        check(part in source, f"the truncation tooltip no longer {part!r} — it cannot measure the "
                              "text it would have to reveal")
    check("block.ActualWidth" in source,
          "the behaviour no longer compares the text against the width it was given")
    check("BubbleStyleKey" in source and "TryFindResource(BubbleStyleKey)" in source,
          "the tooltip is no longer built from the theme's bubble style, so it is drawn by the "
          "system and ignores the theme")
    check("Style = style" in source,
          "the bubble is created without its theme style")

for package in theme_definitions:
    bubble = body(package, "ListBubble")
    if not bubble:
        check(False, f"{package}: the value bubble style is missing, so a clipped cell falls back "
                     "to a system tooltip")
        continue
    check('Property="MaxWidth" Value="{DynamicResource PopupMaxWidth}"' in bubble,
          f"{package}: the bubble has no width cap, so a long value becomes a strip")
    check('TargetType="ToolTip"' in bubble and "ToolTip" in bubble,
          f"{package}: the bubble does not target ToolTip")
    check('Property="TextWrapping" Value="Wrap"' in bubble,
          f"{package}: the bubble does not wrap, so the full value is clipped a second time")
    check('Property="TextTrimming" Value="None"' in bubble,
          f"{package}: the bubble inherits the cell's trimming and would clip the value it exists "
          "to reveal")


if PROBLEMS:
    print(f"list pattern checks: {len(PROBLEMS)} failure(s).")
    for problem in PROBLEMS:
        print("  FAIL: " + problem)
    sys.exit(1)

print(f"PASS: the shared list pattern is defined in all {len(packages)} packages and every "
      f"{len(views)} module register follows it — bold ruled centred headers, a body with no grid "
      "lines, an equal width per column, and a tooltip on anything a cell had to trim.")
