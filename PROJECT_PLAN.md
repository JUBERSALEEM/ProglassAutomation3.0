# PROJECT_PLAN.md — ProGlass V3.0 Theme Studio Master Plan

> **SUPERSEDED (2026-10-27) — history only.** This file describes the theme-studio
> phases P1–P5 planned on 2026-10-20 (branch `arena/bd154b4b-proglassautomation`,
> SHA `5b8207d8a853`) and is retained unchanged below so the record of that period
> survives. It is **not** the current plan. For the present state use:
> [the business-module roadmap](docs/11.Business-Module-Roadmap.md) (approved
> implementation order and the `G1` Foundation Review gate),
> [module specifications](docs/12.Module-Specifications.md),
> [the roadmap](docs/04.Project-Roadmap.md) (phase gates),
> [the feature registry](docs/06.Feature-Registry.md) (allocations) and
> [the decision log](docs/05.Decision-Log.md) (dated decisions, authoritative).
> `PROJECT_HISTORY.md` carries the owner-readable summary of what shipped.

**Created:** 2026-10-20 | **Branch:** `arena/bd154b4b-proglassautomation` | **Current SHA:** `5b8207d8a853` | **Owner state:** Non-technical, requires evidence per phase before approval

## Current Baseline (Verified 2026-10-20)
- **Repo:** `JUBERSALEEM/ProGlassAutomation` | **Solution:** `ProGlassAutomation.slnx` | **TFM:** `net8.0-windows7.0` WPF
- **Architecture:** `Theme/step1.a.ThemeEngine` (non-visual orchestration) + `Theme/ThemeBento` (owns Light/Dark + 6 component dicts) + `step1.ThemeStudio/step1.b.ThemeStudioScreen` (Studio + Business Reference, separate pages) — **preserved after revert**
- **Last change:** Revert `5b8207d` — restored TabControl shell, removed preview duplication (`SurfaceSecondary→ApplicationBackground` outer), applied Bold `FontWeightHeading` both modes
- **Working tree:** 2 untracked (`ACCEPTANCE_GATE_REPORT_2026-10-20.md`, `step1.c.11.Windows-Verification.md`), no uncommitted modifications, `dotnet` unavailable locally (Windows CI required)
- **Tests:** `Theme/step1.d.tests` — `verify_sources.py` (99×2 tokens, 342 contrast, 28 XML), `verify_foundation.py` (46 styles, isolation), `CheckerRegression.py` (14 mutations) — all PASS in Linux source audit (not build)
- **Outstanding per AGENTS.md:** Phase 01 authorized 2026-10-20, implementation present, Windows build/runtime/visual acceptance **BLOCKED** (workflow inactive at `cols/ci/windows-wpf.yml`)

---

## Phase Structure — Ordered, Small, Reversible

### Phase 1 — Revert Stabilization & Bold-Hardening Verification
**ID:** `P1-REVERT-VERIFY`
- **Objective:** Confirm revert left no broken bindings/namescopes and bold heading propagates to all themed text. Expected: `MainWindow` TabControl visible on launch, `ThemeStudioView` + `BusinessReferenceView` tabs switch via `SelectedWorkspace`, headings render Bold in both modes.
- **Preserve:** Current Light `#FFFFFF/#E9EEF6`/navy hero and Dark `#080D18` palettes, `FontWeightHeading=Bold`, single-card preview, existing ViewModel `SelectedComponentId/AvailableCategories/BuiltInPresets/CustomPresets`.
- **Files expected to change:** *None unless verification finds defect* — read-only check of `MainWindow.xaml`, `ThemeBento.*.xaml`, `step1.b.1/18/22.xaml`, `ProGlassAutomation.csproj` includes.
- **Design changes:** None; validates recent `Bold` token reaches `TextBlock SectionHeading/PageHeading/Button` styles.
- **Dependencies:** None — first.
- **Steps:** 1) Re-read 6 changed files + diff vs `2f4b241`; 2) Grep for hard-coded `SemiBold`, duplicate `PreviewStack`, `FontSize` literals; 3) Run `verify_sources.py` + `verify_foundation.py` + `CheckerRegression.py`; 4) Manual XAML namescope check (no duplicate `x:Name`, no missing `BusinessModules` refs).
- **Tests/verification:** Source checks must PASS; namescope/bindings checked by reading, not build.
- **Acceptance:** All 3 source scripts PASS, no hard-coded heading weight, no duplicate preview container, diff shows only intended 8-file revert+Bold.
- **Evidence:** Script outputs (tail 5 lines), grep results, file head snippets.
- **Risks/rollback:** If bold breaks contrast, revert token to `SemiBold` — one-line change, no layout impact.
- **Docs/history:** Update `PROJECT_HISTORY.md` with revert entry; no `PROJECT_PLAN` status change yet.

