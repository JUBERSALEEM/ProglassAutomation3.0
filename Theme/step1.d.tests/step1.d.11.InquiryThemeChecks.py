from pathlib import Path
import re
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
    combo_template = inputs.split('<Style TargetType="ComboBox">', 1)[1].split('</Style>', 1)[0]
    assert 'x:Name="PART_EditableTextBox"' in combo_template, theme.name
    assert 'x:Name="ContentSite"' in combo_template, theme.name
    assert 'Property="IsEditable" Value="True"' in combo_template, theme.name
    assert 'TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"' in combo_template, theme.name
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

editor_xaml_path = ROOT / "Inquiry/Views/InquiryEditorView.xaml"
editor_xaml = editor_xaml_path.read_text(encoding="utf-8")
ET.parse(editor_xaml_path)
for field, source in (("Customer", "Customers"), ("Customer location", "CustomerLocations"), ("Salesman", "Salesmen")):
    assert 'IsEditable="True" IsTextSearchEnabled="True" StaysOpenOnEdit="True"' in editor_xaml
    assert f'ItemsSource="{{Binding {source}}}"' in editor_xaml
    assert f'AutomationProperties.Name="{field}"' in editor_xaml
    assert f'Text="{{Binding {"CustomerName" if field == "Customer" else "CustomerLocation" if field == "Customer location" else "SalesmanName"}, UpdateSourceTrigger=PropertyChanged}}"' in editor_xaml
editor_vm = (ROOT / "Inquiry/ViewModels/InquiryEditorViewModel.cs").read_text(encoding="utf-8")
assert 'Record.CustomerName = CustomerName.Trim();' in editor_vm
assert 'Record.CustomerLocation = CustomerLocation.Trim();' in editor_vm
assert 'Record.SalesmanName = SalesmanName.Trim();' in editor_vm
assert 'private void NotifyCanSaveChanged()' in editor_vm
assert 'private bool SetFormField<T>' in editor_vm and 'IsDirty = true;' in editor_vm
assert editor_vm.count('NotifyCanSaveChanged();') >= 3
assert 'if (await SaveAsync() != null)' in editor_vm
salesman_setter = editor_vm.split('public string SalesmanName', 1)[1].split('private string _notes', 1)[0]
assert 'Salesmen.Add' not in salesman_setter

focus_behavior = (ROOT / "Theme/step1.g.Customization/step1.g.7.TextBoxFocusBehavior.cs").read_text(encoding="utf-8")
assert 'textBox.SelectAll()' in focus_behavior
assert 'PreviewMouseLeftButtonDown' in focus_behavior

