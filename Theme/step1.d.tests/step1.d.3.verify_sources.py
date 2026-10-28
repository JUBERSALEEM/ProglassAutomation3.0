"""Portable source checks; these do NOT compile or execute WPF."""
from pathlib import Path
import math
import runpy
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
P = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
ENGINE = ROOT / 'Theme/step1.a.ThemeEngine'
contract = dict(re.findall(r'\[\"([^\"]+)\"\] = typeof\(([^)]+)\)',
                           (ENGINE / 'step1.a.1.ThemeContract.cs').read_text().split('RequiredStyles')[0]))
assert contract, 'Typed contract missing'

def local(tag):
    return tag.split('}')[-1]

def luminance(color):
    channels = [int(color[i:i+2], 16) / 255 for i in (1, 3, 5)]
    linear = [x / 12.92 if x <= .04045 else ((x + .055) / 1.055) ** 2.4 for x in channels]
    return sum(x * weight for x, weight in zip(linear, (.2126, .7152, .0722)))

contrast_checks = 0
pairs = [(fg, bg, 4.5) for fg in ('TextPrimary', 'TextSecondary', 'TextMuted')
         for bg in ('ApplicationBackground', 'SurfacePrimary', 'SurfaceSecondary', 'SurfaceElevated', 'StateHover', 'StatePressed', 'StateSelected', 'AccentSoft')]
pairs += [('TextOnAccent', bg, 4.5) for bg in ('AccentPrimary', 'AccentHover', 'AccentPressed')]
pairs += [(f'Status{s}', f'Status{s}Surface', 4.5) for s in ('Success', 'Warning', 'Error', 'Information')]
pairs += [('StatusWarning', 'SurfaceSecondary', 4.5)]
pairs += [(fg, bg, 4.5) for fg in ('TextPrimary', 'TextSecondary', 'TextMuted') for bg in ('SurfacePositive', 'SurfaceFeature', 'SurfaceInformation', 'SurfaceAttention')]
pairs += [('ChartTooltipText', 'ChartTooltipSurface', 4.5), ('ChartAxis', 'SurfacePrimary', 4.5)]
pairs += [(fg, 'SurfaceElevated', 4.5) for fg in ('AccentPrimary', 'StatusSuccess', 'StatusInformation')]
pairs += [('AccentPrimary', 'AccentSoft', 4.5), ('AccentPrimary', 'StateSelected', 4.5)]
pairs += [(fg, bg, 3) for fg in ('FocusIndicator', 'BorderStrong')
          for bg in ('SurfacePrimary', 'SurfaceSecondary')]
for package, stem, accents in (('ThemeBento', 'ThemeBento', True), ('ThemeClaymorphism', 'ThemeClaymorphism', False), ('ThemeFluent', 'ThemeFluent', False), ('ThemeNeumorphism', 'ThemeNeumorphism', False), ('ThemeLiquidGlass', 'ThemeLiquidGlass', False), ('ThemeGlassmorphism', 'ThemeGlassmorphism', False), ('ThemeSpatial', 'ThemeSpatial', False), ('ThemeSwiss', 'ThemeSwiss', False), ('ThemeHolographic', 'ThemeHolographic', False)):
    for mode in ('Light', 'Dark'):
        path = ROOT / f'Theme/{package}/{stem}.{mode}.xaml'
        tree = ET.parse(path).getroot()
        keys = [item.attrib[X+'Key'] for item in tree]
        assert len(keys) == len(set(keys)), f'{package}/{mode}: duplicate token keys'
        values = {item.attrib[X+'Key']: item for item in tree}
        assert set(values) == set(contract), f'{package}/{mode}: contract mismatch: {set(values) ^ set(contract)}'
        for key, type_name in contract.items():
            assert local(values[key].tag) in ({'SolidColorBrush', 'LinearGradientBrush', 'RadialGradientBrush'} if type_name == 'Brush' else {{'double': 'Double'}.get(type_name, type_name)}), (package, key, type_name)
            if type_name == 'double':
                number = float(values[key].text)
                assert math.isfinite(number) and number >= 0, key
                if key.endswith('Opacity'):
                    assert number <= 1, key
        cases = [('Default', {key: node.attrib['Color'] for key, node in values.items() if 'Color' in node.attrib})]
        if accents:
            source = (ROOT/'Theme/ThemeBento/step1.e.8.BentoCustomization.cs').read_text()
            for name, strings in re.findall(r'\(BentoAccent\.(\w+), Appearance\.' + mode + r'\) => \[(.*?)\]', source):
                overrides = re.findall(r'\"(#[0-9A-Fa-f]{6})\"', strings)
                assert len(overrides) == 5, name
                current = dict(cases[0][1])
                current.update(zip(('AccentPrimary', 'AccentHover', 'AccentPressed', 'AccentSoft', 'TextOnAccent'), overrides))
                current['FocusIndicator'] = current['AccentPrimary']
                current['StateSelected'] = current['AccentSoft']
                cases.append((name, current))
            assert len(cases) == 3, 'Every curated accent needs portable contrast coverage'
        for preset, current in cases:
            for fg, bg, minimum in pairs:
                a, b = (luminance(current[k]) for k in (fg, bg))
                ratio = (max(a, b) + .05) / (min(a, b) + .05)
                assert ratio >= minimum, f'{package}/{mode}/{preset} {fg}/{bg}: {ratio:.2f} < {minimum}'
                contrast_checks += 1
        for stop in values['HeroBackground'].iter(P+'GradientStop'):
            for fg in ('HeroForeground', 'HeroSecondaryText', 'HeroAccent'):
                a, b = luminance(values[fg].attrib['Color']), luminance(stop.attrib['Color'])
                ratio = (max(a, b)+.05)/(min(a, b)+.05)
                assert ratio >= 4.5, f'{package}/{mode} hero contrast: {fg}: {ratio:.2f}'
                contrast_checks += 1

