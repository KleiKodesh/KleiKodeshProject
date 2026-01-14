---
inclusion: fileMatch
fileMatchPattern: '**/vue-zayit/**'
---

# Zayit Reading Background System

## Key Behavior: Light Mode Only
Reading background colors **only apply in light mode**. Dark mode always uses default dark theme colors.

## Component Pattern: Dark Mode Detection
```typescript
// Reactive dark mode detection
const isDarkMode = ref(false)

const updateDarkMode = () => {
    isDarkMode.value = document.documentElement.classList.contains('dark')
}

onMounted(() => {
    updateDarkMode()
    const observer = new MutationObserver(updateDarkMode)
    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['class']
    })
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

## Color Palette
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

## Text Contrast Algorithm
```typescript
const getContrastingTextColor = (backgroundColor: string): string => {
    const hex = backgroundColor.replace('#', '')
    const r = parseInt(hex.substr(0, 2), 16)
    const g = parseInt(hex.substr(2, 2), 16)
    const b = parseInt(hex.substr(4, 2), 16)
    
    // WCAG luminance formula
    const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255
    
    return luminance > 0.5 ? '#1f1f1f' : '#ffffff'
}
```

## CSS Integration
```css
--reading-bg-color: '';
--reading-text-color: var(--text-primary);
```

## Components Using Reading Backgrounds
- `BookLineViewer.vue`: Main text reading area
- `BookCommentaryView.vue`: Commentary panel content area