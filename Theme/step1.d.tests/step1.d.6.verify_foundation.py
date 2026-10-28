"""Portable clean-foundation guards; never compilation or rendered-UI evidence."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
inventory = ROOT / 'step1.ThemeStudio/step1.c.docs/step1.c.4.Cleanup-Inventory.md'
removed = re.findall(r'^\| `([^`]+)` \| Remove \|', inventory.read_text(), re.M)
assert len(removed) == 47, 'Cleanup inventory must retain all 47 inspected legacy paths'
for relative in removed:
    assert not (ROOT / relative).exists(), f'Legacy file returned: {relative}'
for directory in ('SheetStore', 'Converters', 'Shared', 'ViewModels'):
    assert not (ROOT / directory).exists(), f'Legacy tree returned: {directory}'

project = ET.parse(ROOT / 'ProGlassAutomation.csproj').getroot()
assert project.find('.//TargetFramework').text == 'net8.0-windows7.0'
# The reviewed dependency set. LiveCharts renders the theme's chart previews;
# EF Core + SQLite is the inquiry register's own store, added when the register
# was built. The rule is not "one package forever" - it is that every package is
# named here, pinned to an exact version, and nothing arrives unreviewed.
REVIEWED_PACKAGES = {
    'LiveChartsCore.SkiaSharpView.WPF': '2.0.4',
    'Microsoft.EntityFrameworkCore.Sqlite': '8.0.10',
}
packages = [(p.attrib['Include'], p.attrib['Version']) for p in project.findall('.//PackageReference')]
assert len(packages) == len({name for name, _ in packages}), f'duplicate PackageReference: {packages}'
unreviewed = [(name, version) for name, version in packages
              if REVIEWED_PACKAGES.get(name) != version]
assert not unreviewed, f'unreviewed or unpinned package: {unreviewed} (reviewed: {REVIEWED_PACKAGES})'
missing = sorted(set(REVIEWED_PACKAGES) - {name for name, _ in packages})
assert not missing, f'reviewed package disappeared from the project: {missing}'
for kind in ('Compile', 'Page'):
    included = []
    for item in project.findall(f'.//{kind}'):
        # The project also carries <Compile Remove="..."/> for the step2
        # verification executable; only Include entries describe the build.
        include = item.attrib.get('Include')
        if not include:
            continue
        for glob_pattern in include.split(';'):
            paths = [p for p in ROOT.glob(glob_pattern) if p.is_file()]
            assert paths, f'Broken project glob: {glob_pattern}'
            included.extend(paths)
    assert len(included) == len(set(included)), f'Duplicate {kind} items'
    if kind == 'Compile':
        # The guard used to list the source roots by hand, so a new module
        # (Inquiry, step2) looked like missing code the day it was added. The
        # rule is the inverse and cannot go stale: every application source on
        # disk must be in the build. The verification projects are separate
        # executables and keep their sources out by design.
        expected = {p.resolve() for p in ROOT.rglob('*.cs')
                    if not {'bin', 'obj'} & set(p.parts)
                    and not any('tests' in part for part in p.parts)}
        missing = sorted(str(p.relative_to(ROOT)) for p in expected - {p.resolve() for p in included})
        assert not missing, f'Application code missing from project: {missing}'
    print(f'PASS: {len(included)} {kind} items; existing paths and no duplicates.')

# Every source and markup file, not a hand-kept list of folders: the old globs
# stopped at Theme and Theme Studio, so a legacy reference inside the Inquiry or
# Client modules was never scanned.
for path in [*ROOT.rglob('*.cs'), *ROOT.rglob('*.xaml')]:
    if any(part in ('bin', 'obj') for part in path.parts):
        continue
    # The verification projects name the legacy symbols in order to forbid them;
    # scanning them for those names finds the guard, not a violation.
    if any('tests' in part for part in path.parts):
        continue
    text = path.read_text()
    # `ProGlassAutomation.ViewModels` was the legacy root. This application has its
    # own modules under that root (ProGlassAutomation.ViewModels.Inquiry), which is
    # new code with a new home; the legacy root is the bare namespace, so require
    # the name to end there rather than matching every sub-namespace.
    assert not re.search(r'ProGlassAutomation\.(SheetStore|Converters)\b'
                         r'|ProGlassAutomation\.ViewModels(?!\.)'
                         r'|Dapper|ShellStyles|SheetStoreModule', text), \
        f'Legacy reference: {path}'
    # Microsoft.Data.Sqlite is deliberately absent from that list. It is the
    # ADO.NET provider under the approved EF Core Sqlite package, and the
    # single-chain schema runner has to open a raw connection to apply PRAGMAs
    # and read user_version. What must not return is the legacy persistence -
    # Dapper and its SheetStore layer - and the approved package set above is
    # what keeps the provider from being swapped for something unreviewed.
app = (ROOT / 'App.xaml.cs').read_text()
assert 'Themes.Initialize(Resources)' in app and 'MainWindow.Show()' in app
runtime = (ROOT / 'Theme/step1.d.tests/step1.d.1.RuntimeChecks.cs').read_text()
assert 'ThemeService.InstalledThemes' in runtime and 'CanCustomize' in runtime and 'ThemeService.Fluent' in runtime and 'ThemeService.LiquidGlass' in runtime and 'ThemeService.Glassmorphism' in runtime
assert 'Apply("Claymorphism"' not in runtime, 'Clay is installed; do not treat it as missing'
shell = (ROOT/'MainWindow.xaml').read_text()
assert '<studio:ThemeStudioView />' in shell and '<studio:BusinessReferenceView />' in shell
assert 'SelectedIndex="{Binding SelectedWorkspace, Mode=TwoWay}"' in shell
assert 'WindowState="Maximized"' in shell
screen = ROOT/'step1.ThemeStudio/step1.b.ThemeStudioScreen'
view = (screen/'step1.b.1.ThemeStudioView.xaml').read_text()
sample = (screen/'step1.b.18.BusinessReferenceView.xaml').read_text()
assert '<local:CustomizationPanel' in view and '<local:TokensPanel' in view
assert 'CustomizationPanel' not in sample and 'TokensPanel' not in sample, 'Business page must not contain engine settings'
assert 'MaxWidth=' not in view and 'MaxWidth=' not in sample, 'Use full viewport width'
assert 'bento:BentoGrid' in view and 'bento:BentoGrid' in sample, 'Studio and reference must use the Bento grid'
assert 'FillAvailable="False"' in view and 'FillAvailable="False"' in sample, 'Studio and Home mosaics keep BentoRowHeight so cards are not crushed to the viewport'
assert 'Compact="True"' in view, 'Studio preview must use the compact 2×3 mosaic, not a nested full page'
assert 'ScrollViewer' in view and 'ScrollViewer' in sample and 'BusinessModules' in sample, 'Studio and Home mosaics must scroll when taller than the viewport'
assert 'UniformGrid' not in sample, 'Equal KPI strips are not a bento mosaic'
assert 'BentoGrid.ColumnSpan' in sample and 'BentoGrid.RowSpan' in sample, 'Reference mosaic must declare spans'
for component in ('OverviewPanel', 'AnalyticsOverview', 'WorklistPanel', 'ControlsPanel'):
    assert component in sample, f'Missing business reference pattern: {component}'
grid = ROOT / 'Theme/ThemeBento/step1.e.11.BentoGrid.cs'
assert grid.is_file() and 'class BentoGrid : Panel' in grid.read_text() and 'PlacePacked' in grid.read_text()
settings = (screen/'step1.b.16.CustomizationPanel.xaml').read_text()
for name in ('ThemePicker', 'AccentPicker', 'DensityPicker', 'ShapePicker', 'TypePicker'):
    assert f'x:Name="{name}"' in settings
for prop in ('SelectedAccent', 'SelectedDensity', 'SelectedShape', 'SelectedTypography'):
    assert f'Binding {prop}' in settings and f'public ' in (screen/'step1.b.17.Customization.cs').read_text()
assert 'Binding IsDark' in settings and 'Binding ResetDesign' in settings
assert 'Binding StoreWarning' in settings
catalog = (ROOT / 'Theme/step1.a.ThemeEngine/step1.a.6.ThemeCatalog.cs').read_text()
assert catalog.count('new ThemeDefinition(') == 9 and catalog.count('new ThemeIdentity(') == 9
session = (ROOT / 'Theme/step1.g.Customization/step1.g.5.ThemeEditSession.cs').read_text()
assert 'class ThemeEditSession' in session and 'CustomizationStore.Save' in session
customization = (screen / 'step1.b.17.Customization.cs').read_text()
assert 'ThemeEditSession.Begin' in customization and 'PersistSaved' in customization
assert 'class ThemeNotificationException' in (ROOT / 'Theme/step1.a.ThemeEngine/step1.a.7.ThemeNotificationException.cs').read_text()
assert 'ThemeNotificationException' in (ROOT / 'Theme/step1.a.ThemeEngine/step1.a.2.ThemeService.cs').read_text()
assert 'ReduceTransparency' not in (ROOT / 'Theme/step1.a.ThemeEngine/step1.a.1.ThemeContract.cs').read_text(), 'Keep ReduceTransparency theme-owned; do not glass other themes'
assert (ROOT / 'Theme/ThemeGlassmorphism/ThemeGlassmorphism.Light.xaml').is_file() and (ROOT / 'Theme/ThemeGlassmorphism/ThemeGlassmorphism.Dark.xaml').is_file()
texts = (screen/'step1.b.26.TextsStudioView.xaml').read_text()
colors = (screen/'step1.b.27.ColorsStudioView.xaml').read_text()
assert 'x:Name="FontPicker"' in texts and 'Binding SelectedFontFamily' in texts
assert 'x:Name="ColorPlatePicker"' in colors and 'Binding SelectedColorPlate' in colors
assert 'Binding ColorPlateCards' in colors
shell_text = (ROOT/'MainWindow.xaml').read_text()
assert 'Texts Studio' in shell_text and 'Colors Studio' in shell_text and 'Icons Studio' in shell_text
assert 'ShowTexts' in shell_text and 'ShowColors' in shell_text and 'ShowIcons' in shell_text
assert '<studio:IconsStudioView />' in shell_text
icons = (screen/'step1.b.30.IconsStudioView.xaml').read_text()
assert 'x:Name=\"IconPicker\"' in icons and 'Binding SelectedIconStyle' in icons
icons_svc = (ROOT / 'Theme/step1.g.Customization/step1.g.8.IconService.cs').read_text()
assert 'static IconKind Resolve' in icons_svc
assert 'Appearance, Layout, Charts, Work, Settings, Author, Engineering, Components' in icons_svc
mark = (screen/'step1.b.31.ShellMark.cs').read_text()
assert 'TitleProperty' in mark and 'CategoryProperty' in mark
assert 'IconService.Resolve(Title, Category, Kind)' in mark
about = (screen/'step1.b.29.AboutView.xaml').read_text()
assert 'Title="{Binding Name}"' in about and 'Category="engine"' in about
assert 'Title="Author"' in about and 'Title="Engineering notes"' in about
assert 'Title="Texts studio"' in texts and 'Title="Colors studio"' in colors
assert 'Title="{Binding WorkspaceTitle}"' in shell_text
assert 'Title="Active work"' in (screen/'step1.b.14.WorklistPanel.xaml').read_text()
assert 'Title="Monthly performance"' in (screen/'step1.b.21.AnalyticsOverview.xaml').read_text()
assert 'Title="Design settings"' in settings
active = ROOT / '.github/workflows/windows-wpf.yml'
parked = ROOT / 'cols/ci/windows-wpf.yml'
workflow = active if active.is_file() else parked
assert workflow.is_file(), 'Windows workflow YAML missing from .github/workflows or cols/ci'
# Owner may place the file in .github/workflows. Never require that path to be absent.
for command in ('dotnet build ProGlassAutomation.csproj', '--contracts-only', 'step1.d.6.verify_foundation.py'):
    assert command in workflow.read_text(), f'Missing CI verification: {command}'
print('PASS: 47 inventoried legacy files absent; no legacy source/startup dependencies; separate customization/business pages, live settings and Windows workflow present.')
print('LIMITATION: this is a source audit, not Windows compilation, runtime or visual inspection.')

# ── A verification executable must be able to SEE what it calls ─────────────
# Each verification project is its own assembly. When it calls a helper the
# application marks `internal`, it needs an InternalsVisibleTo line; without one
# the project does not compile, the test never runs, and the only symptom is a
# build error far from the cause. That is exactly how step4.f.CompanyTests
# stopped building in round 34.1: SqliteResilience.ApplyConnectionPolicy is
# internal for good reason (it is the app's one pragma policy), step1/2/3 were
# named as friends, and step4 was added later without its line.
#
# The internal members are read from the source rather than listed here, so the
# rule cannot go stale: mark a new helper internal, call it from a test, and the
# friend line becomes mandatory.
NOT_A_MEMBER = {'static', 'const', 'readonly', 'void', 'async', 'partial', 'sealed',
                'class', 'interface', 'enum', 'struct', 'record'}
internal_members = set()
for source_file in sorted(ROOT.rglob('*.cs')):
    if {'bin', 'obj'} & set(source_file.parts) or any('tests' in part for part in source_file.parts):
        continue
    owner = ''
    for line in source_file.read_text(encoding='utf-8', errors='replace').splitlines():
        declared = re.search(r'\b(?:class|struct|record)\s+(\w+)', line)
        if declared:
            owner = declared.group(1)
        stripped = line.strip()
        if stripped.startswith('internal ') and owner:
            member = re.split(r'[\(=;{]', stripped)[0].split()[-1]
            if re.fullmatch(r'\w+', member) and member not in NOT_A_MEMBER and member != owner:
                internal_members.add(f'{owner}.{member}')
# A guard against the scan silently finding nothing (a broken regex would pass
# every project by accident). It counts, so renaming a helper cannot trip it.
assert len(internal_members) >= 5, f'the internal-member scan found only {len(internal_members)} members; the rule is blind'

friends = set(re.findall(r'InternalsVisibleTo\("([^"]+)"\)', (ROOT / 'AssemblyInfo.cs').read_text()))
checked = 0
for csproj in sorted(ROOT.rglob('*.csproj')):
    if not any('tests' in part for part in csproj.parts):
        continue
    tree = ET.parse(csproj).getroot()
    references_app = any('ProGlassAutomation.csproj' in (item.attrib.get('Include') or '')
                         for item in tree.findall('.//ProjectReference'))
    if not references_app:
        continue
    declared_name = tree.find('.//AssemblyName')
    assembly = declared_name.text if declared_name is not None else csproj.stem
    sources = '\n'.join(p.read_text(encoding='utf-8', errors='replace') for p in csproj.parent.rglob('*.cs'))
    used = sorted(member for member in internal_members if member in sources)
    if used:
        assert assembly in friends, (
            f'{assembly} calls internal application members {used} but AssemblyInfo.cs does not '
            f'name it as a friend; that project will not compile (CS0117/CS0122)'
        )
        checked += 1
assert checked >= 3, f'expected the verification executables that call internal helpers, found {checked}'
print(f'PASS: {checked} verification executable(s) are named as friends for the internal helper(s) they call.')

# ── A test must not assert the platform's newline ───────────────────────────
# Windows joins multi-line values with CRLF, Linux with LF. A verification check
# that compares a stored value against a literal containing "\n" is therefore
# asserting the machine it happens to run on, not the rule — and it fails on
# Windows against correct code. That happened twice in step4.f.1 (round 34.3 and
# 34.4), both times reported as a product failure when the product was right.
#
# The rule: an equality assertion may not take a newline-containing literal as
# its expected value. Compare the LINES instead (step4.f.1 has JoinLines, which
# splits and rejoins with a fixed separator).
equality = re.compile(r'\b(Equal|EqualLine|AreEqual|Assert\.Equal)\s*\(\s*"((?:[^"\\]|\\.)*)"')
offenders = []
for test_file in sorted(ROOT.rglob('*.cs')):
    if {'bin', 'obj'} & set(test_file.parts) or not any('tests' in part for part in test_file.parts):
        continue
    text = test_file.read_text(encoding='utf-8', errors='replace')
    for match in equality.finditer(text):
        literal = match.group(2)
        if '\\n' in literal or '\\r' in literal:
            line = text[:match.start()].count('\n') + 1
            offenders.append(f'{test_file.relative_to(ROOT)}:{line} ({match.group(1)}("{literal[:40]}"))')
assert not offenders, (
    'a check asserts a line separator instead of the lines, so it passes or fails by '
    f'platform: {offenders} — compare the lines (a JoinLines-style helper)'
)
print('PASS: no verification check compares a raw newline-containing literal.')

# ── A verification check may not reference a member the module does not have ─
# This is the failure that cost rounds 34.1 and 34.2: the project could not
# compile, so none of its checks had ever run, and every "passing" claim about it
# was empty. `CS0117` (no such member) and `CS0246` (no such type) are not runtime
# faults to be caught on the owner's machine — they are visible in the text.
#
# Scope is deliberately narrow to stay honest: only `Type.Member` references whose
# Type is a type THIS module declares are resolved, and only against the members
# declared in that module's own sources. Calls into the framework, into another
# module, or through a local variable are not touched, so a hit is a real stale
# name rather than a guess.
def _members_by_type(source_text):
    """Declared members per type. Crude, but exact for this codebase's style."""
    stripped = re.sub(r'/\*.*?\*/', '', source_text, flags=re.S)
    stripped = re.sub(r'//[^\n]*', '', stripped)
    found = {}
    for match in re.finditer(r'\b(?:class|record|struct|interface|enum)\s+([A-Za-z_]\w*)', stripped):
        name = match.group(1)
        # A positional record's parameters ARE its properties.
        if re.match(r'\s*record\b', stripped[match.start():]):
            head = stripped[match.end():match.end() + 400]
            opened = re.match(r'\s*(?:<[^>]*>)?\s*\(', head)
            closed = head.find(')')
            if opened and closed > 0:
                for parameter in head[opened.end():closed].split(','):
                    parameter = parameter.strip().split('=')[0].strip()
                    if parameter:
                        candidate = parameter.split()[-1]
                        if re.fullmatch(r'[A-Za-z_]\w*', candidate):
                            found.setdefault(name, set()).add(candidate)
        body_start = stripped.find('{', match.end())
        if body_start < 0:
            continue
        depth, index = 0, body_start
        while index < len(stripped):
            if stripped[index] == '{':
                depth += 1
            elif stripped[index] == '}':
                depth -= 1
                if depth == 0:
                    break
            index += 1
        body = stripped[body_start:index].lstrip('{')
        found.setdefault(name, set()).update(re.findall(
            r'\b(?:public|private|internal|protected)\s+'
            r'(?:static\s+|readonly\s+|const\s+|override\s+|virtual\s+|async\s+|sealed\s+)*'
            r'[A-Za-z_][\w<>,\[\]\.\? ]*?\s+([A-Za-z_]\w*)\s*[\(\{;=]', body))
        if re.search(r'\benum\s+' + re.escape(name) + r'\b', stripped):
            for entry in body.split(','):
                entry = entry.split('=')[0].strip()
                if re.fullmatch(r'[A-Za-z_]\w*', entry):
                    found[name].add(entry)
    return found


