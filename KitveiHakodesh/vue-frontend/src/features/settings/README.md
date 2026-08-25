# settings

App settings UI and first-launch setup wizard.

## Page structure

`SettingsPage.vue` is the page shell. It owns the layout (side nav + scroll body), the sticky search bar, the narrow-screen nav dropdown, and section scroll navigation. It renders four section components in order, with no business logic of its own.

The section components are independent — each imports the stores it needs directly. The `[data-section]` / `data-section-label` attributes on every section root are picked up automatically by `useSettingsSearch`; no manual registration needed.

## Section components

**SettingsPageThemeAndApplicationSection.vue** — theme picker, dark mode toggle, PDF filter toggle, app zoom, toolbar position, new-tab destination, and title bar button visibility chips.

**SettingsPageBookAndCommentaryDisplaySection.vue** — book display (resume last read, toolbar position, fonts/sizes/padding, max content width) and commentary display (sync default, separate-settings overrides). Calls `useSettings()` to wire the commentary-mirror watcher.

**SettingsPageCensorDivineNamesSection.vue** — divine name censoring: main-mode toggle plus the conditional Elokim and hyphenated-names rows.

**SettingsPageCalendarSection.vue** — Hebrew calendar and zmanim location settings.

**SettingsPageAdvancedSection.vue** — HebrewBooks local folder, database path, file-search excluded folders, and the automatic-update toggle. Every control here is backed by a Windows registry value or an on-disk file shared with the hosted app, never by localStorage — so the setting is the same one in dev and hosted. Hosted mode routes through the C# bridge; dev routes through the KitveiHakodesh service, which reads and writes the identical registry values. Branch on `isDev` (`typeof window.__webviewAction !== 'function'`), never on `isHosted` — `isHosted` is TRUE in dev and will silently disable working controls.

**SettingsPageResetSection.vue** — the four reset actions (settings, search index, document locator index, full app reset) with their `ConfirmDialog`. Completely self-contained — no props.

**SettingsPageKeyboardShortcutsSection.vue** — keyboard shortcuts reference grid. No script, no reactivity.

**SettingsExcludedFoldersDialog.vue** — dev-mode excluded-folders manager, mirroring the hosted app's WinForms `ExcludedFoldersForm` (list, add via the native folder dialog, remove selected, confirm to persist) in the app's own theme. Nothing is written until אישור. Persists via `setExcludedFolders` in `bridge.ts` to `excluded_folders.json` inside the file-search index directory — the same file and format the hosted DocumentLocator service uses. Hosted mode opens the native dialog instead and never mounts this component.

## Shared primitives

**SettingRow.vue** — labeled layout wrapper for a single setting row. Use for every new setting to keep spacing consistent.

**SliderSetting.vue** — labeled slider for numeric settings.

**ToggleGroup.vue** — mutually exclusive toggle buttons for enum-style settings.

**ThemePicker.vue** — theme preset selector with color swatches grouped by family × light/dark.

**FontDisplaySettings.vue** — font and size controls for main text or commentary.

**FontSelector.vue** — font family dropdown. Bundled fonts (Taamey Frank CLM, Frank Ruhl Libre, Heebo) sort first; the "ברירת מחדל" badge marks the ONE default belonging to that particular dropdown, passed in as `defaultFont`. Loads installed Hebrew-capable fonts live on every open via `detectAvailableFonts()` from `src/webview-host/fontsApi.ts` (no cache; a loading row shows while the enumeration runs).

**FontPreviewBox.vue** — the live text sample shown above the font controls in the setup wizard's book and commentary steps. `position: sticky` at the top of the wizard card's scroller, so it stays visible while the controls scroll under it; the negative margins in its styles exist to cancel the step body's padding and reach the card's own edges. Sample text and font values are all props, so each step supplies its own.

## Composables

**useSettingsPage.ts** — wires the commentary-mirror watcher (syncs commentary font settings to book settings when `useSeparateCommentarySettings` is false) and exposes the scoped reset actions (`resetSettings`, `resetSearchIndex`, `resetDocumentLocatorIndex`). The *full* app reset is not here — it is `resetEverything()` in `appResetState.ts`, which `SettingsPageResetSection` calls directly.

**useSettingsSearch.ts** — DOM-walker search. Accepts a ref to the scroll container, watches `searchQuery`, walks every `[data-section]` element and toggles `data-section-hidden` on non-matching sections. Also exposes `getSectionNavEntries()` and `getSectionNavTree()` for the sidebar and drawer nav.

**appResetState.ts** — the app-reset module. Exports the `resetting` ref that blocks the UI during a reset/reload (read by `App.vue`), and `resetEverything()`, which wipes every local database and localStorage key, then resets the host and reloads. `resetEverything` sets `resetting` itself, so callers just invoke it. This is the one place that owns a full reset — it deliberately does not live in a store, because it spans all of them.

## Setup wizard

**SetupWizard.vue** — full-screen onboarding overlay shown when `settingsStore.setupDone` is false. Steps: welcome, database (hosted only), theme, general, book display, commentary display, shortcuts. Completion sets `setupDone = true` in IDB.

SetupWizard.vue also calls `useSettings()` itself, to wire the commentary-mirror
watcher for the duration of the flow. It must be registered there, not in the
commentary step: steps unmount on every navigation (the Transition is keyed on the
step), and the book fonts are chosen on the step BEFORE the commentary one — a watcher
owned by that step would not be alive when the values it mirrors change.

The wizard owns ONE card and only its body content changes between steps: the card's
title, scroll area, and nav row (דלג / הקודם / הבא) are rendered once by SetupWizard.vue
and hold their position across the step transition. Each step's heading text therefore
lives in the `STEPS` table in SetupWizard.vue, not in the step component — the header is
part of the frame that stays put.

**SetupWizardStepBookDisplay.vue**, **SetupWizardStepCommentaryDisplay.vue**, **SetupWizardStepDb.vue**, **SetupWizardStepGeneral.vue**, **SetupWizardStepShortcuts.vue**, **SetupWizardStepTheme.vue** — the per-step body content, and nothing else: no card, no title, no nav. Multi-root templates by design.

## Adding a new settings section

Wrap the section content in `<div data-section="section-xxx" data-section-label="Hebrew label">` and add it to the appropriate section component (or create a new one if it's a distinct concern). The sidebar nav, drawer nav, and search filter all pick it up automatically from the DOM.

## Global CSS

`[data-section]`, `.section-label`, and `[data-section-hidden]` are all defined as unscoped global styles in `SettingsPage.vue`. Section components rely on these classes being globally available — do not move them to scoped styles.

There is one heading level. A group of settings that needs its own heading gets its own card, not a heading nested inside one — that's what keeps a card to a single horizontal rule. Row labels carry their own context (`תיקיית ספרים מקומית של היברו בוקס`, not a `היברו בוקס` heading over `תיקיית ספרים מקומית`), so a heading is never doing a label's job.