### Phase 2 — Duplication & Visual-Clean Polish (No Structural Rebuild)
**ID:** `P2-CLEAN-POLISH`
- **Objective:** Remove remaining preview duplication and visual dullness *within approved style-only scope* — ensure Business Reference reads as clear professional image, not repeated chart grid.
- **Preserve:** TabControl shell, `ThemeService` single-slot replacement, `LiveCharts 2.0.4` pinned, `CustomizationStore` JSON, Business Reference startup.
- **Files expected:** `step1.b.22.CustomizeWorkspace.xaml` (if outer duplication remains), `step1.b.21.AnalyticsOverview.xaml` (plate contrast), `ThemeBento/step1.e.Components/step1.e.10.Analytics.xaml` (ChartCard `BorderBrush` subtlety).
- **Design:** Light `BorderSubtle #D3DDEC` may be too faint on `#FFFFFF` — consider `#C8D5E8` for card separation without hardcoding in views; review `ChartGrid` contrast on white.
- **Dependencies:** P1 PASS — confirms baseline stable.
- **Steps:** 1) Open current `CustomizeWorkspace` + `AnalyticsOverview` + `BusinessReferenceView` in editor, read card nesting; 2) Adjust *only* theme-owned tokens (no screen-local palettes); 3) Verify no duplicate `LiveOverview` visible on Business tab; 4) Re-run source checks.
- **Tests:** Source checks remain PASS; visual still BLOCKED (no Windows).
- **Acceptance:** No duplicate `SurfaceSecondary` wrapper on same preview, Business Reference cards have readable hierarchy (hero > KPI > analytics), no hard-coded colors introduced.
- **Evidence:** Before/after XAML snippet diff, token value table.
- **Risks:** Over-tuning borders could weaken Dark mode separation — change both modes together, measure contrast.
- **Docs:** History entry for polish; update `ThemeBento.Readme.md` if palette intent changes.

### Phase 3 — Windows Build & Runtime Verification (Unblock)
**ID:** `P3-WINDOWS-BUILD`
- **Objective:** Prove project compiles and theme switching works on real Windows — unblock all prior `BLOCKED` gates.
- **Preserve:** Branch `arena/bd154b4b-proglassautomation`, workflow inactive rule until authorized.
- **Files expected:** None (CI config) unless build reveals CS errors — then minimal C# fixes in `ThemeStudioViewModel`/`BentoChart` only.
- **Design:** None.
- **Dependencies:** P1–P2 complete — stable source to build.
- **Steps:** 1) Owner authorizes moving `cols/ci/windows-wpf.yml` → `.github/workflows/windows-wpf.yml` (outside Arena App, needs `workflows:write`); 2) Push triggers `windows-2022` job: `setup-dotnet 8.0.x` → `dotnet restore/build` both projects → `dotnet run -- --contracts-only` → STA smoke; 3) Capture logs, fail on errors/timeouts.
- **Tests:** `dotnet build ProGlassAutomation.csproj` SUCCESS, `dotnet build ThemeTests.csproj` SUCCESS, contracts + runtime PASS.
- **Acceptance:** CI green on branch, no introduced compile errors.
- **Evidence:** Workflow run URL, `dotnet --info` + build tail, commit SHA pushed.
- **Risks:** Missing XAML resource key → build fails — fix is adding missing brush in `ThemeBento.*.xaml`, not screen patches.
- **Docs:** Record actual commands/outcomes in `step1.c.2.Phase01-Verification.md` + history; mark build layer PASS.