assert 'Width="120" Margin="{DynamicResource MarginInline}"' in inbox_xaml
assert 'ItemsSource="{Binding PollIntervalOptions}"' in inbox_xaml
assert 'SelectedItem="{Binding SelectedPollInterval, Mode=TwoWay}"' in inbox_xaml
assert 'Text="Supabase account email"' in inbox_xaml
assert 'Text="Supabase password"' in inbox_xaml
assert 'Style="{DynamicResource {x:Type PasswordBox}}"' in inbox_xaml
assert 'InboxPasswordBox' not in inbox_xaml
assert 'Header=""' not in inbox_xaml
assert 'x:Key="InboxCenteredHeader"' in inbox_xaml
assert 'x:Key="InboxCenterCell"' in inbox_xaml
assert 'TextAlignment" Value="Center"' in inbox_xaml
assert 'HorizontalAlignment="Center"' in inbox_xaml
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
assert 'matchesSavedConnection' in view_model
assert 'Save connection to use them for automatic checks' in view_model
assert "Account email changed. Enter that account's Supabase Auth password" in view_model
assert 'CurrentAuthConfiguration()' in client
assert '_authenticatedConfiguration' in client
assert 'ToolTip="{Binding Error}"' in inbox_xaml
assert 'private static HttpClient _http' in client
assert 'private static void Reset()' in client
assert 'catch (ObjectDisposedException) when (attempt == 0)' in client
assert 'ShutdownProcessHttpClient' in client
assert 'get_my_inquiry_access_v1' in client
access_report = (ROOT / "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxAccessReport.cs").read_text(encoding="utf-8")
assert 'inbox.plan.bridge' in access_report
assert 'EnsureDeliveryCapabilityAsync' in client
assert 'IsExactlySixDigits(password)' in client
assert 'AccessReportText' in view_model and 'AccessReport?.CanPlanBridge != false' in view_model
assert 'AccessReportText' in inbox_xaml and 'email OTP is not a password' not in inbox_xaml
assert 'Task<InboxSyncOutcome>? _runningTask' in service
assert 'CancelSynchronization()' in service and 'NeutralStoppedOutcome()' in service
client_check = (ROOT / "Theme/step1.d.tests/step1.d.12.InquiryInboxHttpClientChecks.cs").read_text(encoding="utf-8")
assert 'DisposeProcessHttpClientForTests' in client_check
runtime_checks = (ROOT / "Theme/step1.d.tests/step1.d.1.RuntimeChecks.cs").read_text(encoding="utf-8")
assert 'InquiryInboxHttpClientChecks.Run()' in runtime_checks
assert 'InquiryInboxAuthorizationChecks.Run()' in runtime_checks
assert 'InquiryInboxStorageChecks.Run()' in runtime_checks
assert 'request.Headers.Add("apikey", _settings.AnonKey)' in client
assert 'Storage download API-key error (HTTP 401)' in client
storage_checks = (ROOT / "Theme/step1.d.tests/step1.d.14.InquiryInboxStorageChecks.cs").read_text(encoding="utf-8")
assert 'StorageHadApiKey' in storage_checks and 'StorageHadBearer' in storage_checks
assert 'This account may not run the desktop inbox.' in service
assert 'This account may not read those files.' in service
assert '"Access denied"' in service
permission_start = service.index('catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.PermissionDenied)', service.index('outcome.Delivered++'))
permission_end = service.index('catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.AlreadyDelivered)', permission_start)
assert 'UpsertState' not in service[permission_start:permission_end]
assert 'get_my_inquiry_access_v1' in (ROOT / "Theme/step1.d.tests/step1.d.13.InquiryInboxAuthorizationChecks.cs").read_text(encoding="utf-8")
closed = workspace.split('private void OnOwnerWindowClosed', 1)[1]
assert closed.index('StateChanged -= OnInboxStateChanged') < closed.index('CancelSynchronization()') < closed.index('_inboxService.Dispose()') < closed.index('ShutdownProcessHttpClient()')

# ── Register readability ────────────────────────────────────────────────────
# Owner feedback: the twelve-column register showed values hard against the
# column edges and reading left-aligned. Alignment belongs on the cell — a
# template cell hands its content a content-sized slot, so the Status chip's own
# HorizontalAlignment is ignored — and the themed cell Padding never reaches the
# stock cell template, so each value carries the inset as a margin and trims.
assert 'CellStyle="{StaticResource InquiryCell}"' in list_xaml, "the register cell style is not wired"
assert 'ColumnHeaderStyle="{StaticResource InquiryColumnHeader}"' in list_xaml, "the register header style is not wired"
assert 'Property="HorizontalContentAlignment" Value="Center"' in list_xaml, "cell content is not centred"
assert 'VerticalGridLinesBrush' not in list_xaml, "row rules stay with this view, not a brush override"
assert 'CanUserResizeColumns="True"' in list_xaml, "columns must stay resizable by hand"
assert 'MinColumnWidth="{DynamicResource ControlMinWidth}"' in list_xaml, "a resized column needs a floor"

readability = list_xaml[list_xaml.index('x:Key="InquiryCellText"'):]
readability = readability[:readability.index("</Style>")]
assert 'Value="CharacterEllipsis"' in readability, "long values must trim instead of running into the next column"
assert 'Value="Center"' in readability, "values must be centred"
assert 'Value="{DynamicResource PaddingCompact}"' in readability, "values need the theme's inset, because the cell template drops Padding"

# StaticResource has no forward references: the shared style must be declared
# before the first style that derives from it.
assert list_xaml.index('x:Key="InquiryCellText"') < list_xaml.index('BasedOn="{StaticResource InquiryCellText}"'), \
    "InquiryCellText is used before it is declared"