MODULES = [('Theme', 'Theme/step1.d.tests'),
           ('step2.ClientManagement', 'step2.ClientManagement/step2.f.tests'),
           ('step3.UserManagement', 'step3.UserManagement/step3.f.tests'),
           ('step4.CompanyProfile', 'step4.CompanyProfile/step4.f.tests'),
           ('Diagnostics', 'Diagnostics/tests')]
stale = []
audited_executables = 0
for module_name, test_directory in MODULES:
    module_root, test_root = ROOT / module_name, ROOT / test_directory
    if not test_root.is_dir():
        stale.append(f'{test_directory} is missing: its checks cannot run at all')
        continue
    module_text = '\n'.join(path.read_text(encoding='utf-8', errors='replace')
                            for path in sorted(module_root.rglob('*.cs'))
                            if test_root not in path.parents and path.parent != test_root
                            and not {'bin', 'obj'} & set(path.parts))
    declared = _members_by_type(module_text)
    for executable in sorted(test_root.glob('*.cs')):
        audited_executables += 1
        text = executable.read_text(encoding='utf-8', errors='replace')
        for reference in re.finditer(r'\b([A-Z][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)', text):
            owner, member = reference.group(1), reference.group(2)
            if owner in declared and member not in declared[owner]:
                line = text[:reference.start()].count('\n') + 1
                stale.append(f'{executable.relative_to(ROOT)}:{line} uses {owner}.{member}, '
                             f'which {module_name} does not declare')
