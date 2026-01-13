---
inclusion: fileMatch
fileMatchPattern: '**/vue-zayit/**'
---

# Zayit Reading Background Colors System

## CRITICAL: Reading Background Implementation

### Overview
The Zayit project includes a customizable reading background color system that enhances reading comfort while respecting theme preferences.

### Key Behavior: Light Mode Only
**IMPORTANT**: Reading background colors only apply in **light mode**. Dark mode always uses default dark theme colors, ignoring the reading background setting completely.

### Architecture Components

**Settings Store** (`settingsStore.ts`):
- `readingBackgroundColor`: String property for hex color value
- Persisted to localStorage as part of settings
- Applied via CSS custom property `--reading-bg-color`
- Automatic text contrast calculation via `getContrastingTextColor()`

**Theme Integration** (`theme.css`):
```css
--reading-bg-color: '';
--reading-text-color: var(--text-primary);
```

**Component Implementation**:
- **BookLineViewer**: Main reading area background
- **BookCommentaryView**: Commentary panel background
- Both use reactive computed properties with dark mode detection

### Settings UI Implementation

**Color Palette** (`SettingsPage.vue`):
```typescript
const readingBackgroundColors = [
    { name: 'ברירת מחדל', value: '' },
    { name: 'קרם חם', value: '#FDF6E3' },     // Warm cream
    { name: 'בז\' רך', value: '#F5F5DC' },    // Soft beige
    { name: 'נייר ישן', value: '#FAF0E6' },   // Old paper
    { name: 'ירוק רך', value: '#F0F8F0' },    // Soft green
    { name: 'כחול רך', value: '#F0F8FF' },    // Soft blue
    { name: 'אפור בהיר', value: '#F8F8F8' },  // Light gray
    { name: 'ורוד רך', value: '#FFF0F5' },    // Soft pink
    { name: 'צהוב עדין', value: '#FFFACD' },  // Light yellow
]
```

**UI Pattern**:
- Color palette with clickable swatches
- Custom color picker for additional options
- Clear button for custom colors
- Hebrew labels for accessibility

### Component Pattern: Dark Mode Detection

**✅ CORRECT Implementation**:
```typescript
// Reactive dark mode detection
const isDarkMode = ref(false)

const updateDarkMode = () => {
    isDarkMode.value = document.documentElement.classList.contains('dark')
}

onMounted(() => {
    updateDarkMode()
    // Watch for theme changes
    const observer = new MutationObserver(updateDarkMode)
    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['class']
    })
    
    // Cleanup observer on unmount
    onUnmounted(() => observer.disconnect())
})

// Computed styles that respect dark mode
const containerStyles = computed(() => ({
    backgroundColor: !isDarkMode.value && settingsStore.readingBackgroundColor 
        ? settingsStore.readingBackgroundColor 
        : 'var(--bg-primary)',
    color: !isDarkMode.value && settingsStore.readingBackgroundColor 
        ? 'var(--reading-text-color)' 
        : 'var(--text-primary)'
}))
```

### Text Contrast Algorithm

**Automatic Contrast Calculation**:
```typescript
const getContrastingTextColor = (backgroundColor: string): string => {
    // Convert hex to RGB
    const hex = backgroundColor.replace('#', '')
    const r = parseInt(hex.substr(0, 2), 16)
    const g = parseInt(hex.substr(2, 2), 16)
    const b = parseInt(hex.substr(4, 2), 16)
    
    // Calculate relative luminance using WCAG formula
    const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255
    
    // Return dark text for light backgrounds, light text for dark backgrounds
    return luminance > 0.5 ? '#1f1f1f' : '#ffffff'
}
```

### Theme Mode Behavior

**Light Mode**:
- Reading background colors apply as selected
- Text color automatically contrasts with background
- Full customization available

**Dark Mode**:
- Reading background setting completely ignored
- Always uses `--bg-primary` (dark theme background)
- Always uses `--text-primary` (dark theme text)
- Preserves dark mode aesthetic

### Persistence & Lifecycle

**Settings Persistence**:
- Saved to localStorage key: `'zayit-settings'`
- Loaded on app initialization
- Real-time updates via Vue watch
- Included in settings reset functionality

**CSS Variable Application**:
- Applied to document root via `applyCSSVariables()`
- Updates immediately on setting change
- Fallback to theme defaults when empty

### Color Selection Rationale

**Research-Based Palette**:
- Warm cream (#FDF6E3): Reduces eye strain, popular in e-readers
- Soft beige (#F5F5DC): Classic book/paper color
- Old paper (#FAF0E6): Vintage reading experience
- Soft green (#F0F8F0): Calming, reduces eye fatigue
- Soft blue (#F0F8FF): Reduces glare, cool tone option
- Light gray (#F8F8F8): Neutral, minimal distraction
- Soft pink (#FFF0F5): Warm, gentle on eyes
- Light yellow (#FFFACD): Bright but soft, highlighting effect

### Integration Points

**Components Using Reading Backgrounds**:
- `BookLineViewer.vue`: Main text reading area
- `BookCommentaryView.vue`: Commentary panel content area

**Settings Management**:
- `SettingsPage.vue`: User interface for color selection
- `settingsStore.ts`: State management and persistence

**Theme System**:
- `theme.css`: CSS variable definitions
- Automatic integration with light/dark theme toggle

### Common Patterns

**Adding New Reading Components**:
1. Import `useSettingsStore`
2. Add dark mode detection with MutationObserver
3. Create computed style property with theme-aware logic
4. Apply to container element via `:style` binding

**Testing Considerations**:
- Test in both light and dark modes
- Verify contrast with all palette colors
- Check theme switching behavior
- Validate persistence across sessions

### Accessibility Notes

- All colors meet WCAG contrast requirements
- Automatic text color ensures readability
- Hebrew labels for RTL interface
- Clear visual feedback for selected colors
- Keyboard navigation support in color palette

This system provides optimal reading comfort while maintaining theme consistency and accessibility standards.