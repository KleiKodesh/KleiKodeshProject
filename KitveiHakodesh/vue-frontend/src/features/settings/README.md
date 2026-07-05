# settings

App settings UI and first-launch setup wizard.

## Page structure

`SettingsPage.vue` is the page shell. It owns the layout (side nav + scroll body), the sticky search bar, the narrow-screen nav dropdown, and section scroll navigation. It renders four section components in order, with no business logic of its own.

The section components are independent — each imports the stores it needs directly. The `[data-section]` / `data-section-label` attributes on every section root are picked up automatically by `useSettingsSearch`; no manual registration needed.

## Section components

**SettingsPageDisplaySection.vue** — theme picker, dark mode toggle, PDF filter toggle, app zoom, toolbar position, new-tab destination, and title bar button visibility chips.

**SettingsPageReadingSection.vue** — resume last read, commentary sync default, divine name censoring, book display fonts/sizes/padding, max content width, and commentary display overrides. Calls `useSettings()` to wire the commentary-mirror watcher.

**SettingsPageStorageSection.vue** — HebrewBooks local folder, database path picker, and file-system search excluded folders manager (opens the native WinForms dialog via the C# bridge).

**SettingsPageResetSection.vue** — the four reset actions (settings, search index, document locator index, full app reset) with their `ConfirmDialog`. Completely self-contained — no props.

**SettingsPageShortcutsSection.vue** — keyboard shortcuts reference grid. No script, no reactivity.

## Shared primitives

**SettingRow.vue** — labeled layout wrapper for a single setting row. Use for every new setting to keep spacing consistent.

**SliderSetting.vue** — labeled slider for numeric settings.

**ToggleGroup.vue** — mutually exclusive toggle buttons for enum-style settings.

**ThemePicker.vue** — theme preset selector with color swatches grouped by family × light/dark.

**FontDisplaySettings.vue** — font and size controls for main text or commentary.

**FontSelector.vue** — font family dropdown. Detects installed fonts via `detectFonts.ts` from `src/utils/`.

## Composables

**useSettingsPage.ts** — wires the commentary-mirror watcher (syncs commentary font settings to book settings when `useSeparateCommentarySettings` is false) and exposes reset actions (`resetSettings`, `resetSearchIndex`, `resetDocumentLocatorIndex`, `resetAll`). Called by `SettingsPageReadingSection` and `SettingsPageSystemSection`.

**useSettingsSearch.ts** — DOM-walker search. Accepts a ref to the scroll container, watches `searchQuery`, walks every `[data-section]` element and toggles `data-section-hidden` on non-matching sections. Also exposes `getSectionNavEntries()` and `getSectionNavTree()` for the sidebar and drawer nav.

**appResetState.ts** — single exported `resetting` ref used to block UI during a reset/reload.

## Setup wizard

**SetupWizard.vue** — full-screen onboarding overlay shown when `settingsStore.setupDone` is false. Steps: welcome, database (hosted only), theme, general, book display. Completion sets `setupDone = true` in IDB.

**SetupWizardStepBookDisplay.vue**, **SetupWizardStepDb.vue**, **SetupWizardStepGeneral.vue**, **SetupWizardStepTheme.vue** — individual wizard steps.

## Adding a new settings section

Wrap the section content in `<div data-section="section-xxx" data-section-label="Hebrew label">` and add it to the appropriate section component (or create a new one if it's a distinct concern). The sidebar nav, drawer nav, and search filter all pick it up automatically from the DOM.

## Global CSS

`[data-section]`, `.section-label`, `.subsection-label`, and `[data-section-hidden]` are all defined as unscoped global styles in `SettingsPage.vue`. Section components rely on these classes being globally available — do not move them to scoped styles.