assert audited_executables >= 5, f'expected at least 5 verification executables, audited {audited_executables}'
assert not stale, (
    f'a verification check names something the module does not declare, so the project cannot '
    f'compile and NONE of its checks can run: {stale}'
)
print(f'PASS: {audited_executables} verification executable(s) only name members their module declares.')

# ── A check may not pass a number to a string-only helper (CS1503) ───────────
# Round 34.7: step3.f.1 compared ids with `Equal(admin.Id, stamp.UserId, ...)`
# against a helper declared `Equal(string, string, string)`. The project did not
# compile — six CS1503 errors — so none of its checks could run, and the failure
# surfaced only on the owner's machine. A missing member was already gated above;
# this covers the other half of "it cannot build": the right name, wrong type.
int_helper = re.compile(r'\b(?:private|internal|public)\s+static\s+void\s+Equal\s*\(\s*int\b')
# The negative lookahead matters: `Equal(admin.Id.ToString(), ...)` is a STRING
# argument and compiles, so only a bare id (not a member access on it) is an int.
id_argument = re.compile(r'\bEqual\(\s*[A-Za-z_]\w*[!]?\s*\.\s*(?:Id|UserId)\b(?!\s*\.)')
type_offenders = []
for test_file in sorted(ROOT.rglob('*.cs')):
    if {'bin', 'obj'} & set(test_file.parts) or not any('tests' in part for part in test_file.parts):
        continue
    text = test_file.read_text(encoding='utf-8', errors='replace')
    if int_helper.search(text):
        continue
    for match in id_argument.finditer(text):
        line = text[:match.start()].count('\n') + 1
        type_offenders.append(f'{test_file.relative_to(ROOT)}:{line}')
