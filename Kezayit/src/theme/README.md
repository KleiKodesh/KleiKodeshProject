# src/theme

Theme system: presets, custom themes, CSS variables, and PDF iframe sync.

**theme.css** — CSS custom properties for all themes. All color, font, and spacing tokens are defined here. Never hardcode colors in component styles — always use a token from this file.

**themeStore.ts** — active theme preset and reading background color. Read and change the theme through this store.

**themes.ts** — theme loading, custom theme support, and the PDF theme observer that keeps the PDF.js iframe in sync with the app theme. Call `initPdfThemeObserver()` once at app boot.

**themeTypes.ts** — TypeScript types for theme presets and custom theme objects. Import types from here.

**themeColorUtils.ts** — color manipulation utilities for building and previewing custom themes.

**ThemeToggle.vue** — light/dark toggle button rendered in the title bar. Not for use elsewhere.

**themes.json** — built-in theme preset definitions. Add new built-in themes here. Default is `vscode-dark`.
