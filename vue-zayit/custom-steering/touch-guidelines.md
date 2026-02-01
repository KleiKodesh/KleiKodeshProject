---
inclusion: manual
---

# Touch-Friendly Development Guidelines

This steering guide provides comprehensive guidelines for implementing and maintaining touch-friendly interactions in the Zayit Vue application.

## 🎯 Core Principles

### Touch Target Standards

- **Minimum Size**: 44px × 44px (iOS standard) / 48px × 48px (Android standard)
- **Spacing**: Minimum 8px between touch targets
- **Feedback**: Immediate visual response to touch interactions
- **Accessibility**: Support for both touch and keyboard navigation

### Performance Requirements

- **Response Time**: Touch feedback within 100ms
- **Animations**: 60fps smooth transitions
- **Scrolling**: Native momentum scrolling where possible

## 🛠️ Implementation Guidelines

### 1. Touch-Friendly Components

#### Buttons

```vue
<button class="touch-interactive" @click="handleClick">
  <!-- Content -->
</button>
```

**Required CSS Classes:**

- `touch-interactive` - Base touch behavior
- `touch-target` - Ensures minimum size
- `touch-feedback` - Visual feedback animations

#### Interactive Elements

```vue
<div class="tree-node touch-interactive" @click="selectItem">
  <!-- Content -->
</div>
```

**Minimum Requirements:**

- `min-height: 44px`
- `touch-action: manipulation`
- `transition: transform 0.1s ease, background-color 0.1s ease`

### 2. Click Outside Detection

**Use the Touch-Friendly Composable:**

```typescript
import { useClickOutside } from "../composables/useClickOutside";

const containerRef = ref<HTMLElement>();
useClickOutside(containerRef, () => {
  // Handle outside click/touch
});
```

**Template:**

```vue
<div ref="containerRef" class="dropdown-container">
  <!-- Dropdown content -->
</div>
```

### 3. Dropdown Components

**Use TouchFriendlyDropdown:**

```vue
<TouchFriendlyDropdown>
  <template #trigger="{ toggle, isOpen }">
    <button @click="toggle">Menu</button>
  </template>
  <template #content="{ close }">
    <div class="dropdown-item" @click="handleItem1; close()">Item 1</div>
    <div class="dropdown-item" @click="handleItem2; close()">Item 2</div>
  </template>
</TouchFriendlyDropdown>
```

### 4. Touch Event Handling

**Use Touch Utilities:**

```typescript
import {
  addTouchFriendlyListener,
  getEventCoordinates,
} from "../utils/touchUtils";

const handleInteraction = (event: MouseEvent | TouchEvent) => {
  const { x, y } = getEventCoordinates(event);
  // Handle interaction
};

addTouchFriendlyListener(element, "start", handleInteraction);
```

## 📱 CSS Guidelines

### Required Touch Styles

#### Global Touch Behavior

```css
* {
  -webkit-tap-highlight-color: transparent;
  touch-action: manipulation;
}
```

#### Touch Targets

```css
.touch-target {
  min-height: 44px;
  min-width: 44px;
}

.touch-interactive {
  touch-action: manipulation;
  transition:
    transform 0.1s ease,
    background-color 0.1s ease;
}

.touch-interactive:active {
  transform: scale(0.98);
  background-color: var(--active-bg);
}
```

#### Device-Specific Styles

```css
/* Touch devices only */
@media (hover: none) and (pointer: coarse) {
  button {
    min-height: 44px;
    min-width: 44px;
  }

  .hover-bg:active {
    background: var(--active-bg);
  }
}

/* Hover-capable devices */
@media (hover: hover) {
  .hover-bg:hover {
    background: var(--hover-bg);
  }
}
```

### Scrolling Optimization

```css
.touch-scroll {
  -webkit-overflow-scrolling: touch;
  overscroll-behavior: contain;
  scroll-behavior: smooth;
}
```

## 🔧 Component Patterns

### 1. Tree Nodes

