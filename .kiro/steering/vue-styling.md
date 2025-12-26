# Vue App Styling Guidelines

## Design System
- Base styling on Windows 11 Fluent Design System colors and VS Code app aesthetics
- Use consistent spacing, typography, and interaction patterns
- Maintain accessibility standards with proper contrast ratios

## Color Palette

### Windows 11 Fluent Colors
```css
/* Primary Colors */
--fluent-blue: #0078d4;
--fluent-blue-hover: #106ebe;
--fluent-blue-pressed: #005a9e;

/* Neutral Colors */
--fluent-grey-10: #faf9f8;
--fluent-grey-20: #f3f2f1;
--fluent-grey-30: #edebe9;
--fluent-grey-40: #e1dfdd;
--fluent-grey-50: #d2d0ce;
--fluent-grey-60: #c8c6c4;
--fluent-grey-70: #b3b0ad;
--fluent-grey-80: #a19f9d;
--fluent-grey-90: #8a8886;
--fluent-grey-100: #797775;
--fluent-grey-110: #69797e;
--fluent-grey-120: #5d5a58;
--fluent-grey-130: #484644;
--fluent-grey-140: #3b3a39;
--fluent-grey-150: #323130;
--fluent-grey-160: #292827;
--fluent-grey-170: #201f1e;
--fluent-grey-180: #161514;
--fluent-grey-190: #0b0a0a;

/* Semantic Colors */
--fluent-red: #d13438;
--fluent-green: #107c10;
--fluent-yellow: #ffd83d;
--fluent-orange: #ff8c00;
```

### VS Code Theme Colors
```css
/* VS Code Dark Theme */
--vscode-bg: #1e1e1e;
--vscode-sidebar-bg: #252526;
--vscode-editor-bg: #1e1e1e;
--vscode-panel-bg: #181818;
--vscode-border: #2d2d30;
--vscode-text: #cccccc;
--vscode-text-muted: #969696;
--vscode-accent: #007acc;
--vscode-hover: #2a2d2e;
--vscode-selection: #264f78;

/* VS Code Light Theme */
--vscode-light-bg: #ffffff;
--vscode-light-sidebar-bg: #f3f3f3;
--vscode-light-editor-bg: #ffffff;
--vscode-light-panel-bg: #f8f8f8;
--vscode-light-border: #e7e7e7;
--vscode-light-text: #333333;
--vscode-light-text-muted: #6c6c6c;
--vscode-light-accent: #0078d4;
--vscode-light-hover: #f0f0f0;
--vscode-light-selection: #add6ff;
```

## Typography
- **Primary Font**: 'Segoe UI', system-ui, -apple-system, sans-serif
- **Hebrew Font**: 'Segoe UI', 'Arial', sans-serif (good Hebrew support)
- **Monospace Font**: 'Cascadia Code', 'Fira Code', 'Consolas', monospace
- **Font Sizes**: 
  - Small: 12px
  - Body: 14px
  - Heading: 16px, 18px, 20px
- **Line Height**: 1.4 for body text, 1.2 for headings

## Hebrew Language Support

### RTL (Right-to-Left) Layout
- Use CSS `direction: rtl` for Hebrew content
- Apply `text-align: right` for Hebrew text alignment
- Use logical properties when possible: `margin-inline-start`, `padding-inline-end`
- Mirror layouts appropriately for RTL reading patterns

### Font Considerations
- Ensure fonts have proper Hebrew character support
- Use web-safe fonts with good Hebrew rendering: 'Segoe UI', 'Arial', 'Tahoma'
- Test Hebrew text rendering across different browsers and devices
- Consider font weight differences between Hebrew and Latin characters

### Layout Guidelines
```css
/* Hebrew content container */
.hebrew-content {
  direction: rtl;
  text-align: right;
  font-family: 'Segoe UI', 'Arial', sans-serif;
}

/* Mixed content (Hebrew + English) */
.mixed-content {
  direction: rtl;
  text-align: right;
}

.mixed-content .english-text {
  direction: ltr;
  display: inline-block;
}
```

### UI Component Adaptations
- Flip icon directions for RTL (arrows, chevrons)
- Adjust button and input field layouts
- Mirror navigation patterns (left/right becomes right/left)
- Ensure proper tab order for RTL interfaces

### Accessibility for Hebrew
- Provide proper `lang="he"` attributes for Hebrew content
- Ensure screen readers handle RTL text correctly
- Test keyboard navigation in RTL layouts
- Maintain proper focus indicators in RTL context

### Implementation Example
```vue
<template>
  <div class="app" :class="{ 'rtl': isHebrew }">
    <div class="content" :dir="isHebrew ? 'rtl' : 'ltr'">
      <!-- Content here -->
    </div>
  </div>
</template>

<style>
.app.rtl {
  direction: rtl;
}

.app.rtl .button-group {
  flex-direction: row-reverse;
}

.app.rtl .icon-arrow {
  transform: scaleX(-1);
}
</style>
```

## Component Styling

