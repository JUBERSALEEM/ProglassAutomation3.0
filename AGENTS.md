# Permanent agent instructions — ProGlass V3.0

Every coding agent must read this complete file before starting any task, then read applicable project/architecture rules and relevant feature documentation, inspect the existing implementation, and identify the exact authorized scope before editing. No agent may silently expand scope, skip documentation, replace confirmed decisions, or report unverified work as complete. These rules apply to Theme Studio, future themes, shared controls, and all future ERP modules.

## Identity and authorization

- ProGlass V3.0 is a completely new native WPF desktop application built from zero application code. Do not migrate, copy, or depend on V2.0 or any previous application's code, architecture, modules, databases, integrations, or workflows. Do not invent requirements from other ProGlass projects or assume an older application exists.
- Inspect actual repository contents to establish the real structure and designated project root. Preserve approved V3.0 documentation and work. Fresh implementation is not permission to delete or overwrite repository content. If unexpected application code exists, report its exact paths and ask before modifying or deleting it. Do not raise older-application preservation concerns without repository evidence. Never create a parallel application directory to accommodate a presumed previous application. After authorization, create the new WPF application in the designated project root. The [inspection record](docs/05.Decision-Log.md) lists actual unexpected code without assuming its origin.
- **Phase 01 was explicitly authorized on 2026-10-20** using the existing root Visual Studio WPF starter project. Authorized scope is the fresh shared engine, Bento Light/Dark, Bento-owned reusable styles and functional Theme Studio. Root startup/window/project integration is authorized; the subsequently authorized clean-foundation reset removes the inspected legacy-only graph recorded in step1.c.4.Cleanup-Inventory.md. Do not stop to request start approval again. No later theme/feature is authorized; final acceptance still requires honest verification and user review.
- Work on one explicitly approved feature at a time. Do not jump ahead, invent future modules, introduce unapproved databases/integrations/infrastructure, or modify unrelated files.
- The approved business-module order lives in [docs/11.Business-Module-Roadmap.md](docs/11.Business-Module-Roadmap.md) with each module's scope in [docs/12.Module-Specifications.md](docs/12.Module-Specifications.md). **Allocation is not authorization:** a numbered module in the registry still requires the owner's explicit approval for that module, and every transaction module additionally waits for gate `G1`. Read both before proposing any next step.
- Inspect before editing. Preserve user work and secrets. Do not delete user files, reset/rewrite history, or change branches without authorization. Standing user authorization: commit completed authorized changes and immediately push the current session branch; do not leave completed work local-only. See the Git delivery/PR safeguards below.

## Organization and understandable code

- Consult [Project Rules](docs/00.Project-Rules.md), the authoritative naming specification, before creating files. Follow `step1`, `step1.a`, `step1.a.1`, and `step2.a` hierarchy. Check filenames, parent folders, and feature allocations against the [registry](docs/06.Feature-Registry.md); never reuse a feature number.
- Explain necessary tool/framework naming exceptions. Do not rename preserved code merely to apply the new convention.
- Keep source files short and focused on one responsibility. Use descriptive names and explain architectural choices in plain language. Avoid giant files, duplicate implementations, unnecessary abstractions, and unnecessary dependencies.
- The owner is a non-developer: repository structure and feature READMEs must explain each folder and file in understandable terms.
- Follow [architecture principles](docs/01.Architecture-Principles.md) and the [design reference/adaptation policy](docs/02.UI-UX-Design-System.md). External references do not override project rules; Ponytail reuse advice does not authorize copying or depending on previous-application code.

## Theme directory architecture — permanent rules

All theme definitions, palettes, dictionaries, services, validation, effects, and shared theme-aware styles/templates must live under **`Theme/` in the designated project root**, never scattered through unrelated feature folders. The [naming specification](docs/00.Project-Rules.md) owns the exact planned tree and documented exceptions.

