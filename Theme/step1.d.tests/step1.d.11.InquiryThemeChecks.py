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
# The register page is a register (owner direction: "apply same in inquiry page").
# The hero card, the three KPI cards and the actions card are gone; the pipeline
# value and the counts survive as one caption line, and the page opens on rows.
assert 'Style="{DynamicResource HeroCard}"' not in list_xaml, \
    "the register page must not spend a hero card on its own title"
assert 'Style="{DynamicResource MetricCard}"' not in list_xaml, \
    "the KPI counts are a caption line, not three cards"
assert list_xaml.count('Style="{DynamicResource Plate}"') >= 1, \
    "the command bar is a Plate strip"
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
            # A view may also pin the shared card inset. `Card` and `Plate` ship
            # different paddings (PaddingCard vs PaddingPlate), so two cards on the
            # same page could inset their contents differently and stop lining up;
            # pinning UiCardPadding — one number, defined in every theme dictionary —
            # makes sibling cards agree in all nine packages. Anything else is still
            # a magic number and still banned.
            assert padding is None or padding in (
                "{TemplateBinding Padding}",
                "{DynamicResource UiCardPadding}",
            ), (view, element.attrib)
    text = view.read_text(encoding="utf-8")
    assert 'BasedOn="{StaticResource Card}"' not in text, view
    assert "CompactCard" not in text, view

# Controls that have theme-owned templates must explicitly track replacement
# of the active theme resource dictionary.
for name, xaml in (("Inquiry register", list_xaml), ("Website inbox", inbox_xaml)):
    for control in ("ComboBox", "TextBox", "CheckBox"):
        assert f'Style="{{DynamicResource {{x:Type {control}}}}}"' in xaml, (name, control)
    # A register names the themed ListGrid key. It is declared by the active
    # package like the implicit style is, so it follows a theme switch too, and
    # it is what carries the shared list pattern (round 50).
    assert ('Style="{DynamicResource {x:Type DataGrid}}"' in xaml
            or 'Style="{StaticResource ListGrid}"' in xaml), name

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
# Round 50 replaced the inbox's own centred header template and cell styles with
# the shared register pattern, so the guarantee is now asserted where it lives:
# the page must use the pattern, and the pattern must centre and trim.
assert 'Style="{StaticResource ListGrid}"' in inbox_xaml
assert 'HorizontalAlignment="Center"' in inbox_xaml
pattern = (ROOT / "Theme/ThemeBento/step1.e.Components/step1.e.10.Analytics.xaml").read_text(encoding="utf-8")
assert 'TextAlignment" Value="Center"' in pattern
assert 'TextTrimming" Value="CharacterEllipsis"' in pattern
# No column carries a hand-typed width: an equal share per column is the rule, so
# a narrow screen cannot clip one column to an ellipsis while another sprawls.
# Grid ColumnDefinitions above the grid are layout, not columns, so only the
# DataGrid's own column block is read here.
columns = inbox_xaml[inbox_xaml.index("<DataGrid.Columns>"):]
columns = columns[:columns.index("</DataGrid.Columns>")]
assert 'Width="' not in columns, "a website-delivery column sets its own width"

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
#
# Round 50 moved all of that from this view into the shared register pattern, so
# the assertions moved with it: the page must use the pattern, and the pattern
# must carry every property the page used to carry for itself.
assert 'Style="{StaticResource ListGrid}"' in list_xaml, "the register style is not wired"
assert 'CellStyle=' not in list_xaml, "the page overrides the shared cell style again"
assert 'ColumnHeaderStyle=' not in list_xaml, "the page overrides the shared header style again"
assert 'VerticalGridLinesBrush' not in list_xaml, "row rules stay with the pattern, not a brush override"

pattern = (ROOT / "Theme/ThemeBento/step1.e.Components/step1.e.10.Analytics.xaml").read_text(encoding="utf-8")
grid = pattern[pattern.index('x:Key="ListGrid"'):]
grid = grid[:grid.index("</Style>")]
assert 'Property="ColumnWidth" Value="*"' in grid, "columns must share the width equally"
assert 'Property="MinColumnWidth" Value="{DynamicResource ControlMinWidth}"' in grid, "a resized column needs a floor"
assert 'Property="GridLinesVisibility" Value="None"' in grid, "the body must carry no grid lines"
assert 'Property="CanUserResizeRows" Value="False"' in grid, "row height is the theme's, not the user's"

readability = pattern[pattern.index('x:Key="ListText"'):]
readability = readability[:readability.index("</Style>")]
assert 'Value="CharacterEllipsis"' in readability, "long values must trim instead of running into the next column"
assert 'Value="Center"' in readability, "values must be centred"
assert 'Value="{Binding Text, RelativeSource={RelativeSource Self}}"' in readability, \
    "a trimmed value must keep its full text on hover — the value binds its own text to ToolTip"

text_columns = re.findall(r"<DataGridTextColumn\b[\s\S]*?/>", list_xaml)
assert len(text_columns) == 11, f"expected eleven text columns, found {len(text_columns)}"
for column in text_columns:
    assert "ElementStyle=" in column, f"a register column has no reading style: {column[:60]}"