clay_custom = (ROOT/'Theme/ThemeClaymorphism/step1.e.8.ClayCustomization.cs').read_text()
assert 'public static class ClayCustomization' in clay_custom and 'BentoCustomization' not in clay_custom
assert 'SetOwned' in clay_custom and 'ReplaceOwn' in clay_custom
assert 'public static IReadOnlyList<ClayNamedPreset> BuiltIn' in clay_custom
assert all(token in clay_custom for token in ('ClayType', 'ClaySpacing', 'ClayTextContrast', 'ClaySheen', 'ClayWeight', 'ClayDropPositive', 'ClayInsetLightBrush'))
for mode in ('Light', 'Dark'):
    path = ROOT / f'Theme/ThemeClaymorphism/ThemeClaymorphism.{mode}.xaml'
    values = {item.attrib[X+'Key']: item for item in ET.parse(path).getroot() if X+'Key' in item.attrib}
    current = {key: node.attrib['Color'] for key, node in values.items() if 'Color' in node.attrib}
    found = 0
    for name, strings in re.findall(r'\(ClayAccent\.(\w+), Appearance\.' + mode + r'\) => \[(.*?)\]', clay_custom):
        overrides = re.findall(r'\"(#[0-9A-Fa-f]{6})\"', strings)
        assert len(overrides) == 5, name
        sample = dict(current)
        sample.update(zip(('AccentPrimary', 'AccentHover', 'AccentPressed', 'AccentSoft', 'TextOnAccent'), overrides))
        sample['FocusIndicator'] = sample['AccentPrimary']
        sample['StateSelected'] = sample['AccentSoft']
        for fg, bg, minimum in pairs:
            a, b = (luminance(sample[k]) for k in (fg, bg))
            ratio = (max(a, b) + .05) / (min(a, b) + .05)
            assert ratio >= minimum, f'Clay/{mode}/{name} {fg}/{bg}: {ratio:.2f} < {minimum}'
            contrast_checks += 1
        found += 1
    assert found == 2, f'Clay {mode} needs Berry and Sage accent coverage'

xamls = [ROOT/'App.xaml', ROOT/'MainWindow.xaml', *ROOT.glob('Theme/**/*.xaml'),
         *ROOT.glob('step1.ThemeStudio/step1.b.ThemeStudioScreen/*.xaml')]
style_keys = set()
for path in xamls:
    tree = ET.parse(path).getroot()
    for node in tree.iter():
        if X+'Key' in node.attrib:
            style_keys.add(node.attrib[X+'Key'])
        if local(node.tag) == 'Style' and 'TargetType' in node.attrib:
            style_keys.add('{x:Type '+node.attrib['TargetType']+'}')
        for attribute in node.attrib:
            name = local(attribute)
            assert name not in ('LetterSpacing', 'box-shadow', 'backdrop-filter', 'gap'), (path, name)
        if local(node.tag) == 'ProgressBar' and 'Binding Progress' in node.attrib.get('Value', ''):
            assert 'Mode=OneWay' in node.attrib['Value'], f'{path}: read-only Progress binding must be explicitly OneWay'
        for child in node:
            child_name = local(child.tag)
            if '.' in child_name:
                prop = child_name.split('.', 1)[1]
                assert prop not in node.attrib, f'{path}: duplicate assignment to {prop}'
        if local(node.tag) in ('Border', 'Button', 'UserControl', 'ScrollViewer', 'Window'):
            direct_content = [c for c in node if '.' not in local(c.tag)]
            assert len(direct_content) <= 1, f'{path}: multiple content children'
    if local(tree.tag) == 'ResourceDictionary':
        direct_keys = [n.attrib[X+'Key'] for n in tree if X+'Key' in n.attrib]
        assert len(direct_keys) == len(set(direct_keys)), f'{path}: duplicate keys'