assert not type_offenders, (
    'a check passes an id (an int) to an Equal helper that only takes strings, so the '
    f'project cannot compile and none of its checks run: {type_offenders} — declare an '
    'int overload or compare .ToString()'
)
print('PASS: no verification check passes an int id to a string-only Equal helper.')

# ── A migration check may not pin the chain's tip ────────────────────────────
# The chain is shared and keeps growing: step2's check asserted user_version "1012"
# and step3's asserted "1013"; 1014-1016 joined the same chain and BOTH began
# failing against correct behaviour (round 34.8). The registry is the authority, so
# any executable that reads PRAGMA user_version must derive the expected value from
# AppSchemaMigrations.Inquiries instead of comparing a four-digit literal.
tip_pinners = []
for test_file in sorted(ROOT.rglob('*.cs')):
    if {'bin', 'obj'} & set(test_file.parts) or not any('tests' in part for part in test_file.parts):
        continue
    text = test_file.read_text(encoding='utf-8', errors='replace')
    if 'PRAGMA user_version' not in text:
        continue
    for match in re.finditer(r'\bEqual\(\s*"(\d{4})"', text):
        line = text[:match.start()].count('\n') + 1
        tip_pinners.append(f'{test_file.relative_to(ROOT)}:{line} compares user_version with "{match.group(1)}"')
assert not tip_pinners, (
    'a check pins the migration chain tip with a literal, so it fails as soon as a '
    f'migration is added: {tip_pinners} — derive it from AppSchemaMigrations.Inquiries'
)
print('PASS: no verification check pins the migration chain tip with a literal.')

# ── A check may not assert success while its message says it is impossible ───
# Round 34.9: `True(deactivation.Succeeded, "the sole administrator cannot be
# deactivated")` asserted the OPPOSITE of its own message. It failed against
# correct code, and — worse — it would have PASSED if the last-administrator guard
# had been broken, which is the failure it was written to catch. A check that
# contradicts its own description is not a weak check, it is a reversed one.
inverted = []
for test_file in sorted(ROOT.rglob('*.cs')):
    if {'bin', 'obj'} & set(test_file.parts) or not any('tests' in part for part in test_file.parts):
        continue
    text = test_file.read_text(encoding='utf-8', errors='replace')
    for match in re.finditer(r'True\(\s*([^;]{0,200}?)\.Succeeded\s*,\s*"([^"]{0,200})"', text):
        expression, message = match.group(1), match.group(2)
        if expression.lstrip().startswith('!'):
            continue
        # Only "cannot": a message mentioning a PREVIOUS refusal ("the refused second
        # attempt changed nothing") is legitimate, so broader keywords produce noise.
        if re.search(r'\bcannot\b', message, re.I):
            line = text[:match.start()].count('\n') + 1
            inverted.append(f'{test_file.relative_to(ROOT)}:{line} asserts success but says "{message[:60]}"')
assert not inverted, (
    'a check asserts that an operation SUCCEEDED while its message says it cannot, so it '
    f'fails on correct code and passes on broken code: {inverted}'
)
print('PASS: no verification check asserts success against a message that forbids it.')

# ── A check that touches the file system must import System.IO ───────────────
# Round 35.3: step1.d.16 used Path/File/Directory 66 times with no `using
# System.IO;`, and the Theme test project had never been built, so all 66 surfaced
# as CS0103 on the owner's machine at once. They were invisible to every other gate
# because a missing using is not a missing member — the names simply do not resolve.
#
# This is NOT covered by ImplicitUsings: a UseWPF project gets the WPF implicit set,
# which does not include System.IO. So the import must be explicit, and this rule
# keeps it that way for every verification executable.
io_api = re.compile(r'\b(Path|File|Directory|FileStream|StreamReader|StreamWriter|FileInfo|DirectoryInfo)\s*\.')
io_offenders = []
for test_file in sorted(ROOT.rglob('*.cs')):
    if {'bin', 'obj'} & set(test_file.parts) or not any('tests' in part for part in test_file.parts):
        continue
    text = test_file.read_text(encoding='utf-8', errors='replace')
    references = [m for m in io_api.finditer(text)
                  if not text[max(0, m.start() - 9):m.start()].endswith('System.IO.')]
    if references and 'using System.IO;' not in text:
        lines = sorted({text[:m.start()].count('\n') + 1 for m in references})
        io_offenders.append(f'{test_file.relative_to(ROOT)} ({len(references)} reference(s), '
                            f'first at line {lines[0]})')
assert not io_offenders, (
    'a verification check uses System.IO types without importing them, and a UseWPF project '
    f'does not get System.IO implicitly, so the project cannot compile: {io_offenders}'
)
print('PASS: every check that touches the file system imports System.IO.')

# ── A placed payload is never counted as Failed ──────────────────────────────
# Round 35.5: step1.d.16 asserted that an acknowledgement failure produced
# `Failed == 1` AND, two lines later, that the same item was `ImportPending`. The
# service increments exactly one, on purpose: a folder holding READY/manifest is a
# complete download awaiting local import, and calling it "Failed" would tell the
# operator their documents are missing when they are on disk.
#
# The invariant is worth locking in the product source, because the alternative
# ordering (count first, then decide) reads just as plausible.
service_source = ROOT / 'Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxService.cs'
assert service_source.exists(), f'{service_source} is missing'
service_text = service_source.read_text(encoding='utf-8', errors='replace')
failed_index = service_text.find('outcome.Failed++')
assert failed_index > 0, 'outcome.Failed++ has disappeared: the outcome no longer counts failures'
decision_window = service_text[max(0, failed_index - 900):failed_index]
assert 'else if (payloadPlaced)' in decision_window, (
    'outcome.Failed++ must sit after an else-if on payloadPlaced: a submission whose documents '
    'are already on disk is awaiting import, not failed'
)
assert 'outcome.ImportPending++' in decision_window, (
    'the placed-payload branch must increment ImportPending before the failure branch'
)