assert 'Tag="{Binding StatusTag}"\n                                                HorizontalAlignment="Center"' in list_xaml, \
    "the status chip must centre in its column"

# ── Register rules and geometry (owner rounds 5 and 6, round 50) ───────────
# "grid lines only in the column header", "row data normal blank", and the
# register values must read centred. The rules used to be drawn by this view;
# round 50 put them in the shared register pattern, so they are asserted there
# and the page is asserted not to fight them.
register_grid = list_xaml[list_xaml.index("<DataGrid "):]
register_grid = register_grid[:register_grid.index(">")]
assert 'GridLinesVisibility=' not in register_grid, \
    "the register sets its own line visibility again; the shared style owns that rule"
assert 'VerticalGridLinesBrush="' not in list_xaml and 'HorizontalGridLinesBrush="' not in list_xaml, \
    "a rule brush override is not wanted: the header carries the rules"

pattern = (ROOT / "Theme/ThemeBento/step1.e.Components/step1.e.10.Analytics.xaml").read_text(encoding="utf-8")


def pattern_style(key):
    block = pattern[pattern.index(f'x:Key="{key}"'):]
    return block[:block.index("</Style>")]


grid_style = pattern_style("ListGrid")
assert 'Property="GridLinesVisibility" Value="None"' in grid_style, \
    "the shared register must state no body grid lines instead of leaving it to the package"
assert 'Property="ColumnWidth" Value="*"' in grid_style, "the shared register must divide its width equally"

header_rule = pattern_style("ListColumnHeader")
assert 'Property="FontWeight" Value="Bold"' in header_rule, \
    "the heading must be bold type; the heading token resolves to SemiBold"
assert 'Property="HorizontalContentAlignment" Value="Center"' in header_rule, \
    "the heading must be centred"
assert 'Property="Template"' not in header_rule, \
    ("the header must use the framework template, which lays the label out with "
     "HorizontalContentAlignment; a re-created presenter has rendered the heading flush left twice")
assert 'Property="BorderBrush" Value="{DynamicResource BorderStrong}"' in header_rule and \
    'Property="BorderThickness" Value="0,0,1,1"' in header_rule, \
    "the band must draw one hairline rule on its trailing and bottom edges"

cell_style = pattern_style("ListCell")
assert 'BasedOn="{StaticResource {x:Type DataGridCell}}"' in cell_style, "the cell style must derive from the package"
assert '<ContentPresenter HorizontalAlignment="Stretch"' in cell_style, \
    ("the cell presenter must take the whole column; a centred presenter shrinks to the text, so "
     "the stretched TextBlock inside it cannot be wider than its glyphs and TextAlignment=Center "
     "renders at the left edge")
assert 'Property="VerticalContentAlignment" Value="Center"' in cell_style, \
    "a cell's VerticalContentAlignment defaults to Top, which lifts every value to the top of the row"
assert 'HorizontalContentAlignment="' not in cell_style.split("<ControlTemplate")[1], \
    "HorizontalContentAlignment is not a ContentPresenter member (MC3072); it belongs on the cell style"
assert 'Property="BorderBrush" Value="{DynamicResource TransparentBrush}"' in cell_style, \
    "the cell must not paint a border of its own"

# A plain string header is the pattern now: ListColumnHeader centres and rules it.
register_columns = list_xaml[list_xaml.index("<DataGrid.Columns>"):]
register_columns = register_columns[:register_columns.index("</DataGrid.Columns>")]
assert '<DataGridTextColumn.Header>' not in register_columns, \
    "a column carries a hand-written header template; Header=\"…\" is the pattern"

# The actions card: a WrapPanel sized each button to its label and dropped the
# inbox action onto an orphan line, which read as a collapsed stack of chips.
assert "<WrapPanel" not in list_xaml, \
    "the actions card must lay its buttons out on a grid, not wrap them"

# ── Command bar: dropdowns, one primary action, collapsible filters ─────────
# Owner direction: "all received prepared sent confirmed etc move to dropdown
# single section. same for proforma invoice job order etc". Seven status chips
# and six module chips used to occupy two full-width strips; both are dropdowns
# now, and the advanced filters collapse behind one button.
assert 'ItemsSource="{Binding StatusTabs}"' in list_xaml, \
    "the status filter is a dropdown bound to StatusTabs"
assert 'SelectedItem="{Binding SelectedStatusTab, Mode=TwoWay}"' in list_xaml, \
    "the status dropdown must be two-way bound, or choosing a status does nothing"
assert 'ItemsSource="{Binding ModuleTabs}"' in list_xaml, \
    "the module filter is a dropdown bound to ModuleTabs"
assert 'SelectedItem="{Binding SelectedModuleTab, Mode=TwoWay}"' in list_xaml, \
    "the module dropdown must be two-way bound"
# The closed selection box is a ContentPresenter over SelectionBoxItem, templated
# by SelectionBoxItemTemplate. DisplayMemberPath never fills that template, so a
# dropdown of objects printed its type name in the bar. ItemTemplate is the fix.
assert 'DisplayMemberPath="LabelWithCount"' not in list_xaml, \
    "DisplayMemberPath leaves the closed box printing the type name"
