# src/theme

Theme system: built-in presets, CSS variables, and PDF iframe sync.

## Files

**theme.css** — CSS custom properties for all themes. Two sets: one on `:root` (light defaults) and one on `:root.dark` (dark overrides). All color tokens use a `*-custom` fallback pattern — for example `var(--bg-primary-custom, #ffffff)` — so theme presets inject per-theme values by setting `--bg-primary-custom` on `:root` without touching the fallback. Never hardcode colors in component styles — always use a token from this file.

Light defaults: bg `#ffffff` / `#f8f8f8` / `#f0f0f0`, text `#1f1f1f` / `#5a5a5a`, accent `#0078d4`. Dark defaults: bg `#1e1e1e` / `#2d2d2d` / `#252525`, text `#d4d4d4` / `#a6a6a6`, accent `#60cdff`. `--input-bg` and `--input-bg-focus` are computed via `color-mix()` in both modes and have no `-custom` override. `--header-font` and `--text-font` are set only in `:root` and are not overridden in the dark block.

The five `--reading-*` properties (`--reading-bg-primary`, `--reading-bg-secondary`, `--reading-text-primary`, `--reading-text-secondary`, `--reading-border-color`) are not defined in this file — they are set dynamically by `themeStore.apply()` when `readingBackground !== 'default'`.

**themeStore.ts** — active theme preset and reading background. The only way to read or change the theme from a component or composable.

State: `themePreset` ref (default `'default-light'`) and `readingBackground` ref (default `'default'`). Both are persisted together under a single localStorage key (`KEYS.SETTINGS_THEME`) as `{ themePreset, readingBackground }`.

`init()`: loads from localStorage, then checks `window.__webviewIsDark` injected by C# — if the host dark mode setting disagrees with the stored theme, it calls `toggleThemeMode()` to reconcile them.

`apply()`: calls `applyTheme(themePreset.value)` which sets `--*-custom` CSS properties on `document.documentElement` and adds or removes the `.dark` class. If `readingBackground !== 'default'`, it also sets the five `--reading-*` custom properties.

`toggleDarkMode()`: flips dark/light by swapping the preset via `toggleThemeMode()` from `themes.ts`.

Watcher: any change to `themePreset` or `readingBackground` → persist to localStorage → `apply()` → call `setTheme(isDark)` on the C# bridge to update the WinForms title bar.

**themes.ts** — theme loading (`applyTheme`, `toggleThemeMode`) and the PDF theme observer that keeps the PDF.js iframe in sync with the app theme. Call `initPdfThemeObserver()` once at app boot.

**ThemePicker.vue** — custom theme-selection dropdown (not a native `<select>`). Reads `themePreset` from `themeStore` via `storeToRefs`. Renders a grid: rows are theme families, columns are Light and Dark variants. Each cell shows a mini swatch (background, accent, text). Theme family/variant data is grouped from `themes.json` at component initialization. The dropdown is teleported to `<body>` with `position: fixed` and opens up or down based on available space (max-height 320px). Uses `useDropdownClose` with `ignore: [boxRef]`; the toggle is handled manually by checking `isOpen` in the `toggle()` function rather than passing a `toggleButton` option.

**ThemeToggle.vue** — light/dark toggle button in the title bar. Calls `themeStore.toggleDarkMode()`. Not for use elsewhere. The `Ctrl+L` keyboard shortcut in `AppTitleBar.vue` calls the same method.

**themeTypes.ts** — TypeScript types for theme presets and theme objects. Import types from here.

**themeColorUtils.ts** — color manipulation utilities used when computing PDF filters.

**themes.json** — built-in theme preset definitions. Add new built-in themes here. Default theme is `vscode-dark`.

## Dark Mode Toggle Flow

`ThemeToggle.vue` (or `Ctrl+L` in `AppTitleBar.vue`) → `themeStore.toggleDarkMode()` → `toggleThemeMode(themePreset)` in `themes.ts` → swaps the preset (e.g. `'vscode-dark'` ↔ `'vscode-light'`) → the watcher in `themeStore` fires → `apply()` sets `--*-custom` properties on `:root` and adds/removes the `.dark` class on `document.documentElement` → CSS variables cascade throughout the app → `setTheme(isDark)` notifies C# to update the WinForms title bar.