- Required stable directories, in roadmap order: `Theme/ThemeBento/`, `Theme/ThemeClaymorphism/`, `Theme/ThemeSpatial/`, `Theme/ThemeSwissInternational/`, `Theme/ThemeHolographic/`, `Theme/ThemeNeumorphism/`, `Theme/ThemeLiquidGlass/`, `Theme/ThemeGlassmorphism/`. These are planned locations, not permission to create empty folders or placeholder resources.
- Bento's required stable filenames are `ThemeBento.Light.xaml`, `ThemeBento.Dark.xaml`, and `ThemeBento.Readme.md` inside `Theme/ThemeBento/`. Stable theme/directory names are intentional naming exceptions; apply step1 hierarchy to other internal implementation files wherever compatible, without a second numbering scheme.
- **Shared infrastructure is non-visual only**, under `Theme/step1.a.ThemeEngine/`: registration/discovery, identity/metadata, lifecycle, mode selection, loading/unloading/replacement, typed contract validation, diagnostics/errors and consumer notifications. Theme-independent accessibility/diagnostic utilities may be shared. No palette, visual defaults, component templates, card/plate system or Bento styling is permitted in this engine.
- **Each implemented theme owns its complete visual engine** under its stable directory: both appearance dictionaries, semantic mappings, all brushes/tokens, component styles/templates, typography, card/plate/surface/edge/depth treatments, interactive states, design documentation and validation expectations. Reuse within that theme is encouraged; do not mix theme implementations into a global visual dictionary, conditional style engine or scattered overrides. Never make future themes inherit Bento as a fallback.
- The common contract describes semantic roles and required WPF style targets, not visual values or a shared implementation. Reusable application controls consume active theme resources. A registered theme supplies a complete resource-package factory; the orchestrator knows no theme-specific composition. Validate the candidate independently of currently active resources, including missing/wrong style targets and duplicate merged keys, before replacing one complete package slot.
- Bento visual files live in `Theme/ThemeBento/step1.e.Components/` and its composition factory is `Theme/ThemeBento/step1.e.7.BentoResources.cs`. These replace the earlier engine-owned SharedStyles location. No visual dictionary remains installed globally outside the active package. Bento alone reuses its own sealed style identities across modes while returning fresh dictionaries, preserving control state without sharing mutable dictionaries or cached brushes across themes. Future themes own their own composition/lifetimes; do not impose this implementation on them.
- Theme Studio view/view model and screen layout stay in `step1.ThemeStudio/step1.b.ThemeStudioScreen/`. Studio consumes the actual active engine resources; it owns no palette, theme dictionary, reusable theme style or duplicate switching service. Governance/feature docs may reference the theme architecture without becoming a second source of theme values; concrete theme specifications belong with their theme.
- This supersedes earlier examples placing engine implementation inside step1.ThemeStudio. Do not move existing code during documentation work. If verified technical constraints require a different location, document the reason and obtain approval **before** deviating.

## Clean-code checks — mandatory

Before adding a file, check whether its responsibility already exists. Before adding an abstraction or dependency, explain its concrete necessity; prefer approved existing capabilities, never unapproved previous-application code. Keep views, view models, services, definitions and reusable styles distinct. Avoid god classes, oversized XAML/code-behind, duplicate colors/styles/templates/switching, and speculative frameworks. Use clear names, consistent formatting and comments limited to intent, constraints or non-obvious decisions. Keep error handling explicit; never silently swallow exceptions. Remove only dead code/unused resources introduced by the current task; do not perform unrelated cleanup or delete existing functionality. Preserve accessibility, contrast, keyboard operation, resizing and separation of presentation from feature behavior.

## Bento research and quality loop — mandatory

Follow **Research → Design specification → Implementation → Build → Runtime inspection → Comparison → Correction → Regression verification → User review** before declaring Bento complete. Research actual Bento examples and design-system documentation, record source URLs and native WPF adaptations; a generic palette alone is not sufficient. Before implementation specify real Light/Dark palettes, card/plate variants, typography, spacing, borders, radii, elevation and interaction states in the theme design specification. Compare actual runtime results to that specification and approved reference patterns; correct shared resources/components rather than patching individual screens. Recheck existing-element/dialog switching, contrast, resizing/scaling, clipping/overlap, resource completeness, build and tests after corrections. Record unavailable build/runtime checks exactly; never claim they passed. The [development workflow](docs/03.Development-and-Testing.md) defines evidence to retain. Claymorphism cannot begin until Bento has been reviewed, corrected as needed and explicitly approved.

## Dynamic theme requirements — permanent policy

**Keep dynamic things dynamic.** [UI/UX Design System](docs/02.UI-UX-Design-System.md) owns the semantic resource contract, component coverage, runtime-switching rules, exceptions, and future-theme process. Read it before any UI task; do not duplicate that contract in individual features.