for path in xamls:
    text = path.read_text()
    for token in re.findall(r'\{DynamicResource ([^{}]+)\}', text):
        assert token in contract or token in style_keys, f'{path}: undefined dynamic token {token}'
    for reference in re.findall(r'\{StaticResource (\{x:Type [^}]+\}|[^{}]+)\}', text):
        assert reference in style_keys, f'{path}: undefined style {reference}'
    for node in ET.parse(path).getroot().iter():
        if local(node.tag) == 'ResourceDictionary' and 'Source' in node.attrib:
            # A merged dictionary may be named by a pack URI, which every
            # application-relative merge in App.xaml is. Treating that URI as a
            # file name under the XAML's own folder reported the whole shell as
            # broken; resolve the pack form to the assembly root instead.
            source = node.attrib['Source']
            pack = 'pack://application:,,,/'
            if source.startswith(pack):
                target = ROOT / source[len(pack):].lstrip('/')
            elif source.startswith('/'):
                target = ROOT / source.lstrip('/')
            else:
                target = path.parent / source
            assert target.is_file(), (path, source)
    posix = path.as_posix()
    if 'Theme/ThemeBento/' not in posix and 'Theme/ThemeClaymorphism/' not in posix and 'Theme/ThemeFluent/' not in posix and 'Theme/ThemeNeumorphism/' not in posix and 'Theme/ThemeLiquidGlass/' not in posix and 'Theme/ThemeGlassmorphism/' not in posix and 'Theme/ThemeSpatial/' not in posix and 'Theme/ThemeSwiss/' not in posix and 'Theme/ThemeHolographic/' not in posix and path.name != 'MainWindow.xaml':
        assert not re.search(r'#[0-9A-Fa-f]{6,8}\b', text), f'{path}: scattered literal color'
    if path.name == 'MainWindow.xaml':
        assert 'WindowState="Maximized"' in text, 'Shell must remain maximized'
    if path.parent.name == 'step1.b.ThemeStudioScreen':
        # A studio screen takes every metric and colour from the theme contract.
        # A TemplateBinding is not an exception to that: inside a ControlTemplate
        # it forwards a value the templated parent already took from a token
        # (ShellNavButton's template does exactly this), so it can never be a
        # hand-typed number. Literals are still rejected.
        for prop in ('FontSize', 'CornerRadius', 'Padding', 'Margin', 'Foreground', 'Background', 'BorderBrush'):
            for value in re.findall(rf'\b{prop}="([^"]+)"', text):
                assert value.startswith(('{DynamicResource ', '{Binding ', '{TemplateBinding ')), (path, prop, value)

for path in xamls:
    tree = ET.parse(path).getroot()
    class_name = tree.attrib.get(X+'Class')
    if not class_name:
        continue
    behind = Path(str(path) + '.cs')
    assert behind.is_file(), f'{path}: missing code-behind'
    namespace, name = class_name.rsplit('.', 1)
    code = behind.read_text()
    assert f'namespace {namespace};' in code and f'partial class {name} ' in code, (path, class_name)
    for node in tree.iter():
        for event in ('Click', 'SizeChanged', 'Loaded'):
            if event in node.attrib:
                assert re.search(r'\b' + re.escape(node.attrib[event]) + r'\s*\(', code), (path, event)

