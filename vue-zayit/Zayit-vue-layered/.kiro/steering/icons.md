# Icon System

## Current Implementation

Using @iconify/vue with **Fluent icons** (Microsoft's design system). No custom icon components or mapping files.

## Usage

```typescript
import { Icon } from "@iconify/vue";
```

```vue
<!-- Basic usage - prefer 28-regular when available -->
<Icon icon="fluent:search-28-regular" />
<Icon icon="fluent:home-28-regular" />
<Icon icon="fluent:settings-28-regular" />

<!-- With sizing -->
<Icon icon="fluent:search-28-regular" :width="20" :height="20" />

<!-- Animated loading icons -->
<Icon icon="fluent:spinner-ios-20-regular" class="animate-spin" />
```

## Icon Sets

- **Primary**: `fluent:` - Microsoft Fluent Design System
- **Browse**: https://icon-sets.iconify.design/fluent/

## Size Guidelines

### Size Selection Strategy

- **Large UI elements** (main buttons, primary actions): `28-regular`
- **Medium UI elements** (toolbar buttons, secondary actions): `24-regular`
- **Small UI elements** (compact buttons, tab controls): `20-regular`
- **Tiny UI elements** (inline icons, status indicators): `16-regular`
- **Loading spinners**: Always `20-regular` (with `animate-spin`)

### Context-Based Sizing

```vue
<!-- Main toolbar buttons -->
<Icon icon="fluent:search-28-regular" />

<!-- Tab header buttons -->
<Icon icon="fluent:home-20-regular" />
<Icon icon="fluent:add-20-regular" />
<Icon icon="fluent:dismiss-20-regular" />

<!-- Dropdown menu items -->
<Icon icon="fluent:settings-24-regular" />

<!-- Loading states -->
<Icon icon="fluent:spinner-ios-20-regular" class="animate-spin" />
```

## Common Icons

- `fluent:search-28-regular` - Search
- `fluent:home-28-regular` - Home
- `fluent:settings-28-regular` - Settings
- `fluent:spinner-ios-20-regular` - Loading spinner (use with `animate-spin`)
- `fluent:open-28-regular` - Popout/external
- `fluent:document-pdf-28-regular` - PDF files
- `fluent:more-vertical-28-regular` - Menu dots
- `fluent:text-bullet-list-tree-24-regular` - Tree/TOC (preferred)
- `fluent:folder-tree-28-regular` - Tree/navigation (alternative, avoid)
- `fluent:panel-right-28-regular` - Split pane
- `fluent:color-background-24-regular` - Theme toggle (only available in 24-regular)
- `fluent:chevron-left-28-regular` - Navigation/skip forward (RTL interface)
- `fluent:chevron-right-28-regular` - Navigation/skip backward (RTL interface)
- `fluent:arrow-left-28-regular` - Forward navigation (RTL alternative)
- `fluent:arrow-right-28-regular` - Backward navigation (RTL alternative)
- `fluent:text-align-right-24-regular` - Line display (inline text, only available in 24-regular)
- `fluent:text-align-justify-24-regular` - Block display (justified text, only available in 24-regular)
- `fluent:flash-28-regular` - Performance/virtualization enabled
- `fluent:leaf-24-regular` - Eco mode/virtualization disabled (only available in 24-regular)

## Styling

Icons inherit `currentColor` and work with existing CSS:

- `.reactive-icon` - Hover/focus color changes
- `.animate-spin` - Rotation animation for loaders

## Rules

1. **No custom icon components** - Use @iconify/vue directly
2. **No mapping files** - Browse Iconify and use icon names directly
3. **Consistent sizing** - Use `:width` and `:height` props
4. **Animation** - Add `animate-spin` class for loading icons
5. **Use Fluent icons** - Prefer `fluent:` prefix for consistency
6. **Size preference** - Use `28-regular` when available, fallback to smaller sizes
7. **Check availability** - Some icons only exist in specific sizes (24-regular, 20-regular, etc.)

## Special Cases

**Tab Control Icons** - The + (add) and × (close) icons in tab headers should use the `.small-icon` CSS class:

```vue
<Icon icon="fluent:add-16-regular" class="small-icon" />
<Icon icon="fluent:dismiss-16-regular" class="small-icon" />
```

The `.small-icon` class is defined in `components.css` and sets `width: 12px; height: 12px;`.

**NEVER change diacritics icons** - DiacriticsFullIcon, DiacriticsNikkudOnlyIcon, DiacriticsNoneIcon must remain custom components. These have special visual states and Hebrew text functionality that cannot be replaced with generic icons.