- Bento Grid / Modular is primary; Claymorphism is second. The [roadmap](docs/04.Project-Roadmap.md) owns the approved eight-theme order. All themes eventually require Light and Dark, but Phase 01 is limited to the non-visual shared engine, complete Bento Light/Dark visual implementation, and Theme Studio. No other theme implementation or fake/nonfunctional theme options.
- Centralize theme-owned colors, brushes, spacing, shape, typography, states, depth, and effects in the active theme’s semantic replaceable resources and its reusable component styles. No screen-local palettes, duplicate button/input styles, scattered theme conditionals, or hardcoded theme styling. Every permitted data-specific/exceptional styling exception must be narrow and documented.
- Runtime appearance changes must update the entire existing visual tree, shared controls, and open dialogs without restart or per-control recoloring. Theme Studio must use the real application system, not a disconnected preview palette. Verify WPF lookup/replacement behavior; do not assume every resource reference updates automatically.
- Future themes must satisfy the same typed contract in both appearances without rewriting screens, duplicating views, replacing shared controls, or touching unrelated logic. Detect missing required resources in development/tests. Do not create empty future-theme dictionaries.
- Keep layouts resilient to resizing, DPI, longer text, and font differences; theme-owned dimensions use shared resources without rigidly fixing every layout. Simplicity never excuses incomplete theming.

## Strict native WPF compatibility — authoritative rules

All application C#, XAML, styles, templates, resources, controls, and behavior must match the **actual configured .NET and WPF versions**. Verify APIs before generating code, not merely by appearance in an editor.

1. Use valid native WPF controls, dependency properties, resource dictionaries, styles, control templates, namespaces, markup extensions, and supported APIs. Never invent properties, namespaces, attached properties, controls, event handlers, or third-party APIs. Do not add dependencies to disguise unsupported syntax.
2. Do not copy React, HTML, CSS, Flutter, MAUI, WinUI, or other framework implementations into WPF. Translate concepts into supported WPF techniques. Availability in another XAML framework is not evidence of WPF support.
3. Respect content models. `Border`, `Button`, and `ContentControl` have a single child/content slot; multiple elements need a suitable `Grid`, `StackPanel`, `DockPanel`, or other verified panel inside that slot. Do not confuse content with a panel's `Children` collection. Never set a content property twice; prevent “Child is set more than once.”
4. WPF `TextBlock` does not provide a native `LetterSpacing` property. Do not use CSS `letter-spacing`. Prefer verified font family, size, weight, line height, and layout; use a verified WPF technique only if letter spacing is genuinely necessary, not complex custom rendering for decoration.
5. Verify each property's owning control, type, enum values, namespace, attached-property syntax, and target-framework support. In particular, `CornerRadius`, `Margin`, and `Padding` are not interchangeable across all controls. CSS `box-shadow`, `backdrop-filter`, `gap`, `display`, `position`, and `border-radius` are not native WPF properties. Use supported layouts, brushes, effects, templates, and properties instead.
6. Prevent duplicate resource keys within a resource scope, duplicate `x:Name` declarations within a namescope, incompatible values, undefined `StaticResource`/`DynamicResource` references, and invalid setters. Review resource lookup and dictionary load order.
7. Verify every binding against a real property in the relevant `DataContext`. Commands, converters, and event handlers must exist with compatible signatures. Check property-change notifications, namespaces, `x:Class`, code-behind, project references, project inclusion, build actions, generated files, and binding syntax. Prevent clipping, unwanted overlap, and broken resizing. Never create fake bindings.
8. Styles must target valid WPF types. Templates must preserve expected behavior and required named parts where applicable. Claymorphism must use supported WPF surfaces/shadows, not browser blur, backdrop filters, or CSS shadows. Avoid rendering lag, unreadable surfaces, or needless effects.
9. Priority when trade-offs arise: **compilable native WPF → correct functionality and reliable layout → maintainable understandable code → approved visual fidelity → optional decoration**. Choose the simplest supported implementation; never sacrifice build reliability for effects.

## Windows CI verification — permanent requirement

Do not attempt to install .NET SDKs, Visual Studio, Windows runtimes or other large build tools in this environment. Use the repository's Windows GitHub Actions workflow, `cols/ci/windows-wpf.yml`, to check out, set up the actual required SDK with official actions/setup-dotnet, restore/build the real projects and run compatible automated/portable checks. Retain the configured target framework; use project commands if the SDK does not support the existing solution format. Preserve native exit codes and failure status; never hide failures with continue-on-error, command chains or summaries.

