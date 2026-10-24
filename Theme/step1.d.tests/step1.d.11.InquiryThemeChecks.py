from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
LIST_VIEW = ROOT / "Inquiry/Views/InquiryListView.xaml"
INBOX_VIEW = ROOT / "Inquiry/Views/InquiryInboxView.xaml"

for view in (LIST_VIEW, INBOX_VIEW):
    ET.parse(view)

list_xaml = LIST_VIEW.read_text(encoding="utf-8")
inbox_xaml = INBOX_VIEW.read_text(encoding="utf-8")

# Theme-owned card styles must be dynamically looked up, not captured as a
# StaticResource base style when the view is first loaded. Keep their padding
# owned by each theme so card content never hugs its outline.
assert 'Style="{DynamicResource HeroCard}"' in list_xaml
assert list_xaml.count('Style="{DynamicResource Card}"') >= 2
assert list_xaml.count('Style="{DynamicResource MetricCard}"') >= 3
assert 'Style="{DynamicResource Card}"' in inbox_xaml
assert 'Style="{DynamicResource StatusBadge}"' in inbox_xaml

consumer_xaml = sorted(
    view for view in ROOT.rglob("*.xaml")
    if view.relative_to(ROOT).parts[0] != "Theme"
)
for view in consumer_xaml:
    root = ET.parse(view).getroot()
    for element in root.iter():
        if not element.tag.endswith("ContentControl"):
            continue
        style = element.attrib.get("Style", "")
        if any(f"{{DynamicResource {role}}}" in style
               for role in ("Card", "HeroCard", "MetricCard", "Plate", "AccentPlate", "ElevatedPlate")):
            padding = element.attrib.get("Padding")
            assert padding is None or padding == "{TemplateBinding Padding}", (view, element.attrib)
    text = view.read_text(encoding="utf-8")
    assert 'BasedOn="{StaticResource Card}"' not in text, view
    assert "CompactCard" not in text, view

# Controls that have theme-owned templates must explicitly track replacement
# of the active theme resource dictionary.
for name, xaml in (("Inquiry register", list_xaml), ("Website inbox", inbox_xaml)):
    for control in ("ComboBox", "TextBox", "CheckBox", "DataGrid"):
        assert f'Style="{{DynamicResource {{x:Type {control}}}}}"' in xaml, (name, control)

# All nine active themes must provide the card/badge roles consumed above.
themes = sorted(path for path in (ROOT / "Theme").glob("Theme*") if path.is_dir())
assert len(themes) == 9, len(themes)
for theme in themes:
    foundation = (theme / "step1.e.Components/step1.e.2.Foundation.xaml").read_text(encoding="utf-8")
    workspace = (theme / "step1.e.Components/step1.e.9.Workspace.xaml").read_text(encoding="utf-8")
    assert 'x:Key="Card"' in foundation, theme.name
    card_style = foundation.split('<Style x:Key="Card"', 1)[1].split('</Style>', 1)[0]
    assert 'Value="{DynamicResource PaddingCard}"' in card_style, theme.name
    assert 'x:Key="HeroCard"' in workspace, theme.name
    hero_style = workspace.split('<Style x:Key="HeroCard"', 1)[1].split('</Style>', 1)[0]
    assert 'Value="{DynamicResource PaddingCard}"' in hero_style, theme.name
    assert 'x:Key="MetricCard"' in workspace, theme.name
    assert 'x:Key="StatusBadge"' in workspace, theme.name

    inputs = (theme / "step1.e.Components/step1.e.4.Inputs.xaml").read_text(encoding="utf-8")
    textbox_template = inputs.split('<Setter Property="Template"', 1)[1].split('</ControlTemplate>', 1)[0]
    assert 'SelectionBrush" Value="{DynamicResource StateSelected}"' in inputs, theme.name
    assert 'SelectionTextBrush" Value="{DynamicResource TextPrimary}"' in inputs, theme.name
    assert 'Property="IsMouseOver" Value="True"' in textbox_template, theme.name
    assert 'Value="{DynamicResource StateHover}"' in textbox_template, theme.name
    assert 'TextBoxFocusBehavior.SelectAllOnFocus" Value="True"' in inputs, theme.name
    password_style = inputs.split('<Style TargetType="PasswordBox">', 1)[1].split('</Style>', 1)[0]
    assert '<ControlTemplate TargetType="PasswordBox">' in password_style, theme.name
    assert 'x:Name="PART_ContentHost" Focusable="False" IsTabStop="False"' in password_style, theme.name
    assert 'x:Name="Frame"' in password_style and 'DynamicResource' in password_style, theme.name
    for appearance in ("Light", "Dark"):
        palette = (theme / f"{theme.name}.{appearance}.xaml").read_text(encoding="utf-8")
        selection_opacity = float(palette.split('x:Key="SelectionOpacity">', 1)[1].split('<', 1)[0])
        assert 0 < selection_opacity < 1, (theme.name, appearance, selection_opacity)

