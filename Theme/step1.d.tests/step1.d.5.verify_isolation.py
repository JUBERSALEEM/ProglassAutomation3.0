"""Portable theme-package ownership checks; not a substitute for WPF runtime validation."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
ENGINE = ROOT / 'Theme/step1.a.ThemeEngine'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
assert not list(ENGINE.rglob('*.xaml')), 'Shared engine must not own visual dictionaries'
packages = sorted(p for p in (ROOT / 'Theme').iterdir() if p.is_dir() and p.name.startswith('Theme'))
assert {p.name for p in packages} == {'ThemeBento', 'ThemeClaymorphism', 'ThemeFluent', 'ThemeNeumorphism', 'ThemeLiquidGlass', 'ThemeGlassmorphism', 'ThemeSpatial', 'ThemeSwiss', 'ThemeHolographic'}, 'Registered visual packages only'
catalog = (ENGINE / 'step1.a.6.ThemeCatalog.cs').read_text()
assert catalog.count('new ThemeDefinition(') == 9, 'Installed remains nine complete packages'
assert catalog.count('new ThemeIdentity(') == 9, 'Identity sequence is nine themes'
assert 'Holographic' in catalog and 'Glassmorphism' in catalog
for path in ROOT.glob('Theme/**/*.xaml'):
    assert any(path.resolve().is_relative_to(pkg.resolve()) for pkg in packages), f'Visual dictionary outside a theme package: {path}'


def normalized_type(value):
    return value.removeprefix('{x:Type ').removesuffix('}').removeprefix('analytics:')



def background_of(style):
    for child in style:
        if child.tag.endswith('}Setter') and child.attrib.get('Property') == 'Background':
            return child.attrib.get('Value')
    return None


def key_of(node):
    explicit = node.attrib.get(X + 'Key')
    if explicit:
        return 'type:' + normalized_type(explicit) if explicit.startswith('{x:Type ') else explicit
    if node.tag.endswith('}Style') and 'TargetType' in node.attrib:
        return 'type:' + normalized_type(node.attrib['TargetType'])
    return None


def collect(path, values, package):
    tree = ET.parse(path).getroot()
    for child in tree:
        key = key_of(child)
        if key:
            assert key not in values, f'duplicate package key: {key}'
            values[key] = child
    local_keys = {key_of(child) for child in tree if key_of(child)}
    for node in tree.iter():
        if node.tag.endswith('}ResourceDictionary') and 'Source' in node.attrib:
            target = (path.parent / node.attrib['Source']).resolve()
            assert target.is_relative_to(package.resolve()), f'cross-theme reference: {path}: {target}'
            assert target.is_file(), (path, target)
            collect(target, values, package)
    for reference in re.findall(r'\{StaticResource (\{x:Type [^}]+\}|[^{}]+)\}', path.read_text()):
        key = 'type:' + normalized_type(reference) if reference.startswith('{x:Type ') else reference
        assert key in local_keys, f'order-dependent static reference: {path}: {key}'


source = (ENGINE / 'step1.a.1.ThemeContract.cs').read_text().split('RequiredStyles')[1]
required = dict(re.findall(r'\[\"([^\"]+)\"\] = typeof\(([^)]+)\)', source))
required.update({'type:' + key: target for key, target in re.findall(r'\[typeof\(([^)]+)\)\] = typeof\(([^)]+)\)', source)})
assert len(required) == 47, 'Consumer style contract unexpectedly changed; review coverage'
for package in packages:
    stem = package.name
    components = package / 'step1.e.Components/step1.e.1.Components.xaml'
    for mode in ('Light', 'Dark'):
        values = {}
        collect(package / f'{stem}.{mode}.xaml', values, package)
        extras = package / f'{stem}.{mode}.Extras.xaml'
        if extras.is_file():
            collect(extras, values, package)
        if not components.is_file():
            continue
        collect(components, values, package)
        for key, target in required.items():
            assert key in values, f'missing theme style: {package.name}/{mode} {key}'
            node = values[key]
            assert node.tag.endswith('}Style') and normalized_type(node.attrib.get('TargetType', '')) == target, f'wrong theme style target: {key}'
        for path in package.rglob('*.xaml'):
            for reference in re.findall(r'\{DynamicResource ([^{}]+)\}', path.read_text()):
                assert reference in values, f'{package.name} borrows external resource: {reference}'
        # A plate is the recessed layer a toolbar, footer or well sits in, on top
        # of a card. Filling it with the card's own token makes every plate
        # disappear into its card and leaves the whole page one flat sheet —
        # which is exactly how Claymorphism shipped. Two packages are exempt by
        # design: Neumorphism is single-surface and shows depth through shadow
        # alone, LiquidGlass is a transparent frost over the wallpaper.
        if package.name not in {'ThemeNeumorphism', 'ThemeLiquidGlass'}:
            card = background_of(values['Card'])
            plate = background_of(values['Plate'])
            assert card and plate, (package.name, mode, 'Card/Plate must set Background')
            assert card != plate, (
                f'{package.name}/{mode}: Plate is filled with the card token {card}, '
                'so the recessed layer is invisible')

service = (ENGINE / 'step1.a.2.ThemeService.cs').read_text()
assert 'definition.LoadResources(appearance)' in service, 'Engine must delegate visual composition'
assert '.xaml' not in service, 'Orchestrator must not hardcode a visual dictionary'
assert 'MergedDictionaries.Add(styles)' not in service, 'Global visual styles must not survive theme replacement'
print('PASS: isolated Bento, Clay, Fluent, Neumorphism, Liquid Glass, Glassmorphism, Spatial, Swiss and Holographic packages, 47 required style/target contracts in both modes, no duplicate/cross-theme keys or engine visuals.')
