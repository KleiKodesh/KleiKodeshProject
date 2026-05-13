# settings

App settings UI and first-launch setup wizard.

**SettingsPage.vue** — three-tab settings page: general, fonts, and advanced. Each tab renders its own pane component.

**SettingsGeneralPane.vue** — general settings tab: theme, app zoom, toolbar position, resume last read, commentary auto-sync, new tab destination, and divine name censoring.

**SettingsFontsPane.vue** — fonts tab: book font/size/padding settings and optional separate commentary font settings.

**SettingsAdvancedPane.vue** — advanced tab: database path picker (hosted only), reset settings, and full app reset with confirmation dialogs.

**SettingRow.vue** — labeled layout wrapper for a single setting row. Use this for every new setting to keep spacing consistent.

**HintIcon.vue** — small info icon that shows a tooltip on hover. Used inside `SettingRow` to display hint text. Pass the hint string as a prop.

**SliderSetting.vue** — labeled slider for numeric settings.

**ToggleGroup.vue** — mutually exclusive toggle buttons for enum-style settings.

**ThemePicker.vue** — theme preset selector with custom theme builder.

**FontDisplaySettings.vue** — font and size controls for main text or commentary with live preview. Used in `SettingsFontsPane`.

**FontSelector.vue** — font family dropdown. Detects installed fonts via `detectFonts.ts` from `src/utils/`.

**SetupWizard.vue** — full-screen onboarding overlay shown when `settingsStore.setupDone` is false. Steps: welcome, database setup (hosted only), theme, general, book display. Adding a new setup step means adding it here and updating the step order. Completion sets `setupDone = true` in IDB and the wizard never shows again.

**useSettingsPage.ts** — tab state and derived values for the settings page. Keep settings logic here, not in the page component.