Run compiled contracts separately from any STA WPF runtime smoke. A runtime job must fail on errors/timeouts and describe its desktop/software-rendering limitations. CI compilation is not runtime verification, and runtime assertions are not human visual inspection. Only actual rendered native WPF output may support visual claims. Never substitute XAML parsing, source screenshots, mockups or AI-generated images.

Commit and immediately push completed authorized work under the standing Git delivery rule. Releases, public executable/artifact uploads and release-process changes still need separate authorization. A new local workflow cannot verify unpushed code: report that precisely, along with any GitHub Actions permission/access failure. Never claim a workflow ran without a run/result. Keep user work and credentials safe; do not repeat failed SDK installation attempts.

## Verification and reporting

For every WPF implementation task:
1. Inspect the real project, SDK configuration, and target framework; verify relevant WPF APIs/properties before code generation.
2. Review content models, unsupported CSS/cross-framework syntax, resource keys, namescopes, bindings, namespaces, control types, templates, events, and class references.
3. Build the actual project with the configured SDK and correct framework; fix introduced compilation errors and run applicable tests.
4. Verify UI behavior and inspect the actual rendered WPF screen when possible. Never substitute fabricated screenshots or a browser mock-up for WPF verification.
5. Distinguish existing failures from introduced failures. If build, tests, or visual inspection cannot run, state exactly what remains unverified. Never claim success without execution evidence.

Follow the [development workflow](docs/03.Development-and-Testing.md). Each implementation completion report must list: (1) files created/modified, (2) purpose of each, (3) functionality implemented, (4) build/test results, (5) visual verification where applicable, (6) remaining issues, (7) deviations, and (8) the next decision requiring approval. For theme work also report architecture, token/shared-component coverage, actual Light/Dark switching results, resource completeness, hardcoding exceptions, and evidence that the future-theme process does not require screen rewrites. Do not assert complete coverage without checking it. Confirm no unauthorized scope was added. Stop at the approved phase boundary; Phase 00 approval and explicit Phase 01 authorization are required before implementation, and another approval is required after Phase 01.


## Canonical reference-page standard

The user explicitly authorized removal of the inspected pre-Phase-00 SheetStore/dashboard graph on 2026-10-20. The recorded file-level inventory is step1.c.4.Cleanup-Inventory.md. Do not restore that implementation, its converters, shell styles, database/DI packages or empty legacy view model. Preserve the standard root WPF files and approved V3 documentation/theme work. This is not authorization for other cleanup or later themes. Git delivery follows the standing rule below.

The Business Reference page is now the canonical V3.0 sample implementation; Theme Studio is a separate customization workspace inside the same executable, not an ERP module. Future pages must reuse its clear page title/subtitle/status and real selectors; full available viewport width, page/card spacing and responsive auto/star layout; owning-theme Card/Plate/AccentPlate/ElevatedPlate roles; semantic typography/colors; primary/secondary/subtle/destructive actions; input/validation, readable structured-list, status and feedback patterns. Reserve destructive appearance for destructive actions (the reference clears local preview text only). Keep all important actions visible and keyboard reachable.

Consume DynamicResource for appearance-sensitive styling, never copy a palette or introduce screen-local theme styles. Studio uses a compact settings sidebar and live previews, stacking below 1040 content DIPs; Business Reference uses intentional wide/narrow modules, stacking below 1100; other pages may adapt layouts to their workflow without inventing another design system. Verify hover/pressed/disabled/focus/validation, selected rows, empty state, both modes, resize and DPI/accessibility before acceptance. Update the reference when an approved component rule changes. No static mockup is native WPF evidence. See step1.c.8 for the latest implementation and verification gaps.

If `windows-wpf.yml` is in `.github/workflows/`, leave it there. Do not move it back to `cols/ci`. The agent GitHub App cannot publish workflow files (missing `workflows` permission); the owner placing the file under `.github/workflows/` is the activation path. Foundation checks must accept either location. Do not create a pull request without two separate confirmations.

Latest user instruction: track GitHub Action builds and fix failures. If the owner puts `windows-wpf.yml` in `.github/workflows/`, do not move it back to `cols/ci`. The agent App cannot publish workflow files. No pull request without two separate explicit confirmations.

