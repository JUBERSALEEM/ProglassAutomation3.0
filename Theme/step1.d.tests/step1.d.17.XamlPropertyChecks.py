"""Every XAML attribute must exist on the type that carries it.

Why this file exists: the registers were given hand-written `ControlTemplate`s and
one of them carried `HorizontalContentAlignment` on a `ContentPresenter`. That is
a `ContentControl` property; `ContentPresenter` does not declare it. The XAML
compiler rejects it (`MC3072` / `XLS0413` / `XDG0012`) — and nothing in the
portable checks caught it, because source checks read text and text cannot tell
you what a WPF type declares.

Two gates, both cheap and both deterministic:

1. DENY — a property that does not exist on a type, in any file, anywhere.
   These are the confusions that actually happen: content-alignment properties on
   a `ContentPresenter`, chrome properties on a `TextBlock` or a `Grid`, text
   properties on a `Border`.
2. ALLOW — for the four types this codebase templates by hand, every attribute
   used in the repository must be in a reviewed list. A new attribute fails this
   check until somebody confirms it exists on the type and adds it here. That is
   the point: the reviewed list is the cross-check.

Everything below was verified against the WPF type declarations, not against
memory of a similar type:

  FrameworkElement   Margin, Width, Height, MinWidth, MaxWidth, MinHeight,
                     MaxHeight, HorizontalAlignment, VerticalAlignment,
                     Style, Tag, ToolTip, Cursor, ClipToBounds, Visibility,
                     IsHitTestVisible, Name (x:Name)
  Control            Background, Foreground, FontFamily, FontSize, FontWeight,
                     FontStyle, Padding, BorderBrush, BorderThickness,
                     HorizontalContentAlignment, VerticalContentAlignment,
                     Template, Focusable, IsTabStop, TabIndex
  ContentPresenter   Content, ContentTemplate, ContentTemplateSelector,
                     ContentStringFormat, ContentSource, RecognizesAccessKey
                     -> NOT HorizontalContentAlignment, NOT Padding,
                        NOT Background, NOT BorderBrush
  TextBlock          Text, TextAlignment, TextTrimming, TextWrapping,
                     FontFamily, FontSize, FontWeight, FontStyle, Foreground,
                     Background, Padding, LineHeight
                     -> no BorderBrush / BorderThickness / CornerRadius
  Border             Background, BorderBrush, BorderThickness, CornerRadius,
                     Padding, Child -> no FontSize / FontWeight / Foreground
  Grid / StackPanel  Background, RowDefinitions, ColumnDefinitions
                     -> no Padding, no BorderBrush, no CornerRadius
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
VIEWS = [ROOT / 'Inquiry/Views/InquiryListView.xaml',
         ROOT / 'step2.ClientManagement/step2.d.1.ClientWorkspaceView.xaml',
         ROOT / 'step2.ClientManagement/step2.d.2.ClientEditorView.xaml']

# 1 ── properties that do not exist on these types, in any file ─────────────
DENY = {
    'ContentPresenter': {'HorizontalContentAlignment', 'VerticalContentAlignment',
                         'Padding', 'Background', 'BorderBrush', 'BorderThickness',
                         'CornerRadius'},
    'TextBlock': {'BorderBrush', 'BorderThickness', 'CornerRadius'},
    'Grid': {'Padding', 'BorderBrush', 'BorderThickness', 'CornerRadius', 'Text'},
    'StackPanel': {'Padding', 'BorderBrush', 'BorderThickness', 'CornerRadius', 'Text'},
    'DockPanel': {'Padding', 'BorderBrush', 'BorderThickness', 'CornerRadius', 'Text'},
    'UniformGrid': {'Padding', 'BorderBrush', 'CornerRadius', 'Text'},
    'Border': {'FontSize', 'FontWeight', 'Foreground', 'Text', 'TextAlignment'},
    'ContentControl': {'Text', 'TextAlignment'},
    'Thumb': {'Text', 'TextAlignment', 'Content'},
}

# 2 ── reviewed attribute set for the hand-templated types ──────────────────
ALLOW = {
    'ContentPresenter': {'Content', 'ContentTemplate', 'ContentTemplateSelector',
                         'ContentStringFormat', 'ContentSource', 'RecognizesAccessKey',
                         'HorizontalAlignment', 'VerticalAlignment', 'Margin',
                         'SnapsToDevicePixels', 'IsHitTestVisible', 'x:Name',
                         'Grid.Row', 'Grid.Column', 'Grid.RowSpan', 'Grid.ColumnSpan'},
    'Thumb': {'Width', 'Height', 'MinWidth', 'MinHeight', 'MaxWidth', 'MaxHeight',
              'HorizontalAlignment', 'VerticalAlignment', 'Margin', 'Style',
              'Cursor', 'Background', 'Template', 'x:Name', 'ToolTip',
              'DragDelta', 'DragStarted', 'DragCompleted'},
    'ControlTemplate': {'TargetType', 'x:Key'},
    'TextBlock': {'Text', 'TextAlignment', 'TextTrimming', 'TextWrapping',
                  'FontFamily', 'FontSize', 'FontWeight', 'FontStyle', 'Foreground',
                  'Background', 'Padding', 'LineHeight', 'Margin', 'MinWidth',
                  'MinHeight', 'MaxWidth', 'MaxHeight', 'Width', 'Height',
                  'HorizontalAlignment', 'VerticalAlignment', 'Style', 'Tag',
                  # IsHitTestVisible is UIElement's, and a TextBlock overlay needs
                  # it so clicks fall through to the TextBox underneath (the
                  # search hint). Reviewed, not invented.
                  'ToolTip', 'Visibility', 'IsHitTestVisible', 'x:Name',
                  'Grid.Row', 'Grid.Column',
                  'Grid.RowSpan', 'Grid.ColumnSpan', 'DockPanel.Dock',
                  'AutomationProperties.Name', 'AutomationProperties.LiveSetting',
                  'AutomationProperties.AutomationId', 'AutomationProperties.HelpText'},
    'Grid': {'Background', 'RowDefinitions', 'ColumnDefinitions', 'Margin',
             'MinWidth', 'MinHeight', 'MaxWidth', 'MaxHeight', 'Width', 'Height',
             'HorizontalAlignment', 'VerticalAlignment', 'Visibility', 'Cursor',
             'ClipToBounds', 'IsHitTestVisible', 'Style', 'Tag', 'ToolTip', 'x:Name',
             'Grid.Row', 'Grid.Column', 'Grid.RowSpan', 'Grid.ColumnSpan',
             'DockPanel.Dock', 'MouseLeftButtonDown', 'MouseLeftButtonUp',
             'MouseMove', 'SizeChanged', 'AutomationProperties.Name'},
    'StackPanel': {'Orientation', 'IsItemsHost', 'Margin', 'MinWidth', 'MinHeight',
                   'MaxWidth', 'MaxHeight', 'Width', 'Height', 'HorizontalAlignment',
                   'VerticalAlignment', 'Visibility', 'Style', 'Tag', 'x:Name',
                   'Grid.Row', 'Grid.Column', 'Grid.RowSpan', 'Grid.ColumnSpan',
                   'DockPanel.Dock', 'KeyboardNavigation.DirectionalNavigation'},
    'DockPanel': {'LastChildFill', 'Margin', 'MinWidth', 'MinHeight', 'Width',
                  'Height', 'HorizontalAlignment', 'VerticalAlignment', 'Style',
                  'x:Name', 'Grid.Row', 'Grid.Column', 'Grid.RowSpan', 'Grid.ColumnSpan'},
    'UniformGrid': {'Columns', 'Rows', 'FirstColumn', 'Margin', 'MinWidth',
                    'MinHeight', 'Width', 'Height', 'HorizontalAlignment',
                    'VerticalAlignment', 'x:Name', 'Grid.Row', 'Grid.Column',
                    'Grid.RowSpan', 'Grid.ColumnSpan'},
    'Border': {'Background', 'BorderBrush', 'BorderThickness', 'CornerRadius',
               'Padding', 'Child', 'Margin', 'MinWidth', 'MinHeight', 'MaxWidth',
               'MaxHeight', 'Width', 'Height', 'HorizontalAlignment',
               'VerticalAlignment', 'Style', 'Tag', 'ToolTip',
               'ToolTipService.ShowDuration', 'Opacity', 'ClipToBounds',
               'IsHitTestVisible', 'Visibility', 'SnapsToDevicePixels', 'x:Name',
               'Grid.Row', 'Grid.Column', 'Grid.RowSpan', 'Grid.ColumnSpan',
               'DockPanel.Dock'},
}
ALLOW_ATTACHED_SUFFIX = ('.Row', '.Column', '.RowSpan', '.ColumnSpan')

files = sorted(ROOT.rglob('*.xaml'))
assert files, 'no XAML found'
problems = []
reviewed = 0
for path in files:
    # A malformed dictionary is itself a build break, so parsing is check one.
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as error:
        problems.append(f'{path.relative_to(ROOT)}: not well-formed XAML: {error}')
        continue
    for element in root.iter():
        tag = element.tag.split('}')[-1]
        names = {name.replace(X, 'x:'): value for name, value in element.attrib.items()}
        for name in names:
            if name in DENY.get(tag, ()):  # 1
                problems.append(
                    f'{path.relative_to(ROOT)}: {tag} has no property {name!r} '
                    f'(it is declared on a different WPF type)')
            allowed = ALLOW.get(tag)  # 2
            if allowed is not None:
                reviewed += 1
                if name in allowed:
                    continue
                if any(name.endswith(suffix) for suffix in ALLOW_ATTACHED_SUFFIX):
                    continue
                problems.append(
                    f'{path.relative_to(ROOT)}: {tag} uses {name!r}, which is not in '
                    'the reviewed property set - confirm the property exists on the '
                    'type, then add it to ALLOW in this file')

# 1b ── A Style may not set the same property twice ─────────────────────────
# Two Setters for one property in the same Style is a XAML compile error, and it
# happens precisely when a style is edited in two passes (one setter added for a
# new token, the original left behind). Caught this way on MainWindow's
# ShellBannerRule while fixing the banner height.
for path in files:
    try:
        root_for_styles = ET.parse(path).getroot()
    except ET.ParseError:
        continue   # already reported above
    for style in root_for_styles.iter():
        if not style.tag.endswith('Style'):
            continue
        seen = set()
        for setter in style:
            if not setter.tag.endswith('Setter'):
                continue
            prop = setter.get('Property')
            if prop in seen:
                problems.append(
                    f'{path.relative_to(ROOT)}: Style sets {prop!r} twice '
                    '(a XAML compile error, not a style override)')
            seen.add(prop)

# The two registers must actually use the mechanism these tests describe: the
# presenter is stretched, and the content alignment lives on the cell, which is a
# ContentControl and really does declare it.
for view in VIEWS[:2]:
    text = view.read_text()
    problems += [] if 'Property="HorizontalContentAlignment"' in text else [
        f'{view.relative_to(ROOT)}: content alignment must live on the cell/header '
        'style, which is a ContentControl']
    problems += [] if 'HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"' in text else [
        f'{view.relative_to(ROOT)}: the presenter must take its alignment from the '
        'cell style through a TemplateBinding']

# 2b ── Every DynamicResource key must exist in the theme dictionaries ──────
# A missing key is not a compile error and not a load error: WPF silently leaves
# the property at its default, so a chip with a mistyped corner radius simply
# renders square and nobody knows why. This caught `PillCornerRadius` (which does
# not exist; the token is `ChipCornerRadius`) on the first run of this file.
THEME_KEYS = set()
for dictionary in ROOT.glob('Theme/Theme*/**/*.xaml'):
    THEME_KEYS |= set(re.findall(r'x:Key="([^":]+)"', dictionary.read_text()))
for view in VIEWS + [ROOT / 'MainWindow.xaml']:
    if not view.is_file():
        continue
    text = view.read_text()
    # A view may also define and reference its own keys (the Inquiry register's
    # ContextMenu, for example) - those are local, not theme tokens.
    local_keys = set(re.findall(r'x:Key="([^":]+)"', text))
    for key in re.findall(r'\{DynamicResource\s+([^}]+?)\s*\}', text):
        if key.startswith('{x:Type') or ' ' in key:
            continue  # {DynamicResource {x:Type DataGrid}} is a style lookup
        if key not in THEME_KEYS and key not in local_keys:
            problems.append(
                f'{view.relative_to(ROOT)}: DynamicResource {key!r} is not defined by '
                'any theme dictionary; the property would silently keep its default')

# 3 ── TemplateBinding targets, and view-owned keys that must resolve ───────
# A TemplateBinding naming a property the template target does not declare fails
# at load time, not at compile time, so nothing catches it until the window is
# opened. These are the two template targets this codebase writes by hand.
TEMPLATE_TARGETS = {
    'DataGridCell': {'Background', 'BorderBrush', 'BorderThickness', 'Padding',
                     'Foreground', 'FontFamily', 'FontSize', 'FontWeight',
                     'HorizontalContentAlignment', 'VerticalContentAlignment',
                     'Content', 'ContentTemplate', 'SnapsToDevicePixels',
                     'IsEnabled', 'Focusable', 'Template'},
    'DataGridColumnHeader': {'Background', 'BorderBrush', 'BorderThickness', 'Padding',
                             'Foreground', 'FontFamily', 'FontSize', 'FontWeight',
                             'HorizontalContentAlignment', 'VerticalContentAlignment',
                             'Content', 'ContentTemplate', 'SnapsToDevicePixels',
                             'IsEnabled', 'Focusable', 'Template'},
}
for view in VIEWS[:2]:
    text = view.read_text()
    root = ET.parse(view).getroot()
    for template in root.iter('{http://schemas.microsoft.com/winfx/2006/xaml/presentation}ControlTemplate'):
        target = template.attrib.get('TargetType', '')
        allowed = TEMPLATE_TARGETS.get(target.split(':')[-1])
        if allowed is None:
            continue
        serialized = ET.tostring(template, encoding='unicode')
        for binding in re.findall(r'\{TemplateBinding\s+([A-Za-z.]+)\}', serialized):
            if binding not in allowed:
                problems.append(
                    f'{view.relative_to(ROOT)}: TemplateBinding to {binding!r} in a '
                    f'{target} template, which does not declare it')
    # View-owned style keys are resolved at load time; a typo is a crash on open.
    defined = set(re.findall(r'x:Key="([A-Za-z0-9]+)"', text))
    for key in re.findall(r'\{StaticResource ((?:Client|Inquiry)[A-Za-z0-9]+)\}', text):
        if key not in defined:
            problems.append(
                f'{view.relative_to(ROOT)}: StaticResource {key!r} is used but not '
                'defined in this view')

if problems:
    print('FAIL: XAML property checks')
    for problem in problems:
        print('  -', problem)
    raise SystemExit(1)

print(f'PASS: {len(files)} XAML dictionaries parse, {reviewed} attributes on hand-templated '
      'types are in the reviewed set, and no attribute is placed on a type that does not declare it')
