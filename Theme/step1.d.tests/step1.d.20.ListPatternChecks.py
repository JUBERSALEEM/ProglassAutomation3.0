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


def analytics_of(package):
    """The package's analytics dictionary, the one that defines the register."""
    path = ROOT / "Theme" / package / "step1.e.Components/step1.e.10.Analytics.xaml"
    return read(path)


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
    # Precedence, learned the hard way: the heading is BOLD, and the literal is
    # what makes it bold. FontWeightHeading resolves to SemiBold in every package,
    # which is the weight the owner looked at and called "not the heading type
    # bold". A token that reads as heading is not the same thing as bold type.
    check('Property="FontWeight" Value="Bold"' in header,
          f"{package}: the column header is not bold — a heading weight token "
          "(FontWeightHeading = SemiBold) is not bold")
    check('Property="HorizontalContentAlignment" Value="Center"' in header,
          f"{package}: the column header is not centred")
    # And the centring has to be the framework's own mechanism. A hand-written
    # header template that re-creates the presenter is how the label rendered
    # flush-left while this very setter was present. The master app's working
    # header style carries no Template at all: the framework's template lays the
    # label out with HorizontalContentAlignment.
    check('Property="Template"' not in header,
          f"{package}: the header style carries a hand-written template. The framework's "
          "template is the one that honours HorizontalContentAlignment — a re-created "
          "presenter has now rendered the heading flush left twice")
    check('PART_LeftHeaderGripper' not in header,
          f"{package}: a hand-written header template has come back, and with it the gripper "
          "juggling that the framework template does for free")
    # And neither may the package's own implicit header style. The register's
    # style derives from it, so a template there is inherited by every register:
    # this one had a presenter with no horizontal alignment at all, which is why
    # the label stayed flush left no matter what the register asked for.
    # The implicit header style is what every other grid in the product uses, so
    # it carries the same ruling: bold type, not the SemiBold heading token.
    implicit_header = re.search(r'<Style TargetType="DataGridColumnHeader">(.*?)</Style>',
                                analytics_of(package), re.S)
    check(implicit_header is not None,
          f"{package}: the package has no implicit column-header style")
    if implicit_header is not None:
        check('Property="FontWeight" Value="Bold"' in implicit_header.group(1),
              f"{package}: the package's own column headers are not bold — every grid outside the "
              "register uses this style")
    check('ControlTemplate TargetType="DataGridColumnHeader"' not in analytics_of(package),
          f"{package}: the package's implicit column-header style carries a template of its own. "
          "The register derives from it, so that template wins and the framework's — the one that "
          "honours HorizontalContentAlignment — never runs")    # The rules are the header's own border, one hairline on the trailing and
    # bottom edges — what the master app's header style does with its
    # BorderThickness. Strong enough to read as a grid line, thin enough not to
    # box the band.
    check('Property="BorderBrush" Value="{DynamicResource BorderStrong}"' in header,
          f"{package}: the header band draws no rule — the owner asked for the heading to sit "
          "in a grid line")
    check('Property="BorderThickness" Value="0,0,1,1"' in header,
          f"{package}: the header's rules are not a single hairline on the trailing and bottom "
          "edges; either the package's own 2px/4px frame boxes the band or there is no rule")
    # The rules are the header's own border, one hairline on the trailing and
    # bottom edges — what the master app's header style does with its
    # BorderThickness. Strong enough to read as a grid line, thin enough not to
    # box the band.
    check('Property="BorderBrush" Value="{DynamicResource BorderStrong}"' in header,
          f"{package}: the header band draws no rule — the owner asked for the heading to sit "
          "in a grid line")
    check('Property="BorderThickness" Value="0,0,1,1"' in header,
          f"{package}: the header's rules are not a single hairline on the trailing and bottom "
          "edges; either the package's own 2px/4px frame boxes the band or there is no rule")


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

    # The presenter is STRETCH, not the cell's HorizontalContentAlignment. A
    # centred presenter shrinks to the text, so the stretched TextBlock inside it
    # can never be wider than its own glyphs and TextAlignment=Center has nothing
    # to centre — the value renders at the left edge, with no error anywhere.
    check('<ContentPresenter HorizontalAlignment="Stretch"' in cell,
          f"{package}: the cell's presenter does not take the whole column, so a centred value "
          "has nothing to centre inside and renders at the left edge")
    check('HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"' not in cell,
          f"{package}: the cell's presenter is aligned by the cell again — that is the "
          "shrink-to-text box that made every centred value render left")

    value = body(package, "ListText")
    check('Property="TextAlignment" Value="Center"' in value,
          f"{package}: register values are not centred under their centred headers")
    check('Property="TextTrimming" Value="CharacterEllipsis"' in value,
          f"{package}: a long value does not trim, so it either clips mid-letter or pushes the "
          "column wider")
    check('Property="ToolTip" Value="{Binding Text, RelativeSource={RelativeSource Self}}"' in value,
          f"{package}: a trimmed value shows no tooltip, so the rest of it is unreachable. The "
          "value binds its own text to ToolTip — the master app's pattern, and the only one that "
          "opens immediately with no measuring and no dispatcher work")
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
                # The cell's presenter is full width now, so a chip or a button in
                # a template column must place itself: without this the badge
                # stretches across the column instead of sitting centred in it.
                cell_template = column.find(WPF + f"{kind}.CellTemplate")
                root = None
                if cell_template is not None:
                    data = cell_template.find(WPF + "DataTemplate")
                    if data is not None and len(data):
                        root = data[0]
                check(root is not None and root.attrib.get("HorizontalAlignment") == "Center",
                      f'{name}: column "{heading}" puts a template in a full-width cell without '
                      'HorizontalAlignment="Center" on its root, so the chip or button stretches '
                      "across the column instead of sitting centred under its heading")

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

