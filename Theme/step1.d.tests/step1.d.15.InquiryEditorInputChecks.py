from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
EDITOR = ROOT / "Inquiry/Views/InquiryEditorView.xaml"
VIEW_MODEL = ROOT / "Inquiry/ViewModels/InquiryEditorViewModel.cs"
ET.parse(EDITOR)
view = EDITOR.read_text(encoding="utf-8")
model = VIEW_MODEL.read_text(encoding="utf-8")

for label, source, prop in (
    ("Customer", "Customers", "CustomerName"),
    ("Customer location", "CustomerLocations", "CustomerLocation"),
    ("Salesman", "Salesmen", "SalesmanName"),
):
    assert f'AutomationProperties.Name="{label}"' in view
    assert f'ItemsSource="{{Binding {source}}}"' in view
    assert f'Text="{{Binding {prop}, UpdateSourceTrigger=PropertyChanged}}"' in view
assert view.count('IsEditable="True" IsTextSearchEnabled="True" StaysOpenOnEdit="True"') == 3

for field, record_field in (
    ("CustomerName", "CustomerName"),
    ("CustomerLocation", "CustomerLocation"),
    ("ProjectName", "ProjectName"),
    ("SalesmanName", "SalesmanName"),
):
    assert f'Record.{record_field} = {field}.Trim();' in model
assert "private bool SetFormField<T>" in model
assert "private void NotifyCanSaveChanged()" in model
assert "if (await SaveAsync() != null)" in model
salesman = model.split("public string SalesmanName", 1)[1].split("private string _notes", 1)[0]
assert "Salesmen.Add" not in salesman

for theme in sorted((ROOT / "Theme").glob("Theme*")):
    path = theme / "step1.e.Components/step1.e.4.Inputs.xaml"
    if not path.exists():
        continue
    ET.parse(path)
    controls = path.read_text(encoding="utf-8").split('<Style TargetType="ComboBox">', 1)[1].split('</Style>', 1)[0]
    assert 'x:Name="PART_EditableTextBox"' in controls, theme.name
    assert 'x:Name="ContentSite"' in controls, theme.name
    assert 'Property="IsEditable" Value="True"' in controls, theme.name
    assert 'TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"' in controls, theme.name
print("PASS: Inquiry editable input bindings, dirty/save mapping, and all theme editable ComboBox parts")