### SVG Icons
- All SVG icons should be created as separate reusable Vue components
- Place icon components in `src/components/icons/` directory
- Use consistent naming convention: `Icon[Name].vue` (e.g., `IconSearch.vue`, `IconReplace.vue`)
- Icons should accept `size` prop with default of 16px
- Icons should accept `color` prop with default of `currentColor`
- Use viewBox="0 0 24 24" for Material Design icons
- Example icon component structure:

```vue
<template>
  <svg 
    :width="size" 
    :height="size" 
    viewBox="0 0 24 24" 
    :fill="color"
    class="icon"
  >
    <path d="..." />
  </svg>
</template>

<script setup lang="ts">
interface Props {
  size?: number | string
  color?: string
}

withDefaults(defineProps<Props>(), {
  size: 16,
  color: 'currentColor'
})
</script>

<style scoped>
.icon {
  display: inline-block;
  vertical-align: middle;
  flex-shrink: 0;
}
</style>
```

### Buttons
```css
.btn-primary {
  background: var(--fluent-blue);
  border: 1px solid var(--fluent-blue);
  color: white;
  padding: 6px 12px;
  border-radius: 2px;
  font-size: 14px;
}

.btn-primary:hover {
  background: var(--fluent-blue-hover);
  border-color: var(--fluent-blue-hover);
}

.btn-secondary {
  background: transparent;
  border: 1px solid var(--fluent-grey-60);
  color: var(--fluent-grey-130);
  padding: 6px 12px;
  border-radius: 2px;
}
```

### Input Fields
```css
.input-field {
  border: 1px solid #d2d0ce;
  background: white;
  padding: 6px 8px;
  border-radius: 4px;
  font-size: 14px;
  font-family: inherit;
  outline: none;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.input-field:hover {
  border-color: #a19f9d;
}

.input-field:focus {
  border-color: #0078d4;
  box-shadow: 0 0 0 0.5px #0078d4;
}

/* Input containers with inner inputs */
.input-container {
  display: flex;
  border: 1px solid #d2d0ce;
  background: white;
  border-radius: 4px;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.input-container:hover {
  border-color: #a19f9d;
}

.input-container:focus-within {
  border-color: #0078d4;
  box-shadow: 0 0 0 0.5px #0078d4;
}

.input-container input {
  border: none;
  outline: none;
  background: transparent;
}

/* Select dropdowns */
.select-field {
  border: 1px solid #d2d0ce;
  background: white;
  padding: 4px 6px;
  border-radius: 4px;
  font-size: 12px;
  font-family: inherit;
  outline: none;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.select-field:hover {
  border-color: #a19f9d;
}

.select-field:focus {
  border-color: #0078d4;
  box-shadow: 0 0 0 0.5px #0078d4;
}

/* Placeholder styling */
.input-field::placeholder,
.input-container input::placeholder {
  color: #a19f9d;
}
```

### Panels and Containers
```css
.panel {
  background: var(--fluent-grey-10);
  border: 1px solid var(--fluent-grey-40);
  border-radius: 4px;
  padding: 16px;
}

.sidebar {
  background: var(--vscode-sidebar-bg);
  border-right: 1px solid var(--vscode-border);
  color: var(--vscode-text);
}
```

## Layout Principles
- Use CSS Grid and Flexbox for layouts
- Maintain consistent spacing using 4px, 8px, 12px, 16px, 24px increments
- Follow VS Code's panel and sidebar layout patterns
- Use subtle shadows and borders for depth

## Interactive States
- **Hover**: Subtle background color change
- **Active/Pressed**: Slightly darker background
- **Focus**: Blue outline or border
- **Disabled**: Reduced opacity (0.6) and no pointer events

## Dark Mode Support
- Always provide both light and dark theme variants
- Use CSS custom properties for easy theme switching
- Follow VS Code's dark theme color scheme
- Ensure proper contrast ratios for accessibility

## Accessibility
- Minimum contrast ratio of 4.5:1 for normal text
- Minimum contrast ratio of 3:1 for large text
- Provide focus indicators for keyboard navigation
- Use semantic HTML elements
- Include proper ARIA labels where needed

## Animation and Transitions
- Use subtle transitions (200-300ms) for state changes
- Prefer `ease-out` timing function
- Avoid excessive animations that may cause motion sickness
- Follow Windows 11 Fluent motion principles

## Implementation Example
```css
:root {
  /* Light theme (default) */
  --bg-primary: var(--fluent-grey-10);
  --bg-secondary: var(--fluent-grey-20);
  --text-primary: var(--fluent-grey-160);
  --text-secondary: var(--fluent-grey-120);
  --border-color: var(--fluent-grey-40);
  --accent-color: var(--fluent-blue);
}

[data-theme="dark"] {
  /* Dark theme */
  --bg-primary: var(--vscode-bg);
  --bg-secondary: var(--vscode-sidebar-bg);
  --text-primary: var(--vscode-text);
  --text-secondary: var(--vscode-text-muted);
  --border-color: var(--vscode-border);
  --accent-color: var(--vscode-accent);
}
```