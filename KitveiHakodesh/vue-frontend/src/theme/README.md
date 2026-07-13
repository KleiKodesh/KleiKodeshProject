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

**themes.ts** — theme apply logic and lookups. `applyTheme(preset)` sets the `data-theme-preset` attribute on `document.documentElement`, toggles the `.dark` class from the preset's `isDark`, writes all `--*-custom` UI vars and the `--reading-*` vars onto `documentElement.style`, and computes the derived vars `--accent-bg` / `--accent-bg-light` (from the accent color) and `--ui-reading-bg` (lighten/darken of the background). `toggleThemeMode(current)` computes the sibling preset as `` `${family}-${isDark ? 'light' : 'dark'}` ``. Also exports `getTheme`, `getAllThemes`, `getThemeFamilies`, and `isDarkTheme()` (reads the `.dark` class). `syncPdfViewerTheme()` and `initPdfThemeObserver()` push theme vars plus a computed `--pdf-filter-custom` (`calcPdfFilter`) into PDF.js viewer iframes — call `initPdfThemeObserver()` once at app boot.

**ThemePicker.vue** — custom theme-selection dropdown (not a native `<select>`). Reads `themePreset` from `themeStore` via `storeToRefs`. Renders a grid: rows are theme families, columns are Light and Dark variants. Each cell shows a mini swatch (background, accent, text). Theme family/variant data is grouped from `themes.json` at component initialization. The dropdown is teleported to `<body>` with `position: fixed` and opens up or down based on available space (max-height 320px). Uses `useDropdownClose` with `ignore: [boxRef]`; the toggle is handled manually by checking `isOpen` in the `toggle()` function rather than passing a `toggleButton` option.

**ThemeToggle.vue** — light/dark toggle button in the title bar. Calls `themeStore.toggleDarkMode()`. Not for use elsewhere. The `Ctrl+L` keyboard shortcut in `AppTitleBar.vue` calls the same method.

**themeTypes.ts** — TypeScript types for theme presets and theme objects. Import types from here.

**themeColorUtils.ts** — color manipulation utilities (`lighten`, `darken`, `hexToRgb`, `hexToRgbObj`) used by the apply logic and PDF filter math.

**themes.json** — built-in theme preset definitions: 106 presets across 53 families, each family with a `-light` and a `-dark` variant (e.g. `default-light`/`default-dark`, `vscode-light`/`vscode-dark`). Each preset is `{ name, family, isDark, reading: ThemeColors, ui: ThemeColors, pdfFilter? }` — every theme carries two color sets, `ui` for the app chrome and `reading` for the book text area. `ThemeColors` (see `themeTypes.ts`) is `bgPrimary`, `bgSecondary`, `bgTertiary?`, `textPrimary`, `textSecondary`, `borderColor`, `accentColor`, `hoverBg`, `activeBg`. Preset values win over the hardcoded fallbacks in `theme.css` at runtime. Add new built-in themes here. The app default preset is `default-light` (set in `themeStore`).

## Dark Mode Toggle Flow

`ThemeToggle.vue` (or `Ctrl+L` in `AppTitleBar.vue`) → `themeStore.toggleDarkMode()` → `toggleThemeMode(themePreset)` in `themes.ts` → swaps the preset (e.g. `'vscode-dark'` ↔ `'vscode-light'`) → the watcher in `themeStore` fires → `apply()` sets `--*-custom` properties on `:root` and adds/removes the `.dark` class on `document.documentElement` → CSS variables cascade throughout the app → `setTheme(isDark)` notifies C# to update the WinForms title bar (applied host-side via DarkNet in `AppViewerTheme.cs`).

There is no internal event bus for theme changes — propagation inside the app is purely the CSS-variable cascade off `document.documentElement`.
