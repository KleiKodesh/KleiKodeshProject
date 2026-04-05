# settings

App settings UI and first-launch setup wizard.

**SettingsPage.vue** — three-tab settings page: general, reading display, and app reset.

**SettingRow.vue** — labeled layout wrapper for a single setting. Use this for every new setting row to keep spacing consistent.

**SliderSetting.vue** — labeled slider for numeric settings.

**ToggleGroup.vue** — mutually exclusive toggle buttons for enum-style settings.

**ThemePicker.vue** — theme preset selector with custom theme builder.

**FontDisplaySettings.vue** — font and size controls for main text and commentary with live preview.

**FontSelector.vue** — font family dropdown.

**SetupWizard.vue** — full-screen onboarding overlay shown when `settingsStore.setupDone` is false. Steps: welcome, database setup (hosted only), theme, general, book display. Adding a new setup step means adding it here and updating the step order. Completion sets `setupDone = true` in IDB and the wizard never shows again.

**useSettingsPage.ts** — tab state and derived values for the settings page. Keep settings logic here, not in the page component.