project = ET.parse(ROOT/'ProGlassAutomation.csproj').getroot()
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
assert project.find('.//TargetFramework').text == 'net8.0-windows7.0'
assert (ROOT / 'Theme/ThemeClaymorphism/ThemeClaymorphism.Light.xaml').is_file() and (ROOT / 'Theme/ThemeBento/ThemeBento.Light.xaml').is_file()
assert (ROOT / 'Theme/ThemeFluent/ThemeFluent.Light.xaml').is_file()
assert (ROOT / 'Theme/ThemeNeumorphism/ThemeNeumorphism.Light.xaml').is_file()
catalog = (ENGINE / 'step1.a.6.ThemeCatalog.cs').read_text()
assert 'ClayName = "Claymorphism"' in catalog and 'ClayResources.Load' in catalog
assert 'FluentName = "Fluent / Acrylic"' in catalog and 'FluentResources.Load' in catalog
assert 'NeumorphismName = "Neumorphism"' in catalog and 'NeumorphResources.Load' in catalog
assert 'ThemeGeometry.ThemeOwned' in catalog and 'SharedTokens' in catalog
runtime = (ROOT / 'Theme/step1.d.tests/step1.d.1.RuntimeChecks.cs').read_text()
assert 'ThemeService.InstalledThemes' in runtime and 'Apply("NotInstalled"' in runtime
assert 'Apply("Claymorphism"' not in runtime, 'Clay is installed; do not treat it as missing'
vm = (ROOT/'step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.3.ThemeStudioViewModel.cs').read_text()
assert 'SharedGeometry.Compose' in vm
panel = (ROOT/'step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.16.CustomizationPanel.xaml').read_text()
assert 'CanCustomizeBento' in panel and 'CanCustomizeClay' in panel and 'IsFluent' in panel and 'IsNeumorph' in panel
assert 'CanCustomizeGeometry' in panel and 'SelectedRounding' in panel
assert 'Visibility="{Binding CanCustomizeBento' in panel and 'Visibility="{Binding CanCustomizeClay' in panel and 'Visibility="{Binding IsFluent' in panel and 'Visibility="{Binding IsNeumorph' in panel
assert 'SelectedClayAccent' in panel and 'SelectedClayTypography' in panel and 'SelectedClaySheen' in panel
assert 'SelectedClaySpacing' in panel and 'SelectedClayTextContrast' in panel and 'SelectedClayPresetName' in panel
assert 'SelectedNeumorphDepth' in panel and 'SelectedNeumorphBlur' in panel and 'SelectedNeumorphIntensity' in panel
assert 'Bento studio' in panel and 'Clay studio' in panel and 'Fluent studio' in panel and 'Neumorphism studio' in panel
custom_vm = (ROOT/'step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs').read_text()
assert 'using ProGlassAutomation.ThemeClaymorphism;' in custom_vm
assert 'using ProGlassAutomation.ThemeNeumorphism;' in custom_vm
assert 'ApplyClayState' in custom_vm and 'lastSavedClaySnapshot' in custom_vm
assert 'ApplyNeumorphState' in custom_vm and 'lastSavedNeumorphSnapshot' in custom_vm
assert 'SharedGeometry.Compose' in custom_vm and 'SelectedRounding' in custom_vm
assert 'IsClay)' in custom_vm and 'ResetTheme' in custom_vm
contracts = (ROOT / 'Theme/step1.d.tests/step1.d.2.ContractChecks.cs').read_text()
assert 'ThemeService.InstalledThemes' in contracts
assert 'if (theme == ThemeService.Bento)' in contracts and 'VerifyStyleFailures(resources)' in contracts
print(f'PASS: {len(contract)} typed tokens × 2 complete variants; {contrast_checks} measured contrast pairs.')
print(f'PASS: {len(xamls)} XML documents, content/property checks, resource references, dictionary paths and no view-local styling literals.')
print('LIMITATION: source checks are not a C#/XAML compilation, runtime switching test, visual inspection, or accessibility certification.')

runpy.run_path(str(ROOT / 'Theme/step1.d.tests/step1.d.5.verify_isolation.py'), run_name='__main__')

analytics = ROOT / 'Theme/step1.f.Analytics'
for path in analytics.glob('*.cs'):
    text = path.read_text()
    assert not any(word in text for word in ('AnalyticsSamples', 'ThemeStudio', 'Application.Current', 'ThemeService', 'TryFindResource')), f'{path}: renderer must not own business data/theme lookup'
    assert not re.search(r'#[0-9A-Fa-f]{6,8}\b', text), f'{path}: renderer palette must come from dependency properties'
rendering = (analytics/'step1.f.4.BentoChart.Rendering.cs').read_text()
for native in ('ColumnSeries<double?>', 'RowSeries<double?>', 'StackedColumnSeries<double?>', 'LineSeries<double?>', 'PieSeries<double?>'):
    assert native in rendering, f'Missing real library renderer: {native}'
assert 'ActivityTrendGeometry' not in ''.join(path.read_text() for path in xamls), 'Decorative trend must not return as a fake chart'
# The shell must open on a working page, never on Theme Studio. The index was
# pinned to 1 (Business Reference) until the Inquiry register became the app's
# home; assert the rule, not the literal, because 0 is the Studio workspace.
customization = (ROOT/'step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs').read_text()
startup = re.search(r'private int selectedWorkspace = (\d+);', customization)
assert startup, 'startup workspace field is missing from the customization view model'
assert startup.group(1) != '0', 'the application must not start inside Theme Studio'
print('PASS: native renderer coverage, data/theme ownership boundaries, no fixed chart path, Business Reference startup.')