### Phase 4 — Visual, Responsive & Accessibility Inspection
**ID:** `P4-VISUAL-ACCESS`
- **Objective:** Inspect real rendered output at 1280×900 and 720×600 (narrow) for both Light/Dark — confirm hierarchy, surfaces, charts, no white bleed in Dark.
- **Preserve:** Existing density/spacing choices (compact 32px default).
- **Files expected:** Only if inspection finds clipping/overlap/contrast bug — then fix shared resource (e.g., `CardCornerRadius`, `PaddingCard`) not per-screen hack.
- **Design:** Must meet AGENTS §9 A–G: Bento grid intent (not wall of identical cards), layered surfaces, readable chart axes/legends/tooltips, keyboard focus rings.
- **Dependencies:** P3 green — runnable exe exists.
- **Steps:** 1) Launch app on Windows, screenshot 10 required views (Studio Dark/Light, dashboard, donut, bar, line/area, customization workspace, presets, narrow window, save/reset states); 2) Visual defect audit (white plot areas, misaligned edges, weak contrast); 3) Keyboard/tab, resize, DPI check.
- **Tests:** Human inspection checklist (11 categories from contract §10).
- **Acceptance:** Screenshots genuine WPF (not mockup), defects logged with root file, no critical PASS-blocker remains.
- **Evidence:** 10 screenshots stored under `generated-tests/visual-evidence/2026-10-20/` with SHA reference.
- **Risks:** Chart donut hole shows white if `BentoChart.cs` transparent fix regressed — re-apply `Background=Transparent` in factory.
- **Docs:** Visual evidence report + history; do not mark Phase 01 approved until owner review.

### Phase 5 — Persistence & Lifecycle Hardening
**ID:** `P5-PERSISTENCE`
- **Objective:** Verify every customization control changes the real theme and survives restart, with safe fallback.
- **Preserve:** Session-only default per AGENTS (no DB), existing `SelectedComponentId` preview.
- **Files expected:** `step1.g.Customization/*`, `step1.b.17.Customization.cs`, `ThemeService.cs` notification path.
- **Dependencies:** P3–P4 — can exercise live app.
- **Steps:** Test each control: appearance, palette/surface/contrast/borders/shadows/corners/spacing/typography/density, 5 layout presets, component plates, chart kind/title/height/legend/grid/axis/labels/tooltip/dataset — check visible effect; test Apply/Save/Discard/Reset/PreviewDefault + custom preset CRUD + corrupt JSON fallback + appearance switch after customization.
- **Tests:** Matrix from `ACCEPTANCE_GATE_REPORT` §F — initial→action→expected→observed→PASS.
- **Acceptance:** No decorative-only control, persistence reloads after close, invalid save falls back to defaults without crash.
- **Evidence:** Matrix table with 22+ rows, file-save timestamps, `LocalAppData/bento-customization.json` sample.
- **Docs/history:** Final history entries; close outstanding items.

---

## Dependency Graph
`P1 → P2 → P3 → P4 → P5` — linear. No parallel work; each phase requires prior PASS/BLOCKED review. If P3 blocked (permission), P4–P5 wait.

## Approval Gate
- Owner reviews this plan. To proceed: say **"Apply P1"** (or `P1+P2`, or `all phases`). Agent then implements only authorized phase(s) following the 9-step lifecycle (reconfirm baseline, inspect, implement minimal, review diff, run checks, evaluate acceptance, update docs/history, commit/push only if authorized, report).
- No code changes made in this planning stage — files read only.
