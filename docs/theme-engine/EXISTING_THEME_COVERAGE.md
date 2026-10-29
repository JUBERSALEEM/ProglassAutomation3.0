# Existing theme coverage

**Corrected at `cbd0e5b` (round 56).** The previous version of this file listed
Glassmorphism, Spatial / Immersive, Swiss International and Holographic /
Iridescent as *not registered*. That was wrong at the revision the G1 review
examined: all nine are registered, installed and complete at token level. The
error was in this table, not in the catalog — and it is the reason G1-09 was
raised, so the correction is recorded rather than quietly overwritten.

**Two columns, two different kinds of evidence. Never mix them.**

- **Source coverage** (registered / Light / Dark / tokens) is *measured* by
  `Theme/step1.d.tests/step1.d.21.ModuleBoundaryChecks.py`, which fails if a theme
  leaves the catalog, if a mode dictionary is missing, or if any of the 18
  dictionaries defines a different token set. At this revision: **9 packages, 2
  modes each, 118 typed tokens in every one.**
- **Visual acceptance** is *owner-run*. This repository can state what was
  reported and when; it cannot claim a theme renders correctly, because no Windows
  run is possible in the authoring environment.

## 1. Registration and token coverage — measured

| # | Theme | Catalog | Light | Dark | Tokens | Components |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Bento Grid / Modular | Installed | Yes | Yes | 118 / 118 | 8 |
| 2 | Claymorphism | Installed | Yes | Yes | 118 / 118 | 8 |
| 3 | Fluent / Acrylic | Installed | Yes | Yes | 118 / 118 | 8 |
| 4 | Neumorphism | Installed | Yes | Yes | 118 / 118 | 8 |
| 5 | Liquid Glass | Installed | Yes | Yes | 118 / 118 | 8 |
| 6 | Glassmorphism | Installed | Yes | Yes | 118 / 118 | 8 |
| 7 | Spatial / Immersive | Installed | Yes | Yes | 118 / 118 | 8 |
| 8 | Swiss International | Installed | Yes | Yes | 118 / 118 | 8 |
| 9 | Holographic / Iridescent | Installed | Yes | Yes | 118 / 118 | 8 |

Source: `Theme/step1.a.ThemeEngine/step1.a.6.ThemeCatalog.cs` (`Installed`,
`Sequence`, all nine `ThemeImplementationStatus.Installed`) and the two mode
dictionaries per package. The counts above are produced by the checker, not typed
from memory.

## 2. Visual acceptance — recorded, not verified here

| # | Theme | Last acceptance evidence in this repository |
| --- | --- | --- |
| 1 | Bento Grid / Modular | Historically accepted tree `d1c4268`; later Liquid work did not regress it **in source** |
| 2 | Claymorphism | Owner sent Clay Home screenshots with a chart-collapse issue, pre-`703797e` |
| 3 | Fluent / Acrylic | None recorded recently |
| 4 | Neumorphism | None recorded recently |
| 5 | Liquid Glass | **BLOCKED** — D01 run 2026-10-22: source OK, no WPF pixels (`D01_LIQUID_VISUAL.md`) |
| 6 | Glassmorphism | Catalog records owner acceptance of Home, Light and Dark |
| 7 | Spatial / Immersive | None recorded |
| 8 | Swiss International | None recorded |
| 9 | Holographic / Iridescent | None recorded |

"No acceptance recorded" is not a defect and not a pass. It is the honest state:
these themes are implemented and token-complete, and nobody has said they look
right.

## 3. What each theme supplies

| # | Theme | Material | Customization | Studio preview | Home mosaic |
| --- | --- | --- | --- | --- | --- |
| 1 | Bento | cards/borders, `BentoGrid` | accent, density, shape, type, presets | Yes | Yes |
| 2 | Claymorphism | `ClaySurface` (plump, theme-owned geometry) | volume, plumpness, sheen | Yes | Yes |
| 3 | Fluent / Acrylic | `FluentSurface` tint + stroke | geometry/font/colour plates only — no extras engine | Yes | Yes |
| 4 | Neumorphism | `NeumorphSurface` | depth, blur, intensity | Yes | Yes |
| 5 | Liquid Glass | `LiquidGlassSurface` + coded wallpaper | frost, specular, tint | Yes | Yes |
| 6 | Glassmorphism | frost surface + solid fallback | none | Yes | Yes |
| 7 | Spatial | far plane + lifted near cards | none | Yes | Yes |
| 8 | Swiss | paper, ink, red rule; `SharedTokens` crisp geometry | none | Yes | Yes |
| 9 | Holographic | cyan-violet rim + pearl sheen; no chart blur | none | Yes | Yes |

## 4. Shared component contract (all installed themes)

Every package supplies the same styles: window, caption/section/page headings,
cards and plates, buttons (primary, destructive, subtle), inputs, selections,
popups (including the product's one implicit `ToolTip`), workspace (hero, metric,
badges, tab control), and the analytics/register set — `ChartCard`, `BentoKpi`,
`ListGrid`, `ListColumnHeader`, `ListCell`, `ListText`, `ListMonoText`,
`ListStrongText`. Isolation requires it: a package that omits one renders the
framework default and nothing reports it.

## 5. Honest gaps that remain

- No GPU backdrop blur anywhere — the glass and liquid materials are painted.
- Charts and registers remain opaque Skia/solid rows, not translucent surfaces.
- The customization payload still persists as `bento-customization.json`; the name
  is a historical artefact of a multi-theme file.
- `docs/06.Feature-Registry.md` is kept in step with the catalog by hand; if it
  drifts again, believe this document and the checker, not the registry.
- `CompactWidth` 1100 reflows the Studio to two columns in every theme.
- Nested `BentoGrid` (Home wraps AnalyticsOverview) still exists; the inner and
  outer row counts currently match.

## 6. Standing rule

Do not state a theme is "verified", "accepted" or "working" on the strength of a
folder, a dictionary or a row in a table. Registration and token completeness are
measured here; anything about how it *looks* is owner-run evidence, cited by date.