# ── A check may not demand two mutually exclusive outcomes at once ───────────
# The same defect from the check side. The original looked like this:
#     if (failedAck.Failed != 1 || ...) throw ...;
#     if (item.State != InquiryInboxStates.ImportPending) throw ...;
# Two statements in one check, each impossible given the other, because the service
# increments exactly one of the two counters per item.
#
# So the rule is scoped PER CHECK rather than per `if`: a single check may not
# require a non-zero failure count and an import-pending outcome, because no single
# item can be both. Scoping wider (per file) would flag deliberately mixed fixtures;
# scoping narrower (per condition) let the real defect through — which is how this
# rule was found to be wrong the first time it was written.
contradictions = []
for test_file in sorted(ROOT.rglob('*.cs')):
    if {'bin', 'obj'} & set(test_file.parts) or not any('tests' in part for part in test_file.parts):
        continue
    text = test_file.read_text(encoding='utf-8', errors='replace')
    headers = [m.start() for m in
               re.finditer(r'(?m)^    (?:private|public|internal) static [^\n]*?\b\w+\(', text)]
    bounds = headers + [len(text)]
    for index in range(len(headers)):
        body_start, body_end = bounds[index], bounds[index + 1]
        body = text[body_start:body_end]
        demands_failure = re.search(r'\.Failed\s*(?:!=|==)\s*[1-9]', body)
        mentions_pending = 'ImportPending' in body
        if demands_failure and mentions_pending:
            line = text[:body_start + demands_failure.start()].count('\n') + 1
            name = text[body_start:body_start + 80].split('(')[0].strip().split()[-1]
            contradictions.append(f'{test_file.relative_to(ROOT)}:{line} in {name}')
assert not contradictions, (
    'a check requires both a non-zero failure count and an import-pending outcome, but the '
    'service sets exactly one per item — so the check can never pass on correct code and would '
    f'pass on code that mislabels complete downloads: {contradictions}'
)
print('PASS: a placed payload is never counted as failed, and no check demands both outcomes.')

# ── A corrected source rule does not correct the runtime harness ─────────────
# Round 36: step1.d.1 asserted `model.SelectedWorkspace == 1` ("business reference
# is the startup page"), but no source sets 1 — step1.b.17 initialises it to 2, the
# inquiry register. The rule was already recorded in docs/05 and enforced here for
# step1.d.3 ("the shell must not boot into Theme Studio", i.e. not workspace 0), but
# the compiled check kept a literal because the harness had never been run. A check
# that pins a page number fails the moment the startup page moves, and says nothing
# about the property that actually matters.
runtime_source = ROOT / 'Theme/step1.d.tests/step1.d.1.RuntimeChecks.cs'
assert runtime_source.exists(), f'{runtime_source} is missing'
runtime_text = runtime_source.read_text(encoding='utf-8', errors='replace')
pinned_pages = [m.group(0) for m in
                re.finditer(r'SelectedWorkspace\s*==\s*\d+', runtime_text)]
assert not pinned_pages, (
    'the runtime harness pins a workspace page number, which is not a property of a '
    f'correct shell — assert the documented rule instead: {pinned_pages}'
)
assert re.search(r'SelectedWorkspace\s*!=\s*0\b', runtime_text), (
    'the harness must assert the documented rule that the shell does not boot into '
    'Theme Studio (workspace 0)'
)
assert runtime_text.count('"business reference is the startup page') >= 1, (
    'the startup-page check must keep its name'
)
# The chrome-title check reads the workspace it names, so that workspace has to be
# selected first. It used to pass only because the startup page happened to be 1;
# at the documented startup page it reads "Inquiries"/"Business · Inquiry register".
home_titles = runtime_text.find('"home chrome titles"')
assert home_titles > 0, 'the home chrome title check has disappeared'
sets_page_one = runtime_text.rfind('SelectedWorkspace = 1;', 0, home_titles)
assert sets_page_one > 0, (
    'the home chrome title check must select page 1 explicitly instead of relying on '
    'the startup page'
)
# The owner reports a slow Light/Dark switch, so the harness reports the cost of the
# switch it runs. A printed measurement keeps the next report a number, not a memory.
assert 'MEASURED: package rebuild' in runtime_text, (
    'the runtime harness must print how long the package rebuild and the bound '
    'appearance switch took'
)
print('PASS: the runtime harness asserts the documented startup rule, sets the page it reads, and measures the switch.')

# ── A preset list that nothing is bound to cannot select anything ────────────
# Round 36, owner report: "Night Berry does not select in dark mode". Night Berry is
# an ordinary Clay built-in preset (step1.e.8, Appearance.Dark). The lists that show
# presets had no selection binding at all and ApplyPreset was bound in no XAML file,
# so a click on a row could only highlight it. The Bento family had no picker
# property at all. The rule now: every list of presets selects through its family's
# picker property, and every picker property applies the preset it names.
panel_path = ROOT / 'step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.16.CustomizationPanel.xaml'
customization_path = ROOT / 'step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs'
panel_text = panel_path.read_text(encoding='utf-8')
customization_text = customization_path.read_text(encoding='utf-8')
# Round 36 split the preset half of the view model into its own partial file, because
# step1.b.17 is one of the grandfathered long files and may be split, never grown.
preset_part = ROOT / 'step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.18.Presets.cs'
assert preset_part.exists(), f'{preset_part} is missing'
preset_text = preset_part.read_text(encoding='utf-8')
picker_text = customization_text + preset_text
list_boxes = re.findall(r'<ListBox\b[^>]*>', panel_text)
assert list_boxes, 'the customization panel shows no preset lists at all'
preset_lists = [tag for tag in list_boxes if re.search(r'ItemsSource="\{Binding \w*Presets\}"', tag)]
assert len(preset_lists) == 8, (
    'expected the eight preset lists (Bento, Clay, Neumorph, Liquid × built-in and '
    f'saved): {len(preset_lists)}'
)
unbound_lists = [tag for tag in preset_lists
                 if 'SelectedValuePath="Name"' not in tag
                 or not re.search(r'SelectedValue="\{Binding Selected\w*PresetName\}"', tag)]
assert not unbound_lists, (
    'a preset list has no selection binding, so picking a preset only highlights a '
    f'row: {unbound_lists}'
)
picked = sorted({m.group(1) for tag in preset_lists
                 for m in [re.search(r'SelectedValue="\{Binding (Selected\w*PresetName)\}"', tag)] if m})
for name in picked:
    definition = re.search(r'public string ' + name + r'\b', picker_text)
    assert definition, f'{name} is bound by a preset list but is not a view-model property'
    setter_start = picker_text.find('set', definition.end())
    assert setter_start > 0, f'{name} has no setter, so a pick cannot reach the command'
    setter = picker_text[setter_start:setter_start + 700]
    assert 'ApplyPreset.Execute(value)' in setter, (
        f'{name} does not apply the picked preset: a pick would change the selection '
        'and nothing else'
    )
    assert 'PresetPickNeedsApply' in setter, (
        f'{name} must decide with PresetPickNeedsApply: an equality guard alone makes '
        'the second pick of the same preset do nothing after an appearance switch'
    )