# ── 6. The hover bubble is a binding, and the pattern needs no code ────────
# Round 52 shipped a behaviour: it measured every cell on layout and again on
# mouse-enter, built a ToolTip object per hover, and — because it assigned that
# object to the ToolTip property while also claiming StaysOpen — could throw
# NotSupportedException from WPF's tooltip service on the dispatcher. The owner's
# words for it were "slow and ugly"; the log said FATAL.
#
# The master app (ProGlassAutomationADV) does the whole job in markup:
#   TextTrimming="CharacterEllipsis" ToolTip="{Binding …}"
# with ONE implicit ToolTip style for the whole product (AppToolTipStyle:
# wrapped, MaxWidth-capped, theme surface, theme foreground). Nothing measures,
# nothing intercepts the mouse, and the bubble is open before the pointer settles.
check(not (ROOT / "Theme/step1.g.Customization/step1.g.8.TruncatedTextToolTip.cs").exists(),
      "the truncation tooltip behaviour is back. It measured every cell on layout and on "
      "mouse-enter and built a ToolTip object per hover, which is what made the pointer slow; "
      "the value style binds its own text to ToolTip instead")

behaviour_words = re.compile(r"TruncatedTextToolTip|StaysOpen\s*=\s*\"?[Ff]alse")
for path in list((ROOT / "Theme").rglob("*.cs")) + list((ROOT / "Theme").rglob("*.xaml")):
    if "obj" in path.parts or "bin" in path.parts:
        continue
    hit = behaviour_words.search(path.read_text(encoding="utf-8"))
    check(hit is None,
          f"{path.relative_to(ROOT)}: {hit.group(0) if hit else ''} — nothing may measure a cell "
          "for a tooltip, and no ToolTip element may claim StaysOpen=false. WPF's tooltip service "
          "owns the lifetime of a tooltip assigned to the ToolTip property and throws when it "
          "does not")

# The one tooltip style: implicit, in the package's popup dictionary, wrapped and
# width-capped. Every package already had it — round 52 added a second, keyed
# copy for the register, which is how a product ends up with two tooltips.
for package in packages:
    popup = package / "step1.e.Components/step1.e.6.PopupsAndScroll.xaml"
    check(popup.is_file(), f"{package.name}: the package's popup dictionary is missing")
    if not popup.is_file():
        continue
    text = read(popup)
    match = re.search(r"<Style TargetType=\"ToolTip\">(.*?)</Style>", text, re.S)
    check(match is not None, f"{package.name}: the product's implicit ToolTip style is gone")
    if match is None:
        continue
    tooltip = match.group(1)
    check('Property="MaxWidth" Value="{DynamicResource PopupMaxWidth}"' in tooltip,
          f"{package.name}: the tooltip has no width cap, so a long value becomes a strip that "
          "runs off the edge")
    check('Property="TextWrapping" Value="Wrap"' in tooltip,
          f"{package.name}: the tooltip does not wrap, so the full value is clipped a second time")
    check('Property="TextTrimming" Value="None"' in tooltip,
          f"{package.name}: the tooltip inherits the cell's trimming and would cut the very text "
          "it exists to reveal")
    check(text.count('<Style TargetType="ToolTip">') == 1,
          f"{package.name}: the package defines more than one implicit tooltip style; "
          "the loader rejects the duplicate and the product ends up with two looks")

if PROBLEMS:
    print(f"list pattern checks: {len(PROBLEMS)} failure(s).")
    for problem in PROBLEMS:
        print("  FAIL: " + problem)
    sys.exit(1)

print(f"PASS: the shared list pattern is defined in all {len(packages)} packages and every "
      f"{len(views)} module register follows it — bold ruled centred headers, a body with no grid "
      "lines, an equal width per column, and a tooltip on anything a cell had to trim.")