## Standing Git delivery and double-confirmation PR safeguards

Latest explicit user instruction: after completing and validating authorized changes, commit on the current Arena session branch and immediately push to that same branch. Do not intentionally leave completed changes or commits local-only. Git commits necessarily happen locally before push; if push is rejected, preserve the commit, report the exact blocker immediately and request the needed permission rather than claiming success, discarding work or rewriting published history. Never push to another branch or merge into master without explicit authorization. Keep unrelated user changes out of commits. This standing delivery authorization supersedes older task-specific no-commit/no-push wording in historical reports; it does not authorize extra product scope, releases or deployments.

Before creating ANY pull request, obtain TWO separate explicit confirmations for that specific PR (source branch, target and scope). Ask once; after an affirmative answer, ask a second time and wait for another affirmative answer. Only then may the agent run gh pr create. A clicked Create PR button, ambiguous message, earlier push permission, or two questions bundled in one request does NOT satisfy both confirmations. Never automatically create a PR after pushing. This governs agent actions; it cannot disable or intercept a PR created directly by the user/platform outside the agent.

## Separate customization workspace and business reference — current standard

The latest approved revision separates two native WPF pages in the existing root window: Theme Studio edits the actual active design; Business Reference demonstrates a realistic local business sample without theme controls/token inspectors. Both share one ThemeService and view model; do not create another executable, duplicate theme engine, business database or legacy module. Studio offers real curated accent, density, shape and typography controls, Light/Dark, live token inspection and reset. Never label inactive/fake settings functional. Preferences are session-only unless persistence is separately approved.

All concrete customization behavior/palettes belong to ThemeBento/step1.e.8.BentoCustomization.cs. The shared service may retain a nonvisual per-active-theme configuration callback, validate after customization and distinguish consumer notification failures from activation rejection. It must not encode Bento color/shape rules or carry a Bento callback into another theme. UI callbacks capture immutable preferences, not view-model/control lifetimes. Bento caches sealed styles and frozen private Geometry helpers only; mutable brushes/candidate dictionaries are fresh. Native controls and pages consume semantic resources. New hero/metric/status/navigation visuals belong to Bento's Workspace dictionary. Do not copy CSS, WinUI ThemeResource syntax or external component packages into WPF.

Keep Studio compact and full-width; do not turn every setting into a large marketing card. Default desktop controls are 32 DIPs high, body 13 and caption 12; comfortable density and larger type are real user choices. Page composition is the BentoGrid panel (unequal spans, equal-height rows, packed gutter). This supersedes stacked Auto-height card documents, equal-lane masonry and UniformGrid KPI strips. No claim of rendered polish, accessibility or successful Windows execution is valid before actual native testing.

## Current approved analytics ownership

The master reusable chart/analytics expansion remains step1/Bento only. `Theme/step1.f.Analytics/` owns typed immutable chart data/validation, native library adapter/host and KPI control (no palette, business data/calculations or theme identity logic). `Theme/ThemeBento/step1.e.Components/step1.e.10.Analytics.xaml` and both appearance dictionaries own all chart/KPI/table/plate visuals. LiveChartsCore.SkiaSharpView.WPF 2.0.4 is the one reviewed pinned chart dependency; no further library is approved. Studio b.19–21 own deterministic demonstration data/gallery/overview. Keep Business Reference selected at startup and Studio separate. Diagnostics are optional/collapsed, internal candidate validation remains mandatory. Current contract 102 roles / 47 style targets including BentoRowHeight/BentoGutter/BentoCompactWidth and BentoTile; Bento is default and Claymorphism is the second registered package (`Theme/ThemeClaymorphism/`, report c.12). Current reports c.9, c.11, c.12 and reuse guide f.6 record unverified Windows restore/build/render checks. Native test assertions are not evidence until executed. Workflow cannot run from `cols/ci`. No further theme/module. No PR without the standing two separate confirmations.

## WPF layout, spacing and accessibility rules (permanent, decided 2026-10-27)

These rules are mandatory for every WPF view in this repository — existing and future, every module, every theme. They exist because the owner had to report the same three defects (buttons touching each other, controls overlapping, cramped and misaligned forms) one round at a time. A rule that only lives in a chat message is not a rule; a rule that is not checked is not enforced. So each rule below names how it is enforced as well as what it means.

### A. No overlapping controls

