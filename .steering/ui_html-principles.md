---
inclusion: fileMatch
fileMatchPattern: '**/*.html|**/*.css|**/*.vue'
---

# HTML/CSS Principles

**Note**: These principles apply equally to Vue projects. Vue is treated as an extension of HTML/CSS/JS.

## HTML Structure
- **Semantic containers**: Use logical container divs for grouping
- **Consistent class naming**: Follow established patterns (`.container`, `.flex-row`, `.input-wrapper`)
- **Clear section separation**: UI sections clearly separated and organized

## CSS Standards
- **CSS Variables**: Use `var(--property)` for all theming and consistent values
- **Minimal selectors**: Simple, direct selectors without deep nesting
- **Consistent spacing**: Standard gap/padding patterns (5px, 10px)
- **Flexbox layout**: Clean flex patterns for responsive design
- **No complex styling**: Simple, functional styles without unnecessary decoration

## Flat List Design
All list views MUST follow strict flat design pattern:

### CRITICAL Requirements
- **NO gaps between items**: Never use `gap`, `margin`, or spacing between list items
- **NO borders**: No borders, border-radius, or visual separators of any kind
- **NO background highlights**: Search highlighting uses ONLY `font-weight: bold`, never background colors
- **NO rounded corners**: All items must be completely square/flat

### State Management
- **Hover state**: `background: var(--hover-background-color)` ONLY
- **Selected state**: `background: var(--active-background-color)` (different from hover)
- **Focused state**: Same as hover state
- **Default state**: No background (transparent/inherit from container)

### Layout Structure
```css
.list-container {
    display: flex;
    flex-direction: column;
    /* NO gap property */
}

.list-item {
    padding: 4px 8px;
    /* NO margin, border, border-radius */
}
```

### Text Highlighting
- **Search matches**: Use `font-weight: bold` ONLY
- **NO background colors** for text highlighting
- **NO padding or border-radius** on highlighted text

## Scrollbar Implementation
- **Contained scrolling**: Scrollbars ONLY on specific content containers, NEVER on entire app
- **Use flexbox hierarchy**: Create proper container structure for scroll containment
- **Use `flex: 1`**: Fills available space naturally
- **Use `overflow: hidden`** on parent containers to contain scrolling
- **Never use fixed heights** (max-height: 300px) - doesn't adapt to window size