focus_behavior = (ROOT / "Theme/step1.g.Customization/step1.g.7.TextBoxFocusBehavior.cs").read_text(encoding="utf-8")
assert 'textBox.SelectAll()' in focus_behavior
assert 'PreviewMouseLeftButtonDown' in focus_behavior

assert 'Width="70" Margin="{DynamicResource MarginInline}"' in inbox_xaml
assert 'Text="Supabase account email"' in inbox_xaml
assert 'Text="Supabase password"' in inbox_xaml
assert 'Style="{DynamicResource {x:Type PasswordBox}}"' in inbox_xaml
assert 'InboxPasswordBox' not in inbox_xaml
assert 'Header=""' not in inbox_xaml
for width in ("1.1*", "1.15*", "1.2*", "1*", "0.8*", "0.85*"):
    assert f'Width="{width}"' in inbox_xaml, width

# Outlined secondary actions must remain visible in themes where their
# previous SubtleButton role deliberately zeroed both fill and stroke.
for theme_name in ("ThemeBento", "ThemeHolographic", "ThemeSwiss"):
    buttons = (ROOT / "Theme" / theme_name / "step1.e.Components/step1.e.3.Buttons.xaml").read_text(encoding="utf-8")
    subtle = buttons.split('<Style x:Key="SubtleButton"', 1)[1].split('</Style>', 1)[0]
    assert 'Value="{DynamicResource SurfaceSecondary}"' in subtle, theme_name
    assert 'Value="{DynamicResource BorderSubtle}"' in subtle, theme_name
glass_stroke = (ROOT / "Theme/ThemeGlassmorphism/ThemeGlassmorphism.Light.Extras.xaml").read_text(encoding="utf-8")
assert 'x:Key="GlassStrokeBrush" Color="#99566F86"' in glass_stroke

workspace = (ROOT / "Inquiry/Views/InquiryWorkspaceView.xaml.cs").read_text(encoding="utf-8")
unloaded = workspace.split('private void OnUnloaded', 1)[1].split('private void OnOwnerWindowClosed', 1)[0]
assert '_inboxService.Dispose()' not in unloaded
assert 'Closed += OnOwnerWindowClosed' in workspace
assert '_inboxService.Dispose()' in workspace.split('private void OnOwnerWindowClosed', 1)[1]

repository = (ROOT / "Inquiry/Services/Inquiry/InquiryRepository.cs").read_text(encoding="utf-8")
migrations = (ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs").read_text(encoding="utf-8")
runner = (ROOT / "Inquiry/Services/Database/SchemaMigrations/SchemaMigrationRunner.cs").read_text(encoding="utf-8")
migration_1005 = migrations.split('new(1005,', 1)[1].split('new(1006,', 1)[0]
assert 'CreateTableIfNotExists(connection, @"\n' in migration_1005
assert 'CreateTableIfNotExists(connection, @"""' not in migration_1005
assert 'CREATE TABLE IF NOT EXISTS ""InquiryInboxItems""' in migration_1005
assert 'EnsureInboxLedgerCurrent(InquiryDbContext.DatabaseFilePath)' in repository
assert 'RepairIfNeeded(dbPath, 1009, "Inquiry InboxItems ledger"' in migrations
assert 'BackupBeforeMigrate(dbPath, backupVersion)' in runner
assert 'VerifyDatabaseIntegrity(connection)' in runner

color_plate = (ROOT / "Theme/step1.g.Customization/step1.g.6.ColorPlateService.cs").read_text(encoding="utf-8")
assert 'ApplyTextOnAccent(package, accent)' in color_plate
assert 'Roles.Append("TextOnAccent")' in color_plate
client = (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxClient.cs").read_text(encoding="utf-8")
service = (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxService.cs").read_text(encoding="utf-8")
view_model = (ROOT / "Inquiry/ViewModels/InquiryInboxViewModel.cs").read_text(encoding="utf-8")
assert '403 => InboxRemoteErrorKind.PermissionDenied' in client
assert 'InboxRemoteErrorKind.PermissionDenied => "Access denied"' in service
assert 'Signed in — this account is not authorized for the website inbox' in view_model

print("PASS: inquiry theme styles, interval spacing, inbox schema repair, dynamic input interaction, and auth/authorization diagnostics")