* Every control occupies its own region of a layout panel. Nothing may be drawn on top of a sibling.
* `Canvas`, `Canvas.Left/Top`, absolute positioning, `RenderTransform` offsets and negative margins are not permitted for layout. Use `Grid` (with explicit row/column definitions), `DockPanel`, or a `StackPanel`/`WrapPanel` whose orientation matches the flow.
* A horizontal `StackPanel` inside a `*` column is a known overflow source: it never shrinks below its content. Use a `WrapPanel` where wrapping is acceptable, or divide the content into rows/columns that cannot exceed their cell.
* Controls that float over the shell (overlay buttons, `Panel.ZIndex` chrome) are one of the two causes of the sidebar/content collision. Shell actions belong in the region that owns them.
* A XAML file must also **load**. `Style.BasedOn` and `Binding.Converter` are plain CLR properties, not dependency properties: `BasedOn="{DynamicResource ...}"` and `Converter={DynamicResource ...}` throw `XamlParseException` inside `InitializeComponent()`, so an app that compiles cleanly still fails to open. Use `StaticResource` for both, or move the token onto the element (`Style="{DynamicResource Caption}"`) and drive visibility with one `BooleanToVisibilityConverter`. A mistyped `StaticResource` key fails at parse time for the same reason — a missing token is fixed by defining it, never by switching to `DynamicResource`, which fails silently and leaves the property at its default.
* Enforced by: `Theme/step1.d.tests/step1.d.17.XamlPropertyChecks.py` (attribute review) and the layout checks in each module's source checks (for example `step3.f.2`).

### B. Consistent spacing through shared tokens

* Related controls **8**, separate groups **16**, form sections **20–24**, main content padding **24**.
* Button internal horizontal padding **14–18**; gap between adjacent action buttons **8–12**; minimum interactive control height **36**.
* These are design tokens, not numbers to retype. Use the contract tokens `UiGapField`, `UiGapGroup`, `UiGapSection`, `UiGapAction`, `UiGapCluster`, `UiPagePadding`, `UiCardPadding`, `UiActionMinHeight`, `UiActionPadding` (declared in `Theme/step1.a.ThemeEngine/step1.a.1.ThemeContract.cs` and defined in all eighteen theme dictionaries).
* A spacing or size literal typed into an element is a defect even when it happens to look right.
* Cards and plates on the same page share **one** inner inset. `Card`
  (`PaddingCard`) and `Plate` (`PaddingPlate`) are themed separately and do not
  agree — measured 8–24 px across the nine packages — so a page that mixes them
  pins `UiCardPadding` on every one of its cards and pins nothing else; the cards
  then line up with the same left and right edges in all nine themes. This is the
  only permitted override of a themed card's padding.
 Theme-level metrics belong in a theme dictionary; layout gaps belong to the shared tokens above.
* Enforced by: `step1.d.3.verify_sources.py` (every `{DynamicResource …}` in a view must be a contract token) and `step1.d.5`/`step1.d.6` (nine packages, both modes, identical token sets).

### C. Responsive layout

* Layouts must reflow, not break: `Auto` for content-sized regions, `*` for the region that absorbs remaining space, `WrapPanel` for rows whose children may not fit.
* No fixed sizes that stop a layout from adapting. A fixed width is acceptable only for a control whose content has a known bound (an icon, a badge, a filter dropdown), and even then the surrounding layout must wrap or scroll.
* Long forms and long lists scroll **inside their own region**. A scroll region must never be replaced by a taller card.
* Every view must remain usable at the supported minimum window size (`WindowMinWidth`/`WindowMinHeight` in the theme contract) with the navigation sidebar both expanded and collapsed.

### D. Buttons and actions

* Adjacent action buttons are separated by 8–12 px — always. Two buttons that share an edge read as one malformed control.
* Action groups (selection actions, status actions, create actions) are separated by 16 px so the groups are distinguishable.
* The primary action must be immediately identifiable and is the only primary-styled button in a region; destructive actions use the destructive style and never sit flush against the primary one.
* Every action button has a minimum interactive height of 36 px and internal horizontal padding of 14–18 px.
* Button order inside a cluster is: primary → secondary → cancel/dismiss.
* Enforced by: the spacing tokens above plus the module source checks that assert token usage and button order.

### E. Text and content integrity

