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

service_path = ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxService.cs"
service = service_path.read_text(encoding="utf-8")
fetch = service.split("private async Task<ItemDeliveryResult> DeliverAsync", 1)[1].split("private async Task AcknowledgeAsync", 1)[0]
assert 'Path.Combine(root, "incoming"' in fetch
staged_fetch = fetch.split("var stage = ", 1)[1]
assert staged_fetch.index("DownloadObjectAsync") < staged_fetch.index('File.WriteAllBytes(Path.Combine(stage, "manifest.json")')
assert staged_fetch.index('File.WriteAllBytes(Path.Combine(stage, "manifest.json")') < staged_fetch.index('File.WriteAllText(Path.Combine(stage, "READY")')
assert staged_fetch.index('File.WriteAllText(Path.Combine(stage, "READY")') < staged_fetch.index("Directory.Move(stage, finalPath)")
assert staged_fetch.index("Directory.Move(stage, finalPath)") < staged_fetch.index("await AcknowledgeAsync(plan, manifestHash, docs, ct)")
assert staged_fetch.index("await AcknowledgeAsync(plan, manifestHash, docs, ct)") < staged_fetch.index("WriteDeliveryReceipt(finalPath")
assert '"integrity check failed for "' in staged_fetch
assert "CleanupStalePartials" not in service
assert 'InquiryInboxStates.ImportPending' in service
assert 'existingItem?.State == InquiryInboxStates.ImportPending' in service
ingest_service = (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxIngestService.cs").read_text(encoding="utf-8")
assert 'private static void MarkImportPending' in ingest_service
assert 'item.State = InquiryInboxStates.ImportPending;' in ingest_service
view_model = (ROOT / "Inquiry/ViewModels/InquiryInboxViewModel.cs").read_text(encoding="utf-8")
assert '"Re-import from disk"' in view_model and 'Ledger.State is InquiryInboxStates.Downloaded or InquiryInboxStates.ImportPending' in view_model
assert 'Content="{Binding SelectedIngestActionLabel}"' in (ROOT / "Inquiry/Views/InquiryInboxView.xaml").read_text(encoding="utf-8")

assert 'InquiryInboxStates.DeliveredElsewhere' in service
assert 'ReconcileUnofferedItems(db, plannedIds)' in service
assert 'AlreadyDelivered' in (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxClient.cs").read_text(encoding="utf-8")
assert 'Storage download failed (HTTP ' in (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxClient.cs").read_text(encoding="utf-8")
assert 'Delivered to this PC on ' in service and 'Customer submissions -> open the submission -> Download' in service

assert 'outcome.Failed++' in service and 'if (!stillOffered && !payloadPlaced)' in service
assert 'RunAlreadyDeliveredConflict' in (ROOT / "Theme/step1.d.tests/step1.d.16.InquiryInboxDeliveryOrderingChecks.cs").read_text(encoding="utf-8")
assert 'RunStorageFailureThenPlanDisappearance' in (ROOT / "Theme/step1.d.tests/step1.d.16.InquiryInboxDeliveryOrderingChecks.cs").read_text(encoding="utf-8")

assert 'private string SelectedErrorText => SelectedItem?.Ledger.State == InquiryInboxStates.DeliveredElsewhere' in view_model
assert 'InquiryInboxStates.DeliveredElsewhere => "Information"' in view_model

assert 'No submissions are waiting. The website hands a submission to this PC only after a ProGlass operator accepts it; everything accepted and not yet delivered appears here.' in service
assert 'RunEmptyPlanIsNeutral' in (ROOT / "Theme/step1.d.tests/step1.d.16.InquiryInboxDeliveryOrderingChecks.cs").read_text(encoding="utf-8")

assert 'AcknowledgedAtUtc' in (ROOT / "Inquiry/Models/Inquiry/InquiryInboxState.cs").read_text(encoding="utf-8")
assert 'AcknowledgedBy' in (ROOT / "Inquiry/Models/Inquiry/InquiryInboxState.cs").read_text(encoding="utf-8")
assert 'Signed in as {AccessReport.AccountEmail}' in view_model
migrations = (ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs").read_text(encoding="utf-8")
assert 'new(1009, "Inquiry: persist website acknowledgement identity"' in migrations
assert 'RequireColumn(connection, "InboxItems", "AcknowledgedAtUtc")' in migrations
assert 'RequireColumn(connection, "InboxItems", "AcknowledgedBy")' in migrations
settings = (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxSettings.cs").read_text(encoding="utf-8")
assert '[JsonPropertyName("pollIntervalSeconds")] public int PollIntervalSeconds { get; set; } = 15;' in settings
assert 'legacyMinutes is 1 or 5' in settings
assert 'ShowDeliveryFiles(row)' in (ROOT / "Inquiry/Views/InquiryInboxView.xaml.cs").read_text(encoding="utf-8")
assert 'CompanyDocumentsFolderPath' in view_model
inbox_service = (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxService.cs").read_text(encoding="utf-8")
assert '"Related Documents"' in inbox_service
assert 'CompanyArchiveFileName' in inbox_service
assert '15, "15 seconds"' in view_model and '30, "30 seconds"' in view_model
assert 'Files could not be verified or organized:' in view_model
assert 'SelectedIngestActionLabel' in view_model
assert 'awaitingLocalImport > 0' in (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxService.cs").read_text(encoding="utf-8")
assert 'current.InnerException' in (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxIngestService.cs").read_text(encoding="utf-8")
