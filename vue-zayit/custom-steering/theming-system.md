---
inclusion: fileMatch
fileMatchPattern: "**/themes.ts|**/readingBackgrounds.ts|**/settingsStore.ts|**/ThemeSelector.vue|**/SettingsPage.vue"
---

# Theming System Architecture

## Overview

The Zayit theming system uses a unified approach where **reading backgrounds are simply references to theme reading colors**. This allows users to mix and match: choose any theme for the UI chrome and any theme's reading colors for the reading areas.

## Core Concept

**One System, Not Two:**

- Everything is a theme with UI colors and reading colors
- "Reading backgrounds" are just shortcuts to apply another theme's reading colors
- No duplicate color data - all colors come from themes

**Mental Model:**

- **Theme** = Complete outfit (UI chrome + reading area colors)
- **Reading Background** = Swap just the reading area colors, keep UI chrome

## File Structure

### `zayit-vue/src/data/themes.json`

**Purpose:** Single source of truth for all theme data (JSON format)

**Structure:**

```json
{
  "fluent-light": {
    "name": "עיצוב זורם",
    "family": "fluent",
    "isDark": false,
    "reading": {
      /* ThemeColors */
    },
    "ui": {
      /* ThemeColors */
    },
    "pdfFilter": "none"
  }
}
```

### `zayit-vue/src/data/themeTypes.ts`

**Purpose:** TypeScript type definitions for themes

**Key Types:**

```typescript
// Auto-generated from themes.json keys
export type ThemePreset = keyof typeof themesData | string;

export interface ThemeColors {
  bgPrimary: string;
  bgSecondary: string;
  bgTertiary?: string; // Optional: Subtle backgrounds (fallback to bgSecondary if not defined)
  textPrimary: string;
  textSecondary: string;
  borderColor: string;
  accentColor: string;
  hoverBg: string;
  activeBg: string;
}

export interface Theme {
  name: string;
  isDark: boolean;
  family: string;
  reading: ThemeColors;
  ui: ThemeColors;
  pdfFilter?: string;
}
```

**How it works:** TypeScript automatically infers `ThemePreset` type from themes.json keys using `keyof typeof`. No build script needed!

**Background Color Hierarchy:**

- `bgPrimary` - Main chrome (title bars, toolbars) - can be bold/colored
- `bgSecondary` - Panels, dropdowns, inputs - should be neutral
- `bgTertiary` (optional) - Subtle backgrounds, cards - even more neutral
  - If not defined in theme JSON, automatically falls back to `bgSecondary`
  - Future enhancement for better visual hierarchy in themed apps
  - Custom themes created via ThemeCreator automatically include this

### `zayit-vue/src/utils/themeColorUtils.ts`

**Purpose:** Pure color manipulation utilities (no side effects)

**Key Functions:**

- `hexToRgb(hex)` - Convert hex to RGB string
- `hexToRgbObj(hex)` - Convert hex to RGB object
- `lighten(color, amount)` - Lighten a color
- `darken(color, amount)` - Darken a color
- `adjustAlpha(isDark)` - Calculate hover/active alpha values
- `isDarkTheme(bgColor)` - Detect if color is dark
- `generateDarkVariant(lightColors)` - Generate proper dark theme from light theme
- `generateThemeColors(bg, text, accent)` - Generate complete ThemeColors from base colors

**Design:** All functions are pure - same input always produces same output, no DOM manipulation.

### `zayit-vue/src/utils/themes.ts`

**Purpose:** Theme management, application, and PDF.js syncing

**Key Functions:**

- `applyTheme(preset)` - Applies theme to document, sets both UI and reading CSS variables
- `toggleThemeMode(preset)` - Toggles between light/dark variant of same family
- `getThemeFamilies()` - Returns theme families for dropdown selector
- `getTheme(preset)` - Get theme by preset (built-in or custom)
- `addCustomTheme(id, theme)` - Add custom theme
- `deleteCustomTheme(id)` - Delete custom theme
- `syncPdfViewerTheme()` - Sync theme to PDF.js iframes
- `setPdfPageFilters(enabled)` - Enable/disable PDF page filters

