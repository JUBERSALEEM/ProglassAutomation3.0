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
    assert 'SelectionBrush" Value="{DynamicResource AccentPrimary}"' in inputs, theme.name
    assert 'SelectionTextBrush" Value="{DynamicResource TextOnAccent}"' in inputs, theme.name
    assert 'Property="IsMouseOver" Value="True"' in textbox_template, theme.name
    assert 'Value="{DynamicResource StateHover}"' in textbox_template, theme.name

assert 'Width="70" Margin="{DynamicResource MarginInline}"' in inbox_xaml
assert 'Text="Supabase account email"' in inbox_xaml
assert 'Text="Supabase password"' in inbox_xaml
assert 'BorderBrush" Value="{DynamicResource BorderStrong}"' in inbox_xaml
repository = (ROOT / "Inquiry/Services/Inquiry/InquiryRepository.cs").read_text(encoding="utf-8")
migrations = (ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs").read_text(encoding="utf-8")
runner = (ROOT / "Inquiry/Services/Database/SchemaMigrations/SchemaMigrationRunner.cs").read_text(encoding="utf-8")
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