for family, presets in (('Clay', 'ClayCustomization.BuiltIn'),
                        ('Neumorph', 'NeumorphCustomization.BuiltIn'),
                        ('Liquid', 'LiquidCustomization.BuiltIn')):
    assert presets in picker_text, f'{family} preset catalogue is missing'
assert 'PresetCatalog.BuiltIn' in picker_text, 'the Bento preset catalogue is missing'
assert 'Night Berry' in (ROOT / 'Theme/ThemeClaymorphism/step1.e.8.ClayCustomization.cs').read_text(encoding='utf-8'), (
    'the Night Berry preset has disappeared from the Clay catalogue'
)
print(f'PASS: all {len(preset_lists)} preset lists select through a picker that applies what it names.')

# ── A preset that also changes mode must not rebuild the package twice ───────
# Each package replacement re-resolves every DynamicResource reference in the live
# tree, so a pick that switched appearance and then applied the state rebuilt the
# whole package twice in a row. The state goes in place first and the single
# appearance switch composes it.
apply_preset = preset_text.find('ApplyPreset = new DelegateCommand<string>')
assert apply_preset > 0, 'the preset command has disappeared'
apply_body = preset_text[apply_preset:preset_text.find('SavePreset = new DelegateCommand', apply_preset)]
assert apply_body, 'the preset command has no body'
switch_then_state = re.search(
    r'SelectAppearance\(ThemeService\.\w+,\s*[\w.]+\);\s*\n\s*Apply\w*State\(', apply_body)
assert not switch_then_state, (
    'a preset pick switches appearance and then applies the state, which rebuilds the '
    'whole theme package twice for one pick'
)
for switch in re.finditer(r'SelectAppearance\(ThemeService\.(\w+),', apply_body):
    window = apply_body[max(0, switch.start() - 400):switch.start()]
    field = {'Clay': 'clay', 'Neumorphism': 'neumorph', 'LiquidGlass': 'liquid', 'Bento': 'extended'}[switch.group(1)]
    assert re.search(r'\b' + field + r' = \w+\.State;', window), (
        f'the {switch.group(1)} preset switches appearance without putting the state in '
        'place first, so the switch composes the old state'
    )
print('PASS: a preset pick rebuilds the theme package once, with its state already in place.')

# ── One package change is refreshed once ─────────────────────────────────────
# ThemeService.Apply raises ActiveAppearance and then ActiveTheme for the same
# package. The studio handled both identically, so every switch rebuilt the 164-row
# token list and republished ~45 properties twice.
service_text = (ROOT / 'Theme/step1.a.ThemeEngine/step1.a.2.ThemeService.cs').read_text(encoding='utf-8')
raised = service_text.find('new[] { nameof(ActiveAppearance), nameof(ActiveTheme) }')
assert raised > 0, (
    'the appearance/theme notification pair has changed shape: this rule and the '
    'studio handler both depend on ActiveAppearance being raised first'
)
assert 'nameof(ActiveAppearance), nameof(ActiveTheme)' in service_text, (
    'ActiveAppearance must be raised before ActiveTheme so the package refresh happens once'
)
vm_text = (ROOT / 'step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.3.ThemeStudioViewModel.cs').read_text(encoding='utf-8')
handler_start = vm_text.find('private void ThemeChanged(')
assert handler_start > 0, 'the theme-change handler has disappeared'
handler_end = vm_text.find('private void RefreshTokens()', handler_start)
assert handler_end > handler_start
handler_body = vm_text[handler_start:handler_end]
assert handler_body.count('RefreshTokens();') == 1, (
    'the handler refreshes the token list more than once per package change: '
    f'{handler_body.count("RefreshTokens();")} calls'
)
appearance_branch = handler_body.find('nameof(ThemeService.ActiveAppearance)')
theme_branch = handler_body.find('nameof(ThemeService.ActiveTheme)')
assert 0 < appearance_branch < theme_branch, (
    'the handler must refresh on the ActiveAppearance notification and only republish '
    'theme identity on ActiveTheme'
)
assert 'ThemeService.ActiveTheme)) Changed(property)' not in handler_body.split('if (e.PropertyName !=')[1][:handler_body.split('if (e.PropertyName !=')[1].find('}') + 1], (
    'the ActiveTheme branch must not repeat the package refresh'
)
print('PASS: one package change refreshes the studio once.')

# ── Reading a theme's own values must not rebuild the package ────────────────
# Every `themes.Apply` already builds one candidate package. The shared colour
# overlay then called ThemeService.Load — a second whole-package clone plus a full
# contract validation — twice per activation, only to read three brush colours that
# the theme itself declares, and a third time when the plate is "Theme". Reading the
# cached base is the same values without the rebuild. It must stay read-only: the
# base is what every activation is cloned from.
plate_service = (ROOT / 'Theme/step1.g.Customization/step1.g.6.ColorPlateService.cs').read_text(encoding='utf-8')
loads = re.findall(r'ThemeService\.Load\([^)]*\)', plate_service)
assert not loads, (
    'the colour overlay rebuilds a whole theme package to read a colour, which it does '
    f'on every activation: {loads}'
)
assert plate_service.count('ThemeService.ReadOnlyBase(theme, mode)') == 2, (
    'both base reads in the colour overlay must use the cached base package'
)
assert 'internal static ResourceDictionary ReadOnlyBase(' in service_text, (
    'the cached base accessor has disappeared from the theme engine'
)
base_body = service_text[service_text.find('internal static ResourceDictionary ReadOnlyBase('):]
base_body = base_body[:base_body.find('\n\n')]
assert 'BasePackage(' in base_body and 'ClonePack' not in base_body, (
    'the read-only accessor must return the cached base, never a clone it invites a '
    'caller to mutate'
)
print('PASS: reading a theme\'s own values costs a dictionary read, not a package rebuild.')

# ── A split part inherits nothing: usings are per file, not per class ────────
# Round 36's first compile, on the owner's machine: eight CS0103 "The name
# 'ThemeService' does not exist in the current context" in the new
# step1.b.18.Presets.cs. Moving members into a partial part moves the code but not
# the imports, so the file compiled alone resolved nothing. Round 35.3 had the same
# shape (66 CS0103 for a missing `using System.IO;`) and produced a rule for one
# namespace; this one is general: every unqualified type use must be reachable from
# the file's own namespace or its own usings.
#
# The check derives the type→namespace map from the repository itself, so it does not
# need a C# compiler and cannot go stale the way a hand-kept list would. It ignores
# qualified uses (`Ns.Type.Member`, which need no using) and comments, and it skips
# test projects, whose assemblies add their own imports and friend access.
type_decl = re.compile(
    r'^[ \t]*(?:(?:public|internal|protected|private|file)\s+)?'
    r'(?:(?:sealed|static|abstract|partial|readonly|ref|unsafe)\s+)*'
    r'(class|record|enum|struct|interface)\s+(\w+)', re.M)