**Imports:** Re-exports color utilities from themeColorUtils.ts for convenience.

### `zayit-vue/src/components/settings/useThemeBuilder.ts`

**Purpose:** Composable for building custom themes in ThemeCreator

**Key Features:**

- Mix-and-match mode: Edit light and dark variants independently
- Auto mode: Edit light variant, dark is auto-generated
- Reading background modes: same as UI, preset, or custom
- Proper dark variant generation (not simple inversion)

### `zayit-vue/src/components/settings/ThemeCreator.vue`

**Purpose:** UI for creating custom themes

**Features:**

- Base theme selection
- Reading background mode selection
- Mix-and-match toggle
- Color pickers for UI and reading colors
- Live preview of light and dark variants
- Uses useThemeBuilder composable for all logic

**Theme Families (18 families, 36 variants):**

1. Fluent (עיצוב זורם) - Default app theme
2. Sepia (גוון עתיק) - Antique reading tone
3. Night (מצב לילה) - Night mode
4. Gray (גווני אפור) - Gray tones
5. Warm (גוונים חמים) - Warm tones
6. Cream (קרם חם) - Warm cream
7. Beige (בז' רך) - Soft beige
8. Paper (נייר ישן) - Old paper
9. Green (ירוק רך) - Soft green
10. Blue (כחול רך) - Soft blue
11. Pink (ורוד רך) - Soft pink
12. Yellow (צהוב עדין) - Gentle yellow
13. Catppuccin (פסטל מודרני) - Modern pastel
14. Tokyo Night (טוקיו נייט) - Tokyo Night
15. Rosé Pine (אורן ורדרד) - Rosé Pine
16. Nord (ארקטי) - Arctic
17. Dracula (דרקולה) - Dracula
18. Everforest (יער ירוק) - Green forest

**Key Functions:**

- `applyTheme(preset)` - Applies theme to document, sets both UI and reading CSS variables
- `toggleThemeMode(preset)` - Toggles between light/dark variant of same family
- `getThemeFamilies()` - Returns theme families for dropdown selector

### `zayit-vue/src/utils/readingBackgrounds.ts`

**Purpose:** Provides reading background presets (references to themes)

**Structure:**

```typescript
export type ReadingBackgroundPreset =
  | "default" // Use current theme's reading colors
  | ThemePreset; // Use another theme's reading colors
```

**Key Points:**

- NO color data stored here - just references
- `getReadingBackgrounds()` returns list with display names and preview colors
- Preview colors are extracted from themes for UI display only

### `zayit-vue/src/stores/settingsStore.ts`

**Purpose:** Manages theme and reading background state

**Key Logic:**

```typescript
// 1. Apply theme (sets both UI and reading colors)
applyTheme(themePreset.value);

// 2. If reading background is not default, override reading colors
if (readingBackground.value !== "default") {
  const bgTheme = THEME_PRESETS[readingBackground.value];
  // Apply bgTheme.reading colors to --reading-* CSS variables
}
```

**Settings:**

- `themePreset: ThemePreset` - Current theme (affects UI + reading by default)
- `readingBackground: ReadingBackgroundPreset` - Reading area override ('default' or theme preset)

## CSS Variables

### UI Variables (set by theme)

```css
--bg-primary-custom
--bg-secondary-custom
--bg-tertiary-custom  /* Falls back to bgSecondary if not defined in theme */
--text-primary-custom
--text-secondary-custom
--border-color-custom
--accent-color-custom
--hover-bg-custom
--active-bg-custom
```

### Reading Variables (set by theme, can be overridden by reading background)

```css
--reading-bg-primary
--reading-bg-secondary
--reading-text-primary
--reading-text-secondary
--reading-border-color
--reading-accent-color
--reading-hover-bg
--reading-active-bg
```

### UI Reading Background (calculated from UI colors)

```css
--ui-reading-bg
```

**Purpose:** Provides a softer background for UI pages (like settings) that's derived from UI theme colors, NOT affected by custom reading backgrounds.

**Calculation:**

- Dark themes: `lighten(ui.bgPrimary, 3)`
- Light themes: `darken(ui.bgPrimary, 2)`

**Usage:** Settings page, dialogs, and other UI pages that need a reading-friendly background but should remain part of the UI chrome, not the content reading area.

### Usage in Components

- **UI Components:** Use `var(--bg-primary)`, `var(--text-primary)`, etc.
- **Reading Components:** Use `var(--reading-bg-primary)`, `var(--reading-text-primary)`, etc.
- **UI Reading Pages:** Use `var(--ui-reading-bg)` for background (e.g., SettingsPage)

**Reading Components (Content):**

- `LineView.vue` - Main text reading area
- `CommentaryView.vue` - Commentary reading area
- `KezayitSearchPage.vue` - Search results reading area

**UI Reading Pages (Chrome):**

- `SettingsPage.vue` - Settings interface
- Other UI pages that need softer backgrounds

**Key Distinction:**

- `--reading-bg-primary`: For actual book/commentary content, customizable by user
- `--ui-reading-bg`: For UI pages, always derived from UI theme, never customizable separately

## User Experience Flow

1. **User selects theme** → Sets both UI and reading colors
2. **User optionally selects reading background** → Overrides only reading colors
3. **Result:** UI chrome from theme A, reading area from theme B

**Examples:**

- Fluent theme + Sepia reading background
- Tokyo Night theme + Cream reading background
- Nord theme + Night reading background

## Adding New Themes

### 1. Add to themes.json

```json
{
  "newtheme-light": {
    "name": "שם בעברית",
    "family": "newtheme",
    "isDark": false,
    "reading": {
      "bgPrimary": "#ffffff",
      "bgSecondary": "#f8f8f8",
      "textPrimary": "#1f1f1f",
      "textSecondary": "#5a5a5a",
      "borderColor": "#e5e5e5",
      "accentColor": "#0078d4",
      "hoverBg": "rgba(0, 0, 0, 0.06)",
      "activeBg": "rgba(0, 0, 0, 0.09)"
    },
    "ui": {
      "bgPrimary": "#f8f8f8",
      "bgSecondary": "#f0f0f0",
      "textPrimary": "#1f1f1f",
      "textSecondary": "#5a5a5a",
      "borderColor": "#e0e0e0",
      "accentColor": "#0078d4",
      "hoverBg": "rgba(0, 0, 0, 0.08)",
      "activeBg": "rgba(0, 0, 0, 0.12)"
    },
    "pdfFilter": "none"
  },
  "newtheme-dark": {
    "name": "שם בעברית",
    "family": "newtheme",
    "isDark": true,
    "reading": {
      "bgPrimary": "#1e1e1e",
      "bgSecondary": "#2d2d2d",
      "textPrimary": "#ffffff",
      "textSecondary": "#a6a6a6",
      "borderColor": "#3b3b3b",
      "accentColor": "#60cdff",
      "hoverBg": "rgba(255, 255, 255, 0.08)",
      "activeBg": "rgba(255, 255, 255, 0.12)"
    },
    "ui": {
      "bgPrimary": "#1e1e1e",
      "bgSecondary": "#2d2d2d",
      "textPrimary": "#ffffff",
      "textSecondary": "#a6a6a6",
      "borderColor": "#3b3b3b",
      "accentColor": "#60cdff",
      "hoverBg": "rgba(255, 255, 255, 0.08)",
      "activeBg": "rgba(255, 255, 255, 0.12)"
    },
    "pdfFilter": "invert(0.9) hue-rotate(180deg) brightness(0.9) contrast(0.9)"
  }
}
```

**Note:** TypeScript will automatically recognize the new theme presets - no manual type updates needed!

### 2. Add to reading backgrounds

```typescript
// In readingBackgrounds.ts
const READING_BACKGROUND_NAMES: Record<ReadingBackgroundPreset, string> = {
  // ...
  "newtheme-light": "שם בעברית בהיר",
  "newtheme-dark": "שם בעברית כהה",
};

const READING_BACKGROUND_COLORS: Record<ReadingBackgroundPreset, string> = {
  // ...
  "newtheme-light": "#ffffff", // bgPrimary for preview
  "newtheme-dark": "#1e1e1e",
};
```

### 3. Add migration mapping (if replacing old theme)

```typescript
// In settingsStore.ts migration section
const oldToNewMap: Record<string, ThemePreset> = {
  "old-theme-name": "newtheme-light",
  // ...
};
```

## Custom Theme Creation

### User-Created Themes

Users can create custom themes through the ThemeCreator UI:

**Two Modes:**

1. **Auto Mode (Default)**
   - User edits light variant colors
   - Dark variant is automatically generated using `generateDarkVariant()`
   - Maintains color relationships (hue, saturation) while adapting for dark mode
   - Accent color is brightened for better visibility in dark mode

2. **Mix-and-Match Mode**
   - User can independently edit light and dark variants
   - Click preview cards to switch between editing light or dark
   - Full control over both variants

**Reading Background Options:**

- **Same as UI:** Reading colors match UI colors
- **Preset:** Use another theme's reading colors
- **Custom:** Define custom reading background and text colors

**Storage:**

- Custom themes are stored in localStorage
- Managed by `addCustomTheme()`, `deleteCustomTheme()`, `getCustomThemes()`
- Custom theme IDs use format: `custom-{timestamp}-light` / `custom-{timestamp}-dark`

### Dark Variant Generation Algorithm

The `generateDarkVariant()` function creates proper dark themes (not simple inversion):

1. **Preserve hue and saturation** from light background
2. **Generate dark background** with same color family but low lightness
3. **Invert text luminance** - dark text becomes light
4. **Brighten accent** - increase lightness while keeping hue
5. **Maintain relationships** - dark variant feels related to light variant

**Example:**

- Light: Warm beige bg (#f4ecd8), brown text (#5f4b32), gold accent (#8b6914)
- Dark: Dark brown bg (#3a2f1f), light beige text (#e8dcc4), light gold accent (#d4a574)

## Design Guidelines

### Background Color Hierarchy (Future Enhancement)

**Current State:**

- `bgTertiary` is optional in theme definitions
- Code supports it with automatic fallback to `bgSecondary`
- Custom themes created via ThemeCreator automatically include it
- Built-in themes in themes.json don't have it yet

**Future Implementation Plan:**
When adding `bgTertiary` to built-in themes:

1. **Fluent/Neutral Themes:** All three levels similar
   - Light: #ffffff → #f8f8f8 → #f0f0f0
   - Dark: #1e1e1e → #2d2d2d → #252525

2. **Office/Colored Themes:** Bold → Neutral → Subtle
   - Word Light: #2b579a (blue) → #f3f3f3 (gray) → #fafafa (lighter gray)
   - PowerPoint Light: #c43e1c (red) → #f3f3f3 (gray) → #fafafa (lighter gray)

3. **Component Usage:**
   - Title bars, main toolbars: `--bg-primary`
   - Dropdowns, panels, inputs: `--bg-secondary`
   - Cards, subtle backgrounds: `--bg-tertiary`

**Why This Matters:**

- Allows bold colored title bars without making entire UI overwhelming
- Provides proper visual hierarchy
- Maintains backward compatibility (existing themes work without changes)

### Reading vs UI Colors

- **Reading area:** Optimized for text readability, comfortable for long reading sessions
- **UI chrome:** Neutral, non-distracting, provides context without competing with content

### Color Relationships

- UI should be slightly lighter (dark themes) or darker (light themes) than reading area
- Maintain sufficient contrast for accessibility
- Keep accent colors consistent or complementary

### Theme Families

- Each family should have both light and dark variants
- Light variant: Comfortable for daytime reading
- Dark variant: Comfortable for nighttime reading

### Special Case: Fluent Theme

The Fluent theme (default) has **identical** reading and UI colors - no visual separation. This maintains the original app appearance. Other themes create visual separation between reading and UI areas.

## Migration & Backwards Compatibility

### Old Reading Background Colors

Legacy `readingBackgroundColor` (hex colors) are automatically migrated to theme-based presets:

- `#FDF6E3` → `cream-light`
- `#F5F5DC` → `beige-light`
- `#FAF0E6` → `paper-light`
- etc.

### Old Theme Names

Legacy theme names are migrated in settingsStore:

- `default-light` → `fluent-light`
- `white-light` → `fluent-light`
- `github-light` → `fluent-light`
- etc.

## Testing Checklist

When modifying the theming system:

- [ ] Type check passes (`npm run type-check`)
- [ ] All theme families have light and dark variants
- [ ] Reading backgrounds list matches available themes
- [ ] Theme selector shows correct names in Hebrew
- [ ] Reading background selector shows correct preview colors
- [ ] Switching themes updates both UI and reading areas
- [ ] Switching reading backgrounds updates only reading areas
- [ ] "ברירת מחדל" reading background uses current theme's reading colors
- [ ] Migration from old settings works correctly
- [ ] CSS variables are properly set in document root
- [ ] Dark mode class is applied/removed correctly

## Common Pitfalls

1. **Don't duplicate color data** - Reading backgrounds reference themes, don't store colors
2. **Don't forget both variants** - Every theme family needs light AND dark
3. **Don't break migration** - Keep old theme name mappings for user settings
4. **Don't use wrong CSS variables** - UI components use `--bg-primary`, reading uses `--reading-bg-primary`, UI reading pages use `--ui-reading-bg`
5. **Don't forget RGB values** - Some components need RGB for transparency
6. **Don't skip Hebrew names** - All user-facing names must be in Hebrew
7. **Don't confuse chrome and content** - Settings page is chrome (use `--ui-reading-bg`), book view is content (use `--reading-bg-primary`)

## Architecture: Separation of Chrome and Content

### The Problem

Users want to customize reading backgrounds for books/commentaries without affecting the settings page appearance. The settings page needs a pleasant background that's always derived from the UI theme.

### The Solution

**Three-tier background system:**

1. **UI Primary Background** (`--bg-primary-custom`)
   - Used for: Toolbars, sidebars, menus, buttons
   - Source: `theme.ui.bgPrimary`

2. **UI Reading Background** (`--ui-reading-bg`)
   - Used for: Settings page, dialogs, UI pages with text content
   - Source: Calculated from `theme.ui.bgPrimary` (slightly lighter/darker)
   - **Never customizable separately** - always follows UI theme

3. **Content Reading Background** (`--reading-bg-primary`)
   - Used for: Book view, commentary view, search results
   - Source: `theme.reading.bgPrimary` (can be overridden by reading background setting)
   - **Fully customizable** - user can pick any theme's reading colors

### Visual Hierarchy

```
┌─────────────────────────────────────┐
│  UI Chrome (--bg-primary-custom)    │  ← Darkest/Lightest
│  ┌───────────────────────────────┐  │
│  │ UI Reading (--ui-reading-bg)  │  │  ← Slightly softer
│  │                               │  │
│  │  Settings Page Content        │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘

┌─────────────────────────────────────┐
│  UI Chrome (--bg-primary-custom)    │
│  ┌───────────────────────────────┐  │
│  │ Content Reading               │  │
│  │ (--reading-bg-primary)        │  │  ← Fully customizable
│  │                               │  │
│  │  Book/Commentary Content      │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘
```

### Implementation Details

**In `themes.ts` `applyTheme()` function:**

```typescript
// Calculate UI reading background from UI colors
const uiReadingBg = theme.isDark
  ? lighten(uiColors.bgPrimary, 3)
  : darken(uiColors.bgPrimary, 2);
document.documentElement.style.setProperty("--ui-reading-bg", uiReadingBg);
```

**In component styles:**

```css
/* ❌ WRONG - Settings page using content reading background */
.settings-page {
  background: var(--reading-bg-primary);
}

/* ✅ CORRECT - Settings page using UI reading background */
.settings-page {
  background: var(--ui-reading-bg);
}

/* ✅ CORRECT - Book view using content reading background */
.book-view {
  background: var(--reading-bg-primary);
}
```

### Benefits

1. **Clear separation** - Chrome (UI) vs Content (reading) are visually distinct
2. **User control** - Users can customize book reading backgrounds without breaking UI
3. **Consistency** - Settings page always matches UI theme, never affected by custom reading backgrounds
4. **Flexibility** - Users can mix and match: Dark UI + Cream reading background for books
