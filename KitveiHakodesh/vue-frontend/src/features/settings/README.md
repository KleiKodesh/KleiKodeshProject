# settings

App settings UI and first-launch setup wizard.

**SettingsPage.vue** — settings layout with a search bar + nav dropdown in a top toolbar, and a centered content column (max 680px) below it. The content column is inside a full-width scroller so the scrollbar sits at the page edge. Each section is a card (rounded border, bg-secondary). Sections: אפליקציה, ניווט, קריאה, תצוגת ספר, תצוגת פירושים, לוח שנה, מסד נתונים, איפוס.

**SettingsAdvancedPane.vue** — the calendar, database, and reset sections. Each section is wrapped in `data-section` / `data-section-label` divs so the DOM walker in `useSettingsSearch` can find and filter them. No props.

**SettingRow.vue** — labeled layout wrapper for a single setting row. Use this for every new setting to keep spacing consistent.

**HintIcon.vue** — small info icon that shows a tooltip on hover. Used inside `SettingRow` to display hint text. Pass the hint string as a prop.

**SliderSetting.vue** — labeled slider for numeric settings.

**ToggleGroup.vue** — mutually exclusive toggle buttons for enum-style settings.

**ThemePicker.vue** — theme preset selector with color swatches grouped by family × light/dark.

**FontDisplaySettings.vue** — font and size controls for main text or commentary. Used in `SettingsPage` for both book and commentary display sections.

**FontSelector.vue** — font family dropdown. Detects installed fonts via `detectFonts.ts` from `src/utils/`.

**SetupWizard.vue** — full-screen onboarding overlay shown when `settingsStore.setupDone` is false. Steps: welcome, database setup (hosted only), theme, general, book display. Completion sets `setupDone = true` in IDB and the wizard never shows again.

**useSettingsSearch.ts** — DOM-walker search composable. Accepts a ref to the scroll container, watches `searchQuery`, and after each render tick walks every `[data-section]` element, reads all its text nodes, and toggles `data-section-hidden` on sections that don't match. Also exposes `getSectionNavEntries()` which reads `data-section` and `data-section-label` attributes to build the nav dropdown list.

**appResetState.ts** — single exported `resetting` ref used to block UI during a reset/reload.

## Adding a new settings section

1. Add a `<div data-section="section-xxx" data-section-label="Hebrew label">` block with the section content in the scroll area of `SettingsPage.vue`.
2. The sidebar nav, drawer nav, and search filter all pick it up automatically from the DOM — no manual registration needed.
3. If the section belongs to the advanced group (calendar/db/reset), add it to `SettingsAdvancedPane.vue` instead.