assert '<TextBlock Text="{Binding LabelWithCount}"' in list_xaml, \
    "the status dropdown template must show the label and its count"

assert list_xaml.count('<UniformGrid Columns="7"') == 0, \
    "the seven-way status chip strip is gone"
assert list_xaml.count('<UniformGrid Columns="6"') == 0, \
    "the six-way module chip strip is gone"
assert 'Style="{DynamicResource {x:Type ComboBox}}"' in list_xaml, \
    "dropdowns must track the active theme's ComboBox template"

command_bar = list_xaml[list_xaml.index("<!-- ── One command bar"):list_xaml.index("<!-- ── Advanced filters")]
assert command_bar.count('Style="{DynamicResource PrimaryButton}"') == 1, \
    "exactly one primary action in the bar - the empty state keeps its own call to action"

# Bar rule: the two dropdowns sit inline with their labels. A caption stacked
# above a control makes that column taller than the rest and the bar stops
# reading as one line of actions.
for column in ('0', '1'):
    assert f'StackPanel Grid.Column="{column}" Orientation="Horizontal"' in command_bar, \
        f"the column-{column} dropdown must keep its label beside it, not above it"
assert 'Command="{Binding NewInquiryCommand}"' in list_xaml, \
    "the primary action is New inquiry and it stays visible"

assert 'Binding AreFiltersExpanded' in list_xaml, \
    "the advanced filter row is collapsed by default and driven by AreFiltersExpanded"
assert 'Command="{Binding ToggleFiltersCommand}"' in list_xaml, \
    "the Filters button must toggle that state"
assert 'Text="{Binding FiltersToggleLabel}"' in list_xaml, \
    "the toggle names its state (Filters / Hide filters)"
for binding in ('SelectedDateRange', 'SelectedSalesman', 'SelectedPreparedBy',
                'SelectedPriority', 'SelectedSort', 'HasAttachmentsOnly'):
    assert f'{{Binding {binding}}}' in list_xaml, f"advanced filter {binding} was lost in the move"
assert 'Command="{Binding ResetFiltersCommand}"' in list_xaml, "Reset filters must stay reachable"

# Files and refresh moved into one overflow menu rather than a card of buttons.
for command in ('ImportCommand', 'ExportCommand', 'OpenInboxCommand', 'RefreshCommand'):
    assert f'Command="{{Binding {command}}}"' in list_xaml, f"{command} must stay reachable from the overflow menu"
assert list_xaml.count('Text="{Binding PageStatus}"') == 1, \
    "PageStatus is printed once, in the footer next to the pager"
assert 'OnMoreActionsClick' in list_xaml, "the overflow menu needs a handler, or it only opens on right-click"
code_behind = (ROOT / "Inquiry/Views/InquiryListView.xaml.cs").read_text(encoding="utf-8")
assert 'private void OnMoreActionsClick(' in code_behind, "OnMoreActionsClick is not implemented"

# ── XAML contract: a dropdown must be able to render its own selection ────
# DisplayMemberPath is fine on a ListBox, which never shows a selection box.
# On a ComboBox it leaves SelectionBoxItemTemplate empty and the closed control
# prints the item's type name - which is exactly what the status filter did.
for xaml_path in sorted(ROOT.rglob("*.xaml")):
    if {"bin", "obj", "tests"} & set(xaml_path.parts):
        continue
    for element in ET.parse(xaml_path).getroot().iter():
        if element.tag.endswith("}ComboBox") and "DisplayMemberPath" in element.attrib:
            raise AssertionError(
                f"{xaml_path.relative_to(ROOT)}: ComboBox uses DisplayMemberPath, "
                "which leaves the closed box showing the type name - use ItemTemplate")

# ── Compile safety: the WPF namespaces the code assumes ────────────────────
# Nothing here can build WPF, so the owner's MSBuild is the only compiler. That
# is how `Dispatcher.CurrentDispatcher` reached him as CS0103: Dispatcher lives
# in System.Windows.Threading, and `using System.Windows;` does not import it.
# A bare `Dispatcher.` or `Application.` therefore has to be backed by its own
# using - naming the class in full is equally fine, and is how the inbox
# view model already does it.
SKIP = {"tests", "bin", "obj", ".git"}
for source_path in sorted(ROOT.rglob("*.cs")):
    if SKIP & set(source_path.parts):
        continue
    source = source_path.read_text(encoding="utf-8", errors="ignore")
    name = source_path.relative_to(ROOT)
    if re.search(r"(?<![\w.])Dispatcher\s*[.(]", source):
        assert ("using System.Windows.Threading;" in source
                or "System.Windows.Threading.Dispatcher" in source), \
            f"{name} calls Dispatcher without System.Windows.Threading (CS0103)"
    if re.search(r"(?<![\w.])Application\s*\.", source):
        assert ("using System.Windows;" in source
                or "System.Windows.Application" in source), \
            f"{name} calls Application without System.Windows (CS0103)"

print("PASS: inquiry theme styles, inbox lifecycle, shared HTTP recovery, schema repair, and auth/authorization diagnostics")