* No clipped or truncated text without a deliberate `TextTrimming` and a tooltip that reveals the full value.
* No control may escape its container; no row may draw outside its card.
* Empty states are actionable ("no users match this filter — clear the filters"), error states say what failed and what to do, and both are inside the region they describe.
* Nothing critical is hidden behind hover, and no action is reachable only by right-click.
* Every user-visible string comes from the view model or the catalogue, never from a hardcoded literal that could disagree with the stored data.
* A dropdown, filter or input must say what it is for — a visible caption or a
  placeholder — and its closed state must never be blank. A selection that cannot
  be resolved (bound before its list was loaded, say) is a defect, not a cosmetic
  detail: the operator cannot tell what the control does.
* A filter carries a visible **label** ("Department", "Role") and its face carries
  its **selection, and only that**. Four arrangements have been tried: the field name
  printed on the button — "Department: All" (round 20, rejected), the field name
  prefixed onto every row (round 23, rejected the same round — "remove department and
  roles from each dropdowns"), a bare value with no label at all (rejected: the owner
  asked for the word on the control, and it cannot be resolved), and the settled one —
  a label beside the box, the selection on the face, the way the Inquiry toolbar
  labels its status chips. The unfiltered row names what it covers ("All departments",
  "All roles"), a chosen row is the catalogue's own wording verbatim, and
  `AutomationProperties` + the tooltip say what the control does in every state.

### F. Sidebar and navigation integrity

* The navigation sidebar and the content region occupy **separate layout regions** of the shell. Collapsing or expanding the sidebar must never overlap, clip or shift content into a control.
* Content reflows when the sidebar toggles; the content region must not paint over the sidebar column.
* Preserve navigation order, selected-page behaviour and the active theme. A layout fix must not renumber workspaces or change which page opens at startup without an explicit decision.
* Scrolling belongs to the content region, not to the shell.
* A state that blocks the operator (no users, no administrator yet, unreadable
  data) must present the way out of that state on the same screen. Telling someone
  to "ask an administrator" in a directory that has no administrator is the defect
  this rule exists to prevent.

### G. Dialogs, overlays and menus

* A dialog or overlay is sized by its content (with a maximum), centred on the region it blocks, and never so small that its own fields or buttons are clipped.
* An overlay must obscure only what it is blocking; popups and context menus must not cover the control that opened them.
* Keyboard: `Esc` dismisses, focus starts in the first field, focus is trapped inside the overlay while it is open, and focus returns to the invoking control when it closes.

### H. Accessibility

* Readable type at the shipped sizes; contrast is verified by the theme contract (`step1.d.3` measures 1514 contrast pairs).
* Every interactive control is reachable by keyboard, shows a visible focus indicator, and has an accessible name (`AutomationProperties.Name` when the visible text is not sufficient).
* Order in the visual tree is the tab order; nothing important is mouse-only.

### I. Theme compatibility

* Every colour, brush, metric, radius and font comes from the theme contract through `DynamicResource`. No hardcoded colour, no screen-local palette, no `StaticResource` of a theme-owned card role.
* Controls with a theme-owned template bind the implicit style explicitly (`Style="{DynamicResource {x:Type ComboBox}}"`), so a theme switch replaces the template as well as the tokens.
* A layout change must be checked in **all supported themes**, at minimum Light and Dark, because a fix that fits in one density may clip in another.

### J. Required verification before declaring a WPF module complete

Compilation is not layout verification, and reading XAML is not visual verification. Before reporting a module as complete, and before claiming any UI work is finished:

1. Build the project where the tooling exists; fix every error you introduced.
2. Inspect every changed XAML file **and** the shared layout it inherits (shell, sidebar, page padding, card roles).
3. Run the automated source checks: `step1.d.3`, `step1.d.4`, `step1.d.5`, `step1.d.6`, `step1.d.11`, `step1.d.15`, `step1.d.17`, plus the module's own checks (`step2.f.2`, `step3.f.2`). Extend a checker when a new defect class is found — a defect fixed without a check is a defect that returns.
4. Verify at the normal size **and** at a smaller supported size, with the sidebar **expanded and collapsed**.
5. Check for overlap, clipping, spacing, alignment, control heights, scrolling and empty/error states in each of those states.
6. Verify Light and Dark wherever a runtime exists.
7. **Report explicitly which visual checks could not be performed**, and why. Never present a source-only inspection, a mock-up or a rendered approximation as evidence of native WPF rendering.