text_columns = re.findall(r"<DataGridTextColumn\b[\s\S]*?/>", list_xaml)
assert len(text_columns) == 11, f"expected eleven text columns, found {len(text_columns)}"
for column in text_columns:
    assert "ElementStyle=" in column, f"a register column has no reading style: {column[:60]}"

assert 'Tag="{Binding StatusTag}"\n                                                HorizontalAlignment="Center"' in list_xaml, \
    "the status chip must centre in its column"

# ── Register rules and geometry (owner rounds 5 and 6) ────────────────────
# "grid lines only in the column header", "row data normal blank", and the
# register values must read centred. The body rules came from two sources - the
# view's own GridLinesVisibility and the package's per-cell border - so both are
# stated here, and the header draws its own hairline rather than borrowing a
# package's control weight.
register_grid = list_xaml[list_xaml.index("<DataGrid "):]
register_grid = register_grid[:register_grid.index(">")]
assert 'GridLinesVisibility="None"' in register_grid, \
    "the register must state GridLinesVisibility=\"None\" instead of leaving it to the package"
assert register_grid.count('GridLinesVisibility="') == 1, \
    "the register grid must not carry two competing line settings"
assert 'VerticalGridLinesBrush="' not in list_xaml and 'HorizontalGridLinesBrush="' not in list_xaml, \
    "a rule brush override is not wanted: the header carries the rules"
header_rule = list_xaml[list_xaml.index('x:Key="InquiryHeaderRule"'):]
header_rule = header_rule[:header_rule.index("</Style>")]
assert 'Value="{DynamicResource BorderStrong}"' in header_rule, "the header rule must use a visible theme token"
assert 'Value="{DynamicResource StrokeWidth}"' in header_rule, "the rule width must follow the theme, not a literal"
assert list_xaml.count('Style="{StaticResource InquiryHeaderRule}"') == 11, \
    "one rule per column boundary: eleven for twelve columns"
assert list_xaml.count('Style="{StaticResource InquiryHeaderLabel}"') == 12, \
    "every register column must use the centred header label"
register_columns = list_xaml[list_xaml.index("<DataGrid.Columns>"):]
register_columns = register_columns[:register_columns.index("</DataGrid.Columns>")]
assert 'Header="' not in register_columns, \
    "a plain string header cannot be centred or carry a rule"

cell_style = list_xaml[list_xaml.index('x:Key="InquiryCell"'):]
cell_style = cell_style[:cell_style.index("</Style>")]
assert 'BasedOn="{StaticResource {x:Type DataGridCell}}"' in cell_style, "the cell style must derive from the package"
assert 'HorizontalContentAlignment="Stretch"' in cell_style, \
    "the cell presenter must stretch its content, or centred values render left-aligned"
assert 'Property="BorderBrush" Value="{DynamicResource TransparentBrush}"' in cell_style, \
    "the cell must not paint a border of its own"

header_style = list_xaml[list_xaml.index('x:Key="InquiryColumnHeader"'):]
header_style = header_style[:header_style.index("</Style>")]
assert 'Property="BorderThickness" Value="0"' in header_style, \
    "the package's header frame must be switched off, or its control weight boxes the band"
assert 'PART_RightHeaderGripper' in header_style and 'PART_LeftHeaderGripper' in header_style, \
    "a header template without the resize grippers silently disables column resizing"
assert 'CellStyle="{StaticResource InquiryCell}"' in list_xaml
assert 'ColumnHeaderStyle="{StaticResource InquiryColumnHeader}"' in list_xaml

# The actions card: a WrapPanel sized each button to its label and dropped the
# inbox action onto an orphan line, which read as a collapsed stack of chips.
assert "<WrapPanel" not in list_xaml, \
    "the actions card must lay its buttons out on a grid, not wrap them"
assert list_xaml.count('<ColumnDefinition Width="1*" />') >= 3, \
    "the three file actions must share one row of equal thirds"

print("PASS: inquiry theme styles, inbox lifecycle, shared HTTP recovery, schema repair, and auth/authorization diagnostics")
