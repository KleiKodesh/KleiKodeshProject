# settings

App settings UI and first-launch setup wizard.

## Page structure

`SettingsPage.vue` is the page shell. It owns the layout (side nav + scroll body), the sticky search bar, the narrow-screen nav dropdown, and section scroll navigation. It renders four section components in order, with no business logic of its own.

The section components are independent — each imports the stores it needs directly. The `[data-section]` / `data-section-label` attributes on every section root are picked up automatically by `useSettingsSearch`; no manual registration needed.

## Section components

**SettingsPageThemeAndApplicationSection.vue** — theme picker, dark mode toggle, PDF filter toggle, app zoom, toolbar position, new-tab destination, and title bar button visibility chips.

**SettingsPageReadingAndBookDisplaySection.vue** — resume last read, commentary sync default, divine name censoring, book display fonts/sizes/padding, max content width, and commentary display overrides. Calls `useSettings()` to wire the commentary-mirror watcher.

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

**FontSelector.vue** — font family dropdown. Detects installed fonts via `detectFonts.ts` from `src/utils/`.

## Composables

**useSettingsPage.ts** — wires the commentary-mirror watcher (syncs commentary font settings to book settings when `useSeparateCommentarySettings` is false) and exposes the scoped reset actions (`resetSettings`, `resetSearchIndex`, `resetDocumentLocatorIndex`). The *full* app reset is not here — it is `resetEverything()` in `appResetState.ts`, which `SettingsPageResetSection` calls directly.

**useSettingsSearch.ts** — DOM-walker search. Accepts a ref to the scroll container, watches `searchQuery`, walks every `[data-section]` element and toggles `data-section-hidden` on non-matching sections. Also exposes `getSectionNavEntries()` and `getSectionNavTree()` for the sidebar and drawer nav.

**appResetState.ts** — the app-reset module. Exports the `resetting` ref that blocks the UI during a reset/reload (read by `App.vue`), and `resetEverything()`, which wipes every local database and localStorage key, then resets the host and reloads. `resetEverything` sets `resetting` itself, so callers just invoke it. This is the one place that owns a full reset — it deliberately does not live in a store, because it spans all of them.

## Setup wizard

**SetupWizard.vue** — full-screen onboarding overlay shown when `settingsStore.setupDone` is false. Steps: welcome, database (hosted only), theme, general, book display. Completion sets `setupDone = true` in IDB.

**SetupWizardStepBookDisplay.vue**, **SetupWizardStepDb.vue**, **SetupWizardStepGeneral.vue**, **SetupWizardStepTheme.vue** — individual wizard steps.

## Adding a new settings section

Wrap the section content in `<div data-section="section-xxx" data-section-label="Hebrew label">` and add it to the appropriate section component (or create a new one if it's a distinct concern). The sidebar nav, drawer nav, and search filter all pick it up automatically from the DOM.

## Global CSS

`[data-section]`, `.section-label`, `.subsection-label`, and `[data-section-hidden]` are all defined as unscoped global styles in `SettingsPage.vue`. Section components rely on these classes being globally available — do not move them to scoped styles.