namespace_decl = re.compile(r'^[ \t]*namespace\s+([\w.]+)', re.M)
using_decl = re.compile(r'^[ \t]*using\s+([\w.]+)\s*;', re.M)
using_alias = re.compile(r'^[ \t]*using\s+\w+\s*=\s*([\w.]+)\s*;', re.M)
using_static = re.compile(r'^[ \t]*using\s+static\s+([\w.]+)\s*;', re.M)
code_comment = re.compile(r'//[^\n]*|/\*.*?\*/', re.S)
code_string = re.compile(r'"(?:[^"\\\n]|\\.)*"|\'(?:[^\'\\\n]|\\.)*\'')
type_use = re.compile(r'(?<![.\w])([A-Za-z_]\w*)\s*(?=[.(])')
# A type is also named where it declares something: a parameter, field or local
# (`Appearance mode`, `ClayCustomizationState next`). That shape cannot be confused
# with a member assignment, which is why it is safe to match.
type_declaration_use = re.compile(r'(?<![.\w])([A-Za-z_]\w*)\s+[A-Za-z_]\w*\s*[,)=;]')

def without_prose(text):
    return code_string.sub('""', code_comment.sub('', text))

def reachable_namespaces(ns):
    parts = ns.split('.')
    return {'.'.join(parts[:index]) for index in range(1, len(parts) + 1)} | {''}

declared_in = {}
compiled = []
for source in sorted(ROOT.rglob('*.cs')):
    if {'bin', 'obj', '.git'} & set(source.relative_to(ROOT).parts):
        continue
    raw = source.read_text(encoding='utf-8', errors='replace')
    code = without_prose(raw)
    found = namespace_decl.search(raw)
    file_namespace = found.group(1) if found else ''
    own_types = {match.group(2) for match in type_decl.finditer(code)}
    compiled.append((source, raw, code, file_namespace, own_types,
                     set(using_decl.findall(raw)) | set(using_alias.findall(raw))
                     | {'.'.join(using.split('.')[:-1]) for using in using_static.findall(raw)}))
    for name in own_types:
        declared_in.setdefault(name, set()).add(file_namespace)

unresolved = []
for source, raw, code, namespace, own_types, imports in compiled:
    relative = source.relative_to(ROOT)
    if any(part.endswith('.tests') for part in relative.parts):
        continue
    reachable = imports | reachable_namespaces(namespace)
    for name in set(type_use.findall(code)) | set(type_declaration_use.findall(code)):
        namespaces = declared_in.get(name)
        if namespaces is None or name in own_types or namespaces & reachable:
            continue
        hit = (next(candidate for candidate in type_use.finditer(code) if candidate.group(1) == name)
               if any(candidate.group(1) == name for candidate in type_use.finditer(code))
               else next(candidate for candidate in type_declaration_use.finditer(code) if candidate.group(1) == name))
        unresolved.append(f'{relative}:{code[:hit.start()].count(chr(10)) + 1} uses {name} '
                          f'(declared in {", ".join(sorted(namespaces))})')
assert not unresolved, (
    'a file uses a type it cannot reach: a partial part inherits the other part\'s code '
    'but not its imports, and this is what a first compile reports as CS0103 — the project '
    f'does not build: {unresolved}'
)
print(f'PASS: all {len(compiled)} C# files can reach every type they name.')

# ── A name the harness reads must be a name some page declares ───────────────
# Round 36's second harness run: the layout check asked the Studio for "Signals"
# and "DiagnosticsToggle". No XAML file in this repository ever declared either, and
# `FindName` returns null for an unknown name, so the failure surfaced as an opaque
# WPF exception from a later line instead of "this page has no such control".
# Names come from the page; the harness may only look up what the page declares.
declared_names = set()
for markup in sorted(ROOT.rglob('*.xaml')):
    if {'bin', 'obj', '.git'} & set(markup.relative_to(ROOT).parts):
        continue
    declared_names |= set(re.findall(r'x:Name="(\w+)"', markup.read_text(encoding='utf-8', errors='replace')))
name_reads = []
unanalysable = []
lookups = 0
for check_file in sorted(ROOT.rglob('*.cs')):
    parts = set(check_file.relative_to(ROOT).parts)
    if {'bin', 'obj', '.git'} & parts or not any(part.endswith('.tests') for part in parts):
        continue
    text = check_file.read_text(encoding='utf-8', errors='replace')
    # Names are also read from a list: `foreach (string module in new[] { "A", "B" })`
    # then `FindName(module)`. My first version of this rule only saw string literals
    # and passed the very mutation it names, because the harness enumerated a list.
    enumerated = {}
    for loop in re.finditer(r'foreach\s*\(\s*string\s+(\w+)\s+in\s+new\[\]\s*\{(.*?)\}\s*\)', text, re.S):
        enumerated.setdefault(loop.group(1), set()).update(re.findall(r'"(\w+)"', loop.group(2)))
    # `Template.FindName` reads a control template's parts, which live in styles and
    # templates by design and are not x:Named page elements.
    for match in re.finditer(r'(?<!Template)\.FindName\(\s*([^)]*?)\s*\)', text):
        lookups += 1
        argument = match.group(1).strip()
        line = text[:match.start()].count('\n') + 1
        if argument.startswith('"') and argument.endswith('"'):
            names = {argument.strip('"')}
        elif argument in enumerated:
            names = enumerated[argument]
        else:
            unanalysable.append(f'{check_file.relative_to(ROOT)}:{line} FindName({argument})')
            continue
        for name in sorted(names):
            if name not in declared_names:
                name_reads.append(f'{check_file.relative_to(ROOT)}:{line} reads "{name}"')
assert lookups, 'no element lookup was found: this rule would pass on an empty set'
assert not unanalysable, (
    'a check looks up an element through an expression this rule cannot resolve, so a '
    f'stale name could hide there: {unanalysable}'
)
assert not name_reads, (
    'a check looks up an element name that no XAML file declares, so the lookup returns '
    'null and the failure appears later as an unrelated exception: ' + ', '.join(name_reads)
)
print(f'PASS: every element name the checks read is declared by a page ({len(declared_names)} names).')

# ── Nothing may be measured through an ancestor relation nobody proved ───────
# `TransformToAncestor` throws "The specified Visual is not an ancestor of this
# Visual" when the target is a sibling, which is what happened when the Settings
# sidebar was measured against the modules grid it sits beside. The harness therefore
# proves the relation first; this rule keeps the proof next to the measurement, for
# the same pair of identifiers.
harness_files = [path for path in sorted(ROOT.rglob('*.cs'))
                 if any(part.endswith('.tests') for part in path.relative_to(ROOT).parts)
                 and not ({'bin', 'obj', '.git'} & set(path.relative_to(ROOT).parts))]