```vue
<template>
  <div
    class="tree-node touch-interactive"
    :style="{ paddingInlineStart: `${20 + depth * 20}px` }"
    @click="selectNode"
  >
    <Icon :icon="nodeIcon" />
    <span class="node-text">{{ node.title }}</span>
  </div>
</template>

<style scoped>
  .tree-node {
    min-height: 44px;
    padding: 12px 20px;
    gap: 12px;
    touch-action: manipulation;
  }
</style>
```

### 2. Search Controls

```vue
<template>
  <div class="search-controls">
    <input class="search-input touch-target" v-model="searchTerm" />
    <button class="search-btn touch-interactive" @click="search">
      <Icon icon="search" />
    </button>
  </div>
</template>

<style scoped>
  .search-input {
    min-height: 44px;
    padding: 12px 16px;
    touch-action: manipulation;
  }

  .search-btn {
    min-width: 44px;
    min-height: 44px;
  }
</style>
```

### 3. Tab Headers

```vue
<template>
  <div class="tab-header">
    <button
      v-for="action in actions"
      :key="action.id"
      class="tab-action touch-interactive"
      @click="action.handler"
    >
      <Icon :icon="action.icon" />
    </button>
  </div>
</template>

<style scoped>
  .tab-action {
    min-width: 44px;
    min-height: 44px;
    padding: 8px;
  }
</style>
```

## 🧪 Testing Checklist

### Touch Device Testing

- [ ] All buttons have minimum 44px touch targets
- [ ] Dropdowns close on outside touch
- [ ] No accidental zooming occurs
- [ ] Smooth scrolling in lists
- [ ] Visual feedback on all interactions
- [ ] Proper spacing between touch targets

### Cross-Platform Testing

- [ ] Hover effects work on desktop
- [ ] Keyboard navigation functions
- [ ] RTL layout touch interactions
- [ ] Performance on various devices
- [ ] Accessibility compliance

### Interaction Testing

- [ ] Single tap activates elements
- [ ] Long press doesn't interfere
- [ ] Swipe gestures work in scrollable areas
- [ ] Multi-touch doesn't cause issues

## 🚫 Common Pitfalls

### Avoid These Patterns

```css
/* DON'T: Too small for touch */
button {
  width: 20px;
  height: 20px;
}

/* DON'T: Hover-only feedback */
.item:hover {
  background: var(--hover-bg);
}

/* DON'T: Missing touch-action */
.draggable {
  /* Missing: touch-action: none; */
}
```

### Use These Instead

```css
/* DO: Proper touch targets */
button {
  min-width: 44px;
  min-height: 44px;
}

/* DO: Device-appropriate feedback */
@media (hover: hover) {
  .item:hover {
    background: var(--hover-bg);
  }
}

@media (hover: none) {
  .item:active {
    background: var(--active-bg);
  }
}

/* DO: Proper touch handling */
.draggable {
  touch-action: none;
}
```

## 🔄 Maintenance Guidelines

### Regular Audits

1. **Monthly**: Check new components for touch compliance
2. **Quarterly**: Test on actual touch devices
3. **Release**: Verify touch interactions work correctly

### Performance Monitoring

- Monitor touch response times
- Check for scroll performance issues
- Verify animation smoothness

### Accessibility Updates

- Ensure touch targets meet WCAG guidelines
- Test with assistive technologies
- Verify keyboard alternatives exist

## 📚 Resources

### Files to Reference

- `src/assets/styles/touch.css` - Touch-specific styles
- `src/composables/useClickOutside.ts` - Touch-friendly click outside
- `src/utils/touchUtils.ts` - Touch utility functions
- `src/components/common/TouchFriendlyDropdown.vue` - Reusable dropdown

### External Standards

- [iOS Human Interface Guidelines - Touch](https://developer.apple.com/design/human-interface-guidelines/inputs/touch/)
- [Material Design - Touch Targets](https://material.io/design/usability/accessibility.html#layout-and-typography)
- [WCAG 2.1 - Target Size](https://www.w3.org/WAI/WCAG21/Understanding/target-size.html)

---

**Context Key**: Use `#touch` to load these guidelines when working on touch interactions.
