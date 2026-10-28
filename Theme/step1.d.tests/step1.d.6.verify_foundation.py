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