assert harness_files, 'no test source was found: this rule would pass on an empty set'
unproved = []
measurements = 0
for check_file in harness_files:
    text = check_file.read_text(encoding='utf-8', errors='replace')
    # Fail closed: a call whose receiver or target is not a plain identifier cannot be
    # checked here, so it is reported rather than skipped. A conditional ancestor
    # (`TransformToAncestor(isSidebar ? grid : page)`) is exactly how this defect would
    # come back, and the first version of this rule silently ignored it.
    for call in re.finditer(r'\.TransformToAncestor\(([^)]*)\)', text):
        if re.fullmatch(r'\s*\w+\s*', call.group(1)) and re.search(r'\w+$', text[:call.start()]):
            continue
        unproved.append(f'{check_file.relative_to(ROOT)}:{text[:call.start()].count(chr(10)) + 1} '
                        f'transforms through an expression: TransformToAncestor({call.group(1).strip()})')
    for match in re.finditer(r'(\w+)\.TransformToAncestor\((\w+)\)', text):
        receiver, target = match.group(1), match.group(2)
        proof = re.compile(r'IsInside\(\s*' + re.escape(receiver) + r'\s*,\s*' + re.escape(target) + r'\s*\)')
        if proof.search(text, 0, match.start()):
            continue
        line = text[:match.start()].count('\n') + 1
        unproved.append(f'{check_file.relative_to(ROOT)}:{line} transforms {receiver} through {target}')
    measurements += len(re.findall(r'\.TransformToAncestor\(', text))
assert measurements, 'no measured element was found: this rule would pass on an empty set'
assert not unproved, (
    'a check measures an element through an ancestor it never proved, which throws an '
    f'opaque exception instead of a finding: {unproved}'
)
assert 'private static bool IsInside(' in runtime_text, (
    'the visual-tree membership helper the harness proves ancestors with has disappeared'
)
print('PASS: every measured element is proved to be inside the ancestor it is measured against.')

# ── A check may not pin how many elements a page happens to contain ──────────
# The analytics check opened with `charts.Length == 4, "four real analytics charts"`.
# The reference page declares eight, and no version of that page ever had four — the
# same invented-page pattern as "Signals" and "DiagnosticsToggle" in the layout check.
# A count of live visual elements is not a property of a correct page: it changes when
# somebody adds a tile, and the failure says nothing about whether the application
# works. What the check needs is that the page carries charts and that each carries a
# real dataset; the specific charts it uses are then found by their data, not by index.
element_counts = []
for check_file in sorted(ROOT.rglob('*.cs')):
    parts = set(check_file.relative_to(ROOT).parts)
    if {'bin', 'obj', '.git'} & parts or not any(part.endswith('.tests') for part in parts):
        continue
    text = check_file.read_text(encoding='utf-8', errors='replace')
    # Where a collection of live elements is bound to a name, that name is followed.
    pinned = {match.group(1) for match in re.finditer(
        r'(\w+)\s*=\s*[^;\n]*\.OfType<\w+>\(\)[^;\n]*;', text)}
    for variable in pinned:
        for match in re.finditer(r'\b' + re.escape(variable) + r'\.(?:Length|Count)\(?\)?\s*==\s*\d+', text):
            line = text[:match.start()].count('\n') + 1
            element_counts.append(f'{check_file.relative_to(ROOT)}:{line} pins {match.group(0).strip()}')
    for match in re.finditer(r'\.OfType<\w+>\(\)[^\n]*?\.(?:Length|Count)\(?\)?\s*==\s*\d+', text):
        line = text[:match.start()].count('\n') + 1
        element_counts.append(f'{check_file.relative_to(ROOT)}:{line} pins {match.group(0).strip()}')
assert not element_counts, (
    'a check pins the number of live elements a page contains, which is not a property of '
    f'correct behaviour and fails when a tile is added: {element_counts}'
)
print('PASS: no check pins how many live elements a page happens to contain.')

# ── A responsive rule is the control's, not a number remembered from a screenshot ─
# The Studio responsive check compared `EffectiveColumns < 4` with `ActualWidth < 1100`.
# BentoGrid declares three columns on the Studio page, so "< 4" was true at every
# width — the assertion could not fail for the reason it named — and CompactWidth is a
# themed resource that need not be 1100. The grid's MeasureOverride is the only place
# that decides this, so the check must state the same rule from the grid's own values.
column_pins = []
for check_file in sorted(ROOT.rglob('*.cs')):
    parts = set(check_file.relative_to(ROOT).parts)
    if {'bin', 'obj', '.git'} & parts or not any(part.endswith('.tests') for part in parts):
        continue
    text = check_file.read_text(encoding='utf-8', errors='replace')
    for match in re.finditer(r'EffectiveColumns\s*[<>=!]+\s*\d+', text):
        line = text[:match.start()].count('\n') + 1
        column_pins.append(f'{check_file.relative_to(ROOT)}:{line} pins {match.group(0).strip()}')
assert not column_pins, (
    'a check compares the grid\'s effective column count with a literal, which is only '
    f'meaningful for one grid size and theme: {column_pins}'
)
grid_source = (ROOT / 'Theme/ThemeBento/step1.e.11.BentoGrid.cs').read_text(encoding='utf-8')
rule = re.search(r'usedColumns\s*=\s*width\s*>=\s*CompactWidth[^;]*;', grid_source)
assert rule, 'the grid\'s compact-width rule has changed shape and the check that mirrors it must be reread'
assert '>= 720' in rule.group(0) and 'EffectiveColumns = usedColumns' in grid_source, (
    'the compact-width rule and the published EffectiveColumns must stay the ones the check mirrors'
)
runtime_harness = (ROOT / 'Theme/step1.d.tests/step1.d.1.RuntimeChecks.cs').read_text(encoding='utf-8')
assert runtime_harness.count('ExpectedColumns(') >= 2, (
    'the responsive checks must compare against ExpectedColumns, which derives the rule '
    'from the grid itself'
)
print('PASS: responsive assertions state the grid\'s own compact rule, not a remembered number.')

# ── A theme's "defaults" are only defaults after a reset ─────────────────────
# The Studio restores this workstation's saved design on construction, so a check that
# asserts a theme's own values right after activating it is really asserting whatever
# was last saved here. On the owner's machine that is not a test, it is a memory of his
# last session. Every assertion of a default must therefore follow a reset.
default_markers = [
    'SelectedAccent == ProGlassAutomation.ThemeBento.BentoAccent.Ocean',
    'DesignSummary.Contains("Rose")',
    'SelectedClayAccent == ProGlassAutomation.ThemeClaymorphism.ClayAccent.Rose',
    'SelectedNeumorphDepth == ProGlassAutomation.ThemeNeumorphism.NeumorphDepth.Standard',
    'SelectedLiquidFrost == ProGlassAutomation.ThemeLiquidGlass.LiquidFrost.Standard',
]
unreset = []
for marker in default_markers:
    position = runtime_harness.find(marker)
    assert position > 0, f'the default assertion has disappeared and this rule describes nothing: {marker}'
    activation = runtime_harness.rfind('SelectedTheme = ', 0, position)
    assert activation > 0, f'no theme activation precedes {marker}'
    if 'ResetDesign.Execute' not in runtime_harness[activation:position]:
        line = runtime_harness[:position].count('\n') + 1
        unreset.append(f'step1.d.1.RuntimeChecks.cs:{line} asserts a default ({marker.split(" == ")[0]}) '
                       'without a reset after the theme was activated')
assert not unreset, (
    'a check asserts a theme default on a workstation whose saved design is restored at '
    f'startup, so it tests the saved file, not the engine: {unreset}'
)
print('PASS: every asserted theme default follows a reset of that theme.')
