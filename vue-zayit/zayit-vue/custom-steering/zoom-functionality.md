---
inclusion: auto
fileMatchPattern: "*zoom*"
keywords: zoom, pinch, trackpad, wheel, gesture
---

# Zoom Functionality

Comprehensive zoom handling for the Zayit application with support for keyboard shortcuts, trackpad/mouse wheel, and touch pinch-to-zoom gestures.

## Implementation Location

All zoom logic is centralized in `zayit-vue/src/utils/zoom.ts`.

## Features

- **Keyboard shortcuts**: Ctrl+Plus, Ctrl+Minus, Ctrl+0
- **Trackpad/Mouse wheel**: Ctrl+Wheel for smooth zooming
- **Touch gestures**: Pinch-to-zoom on touch screens
- **Configurable bounds**: Min 50%, Max 200%, Default 100%
- **Reactive**: Works with Vue refs and computed properties

## Usage Patterns

### In Components (Current: App.vue)

```typescript
import { ref, computed } from "vue";
import { useZoomHandler } from "@/utils/zoom";

const currentZoom = computed({
  get: () => tabStore.activeTab?.bookState?.zoom || 100,
  set: (value: number) => {
    const tab = tabStore.activeTab;
    if (tab?.bookState) {
      tab.bookState.zoom = value;
    }
  },
});

const isEnabled = computed(
  () => tabStore.activeTab?.currentPage === "bookview",
);

useZoomHandler({
  zoom: currentZoom,
  target: containerRef,
  enabled: isEnabled,
});
```

### In Stores (Current: tabStore.ts)

```typescript
import { zoomIn, zoomOut, resetZoom } from "@/utils/zoom";

const zoomIn = () => {
  const tab = tabs.value.find((t) => t.isActive);
  if (tab?.bookState) {
    const currentZoom = tab.bookState.zoom || 100;
    tab.bookState.zoom = zoomIn(currentZoom);
  }
};
```

## API Reference

### Constants

```typescript
ZOOM_CONFIG = {
  MIN: 50, // Minimum zoom level (50%)
  MAX: 200, // Maximum zoom level (200%)
  DEFAULT: 100, // Default zoom level (100%)
  STEP: 10, // Step size for keyboard zoom (10%)
  WHEEL_SENSITIVITY: 0.1, // Trackpad/wheel sensitivity
  PINCH_SENSITIVITY: 0.5, // Touch pinch sensitivity
};
```

### Pure Functions

- `calculateZoom(current: number, delta: number): number` - Calculate new zoom within bounds
- `zoomIn(currentZoom: number): number` - Zoom in by one step
- `zoomOut(currentZoom: number): number` - Zoom out by one step
- `resetZoom(): number` - Reset to default (100%)

### Composable

`useZoomHandler(options: ZoomHandlerOptions)` - Sets up comprehensive zoom handling

**Options:**

- `zoom: Ref<number>` - Current zoom level (reactive ref)
- `target?: Ref<HTMLElement> | HTMLElement | Window` - Target element (defaults to window)
- `enabled?: Ref<boolean> | boolean` - Whether zoom is enabled (defaults to true)
- `onZoomChange?: (newZoom: number) => void` - Callback when zoom changes

## User Interactions

### Keyboard Shortcuts

- **Ctrl+Plus** or **Ctrl+NumpadAdd**: Zoom in
- **Ctrl+Minus** or **Ctrl+NumpadSubtract**: Zoom out
- **Ctrl+0** or **Ctrl+Numpad0**: Reset zoom to 100%

### Trackpad/Mouse Wheel

- **Ctrl+Wheel Up**: Zoom in (smooth)
- **Ctrl+Wheel Down**: Zoom out (smooth)

### Touch Gestures

- **Pinch out** (two fingers apart): Zoom in
- **Pinch in** (two fingers together): Zoom out

## Implementation Details

### Event Handling

- Uses `useEventListener` from VueUse for automatic cleanup
- Wheel and touch events use `{ passive: false }` to allow `preventDefault()`
- Keyboard shortcuts use `event.code` for keyboard layout independence

### Touch Distance Calculation

- Uses Euclidean distance: `Math.sqrt(dx² + dy²)`
- Tracks initial distance and zoom on touchstart
- Calculates delta during touchmove
- Resets on touchend

### Zoom Bounds

- All zoom changes are clamped between MIN (50%) and MAX (200%)
- `calculateZoom()` ensures values never exceed bounds

## Current Implementation

### App.vue

- Attaches zoom handler to app container
- Only enabled when on bookview page
- Handles keyboard, trackpad, and touch zooming

### tabStore.ts

- Provides `zoomIn()`, `zoomOut()`, `resetZoom()` methods
- Used by toolbar buttons and other UI controls
- Updates active tab's `bookState.zoom` property

### BookLineViewer.vue & BookCommentaryView.vue

- Apply zoom via CSS: `fontSize: calc(var(--font-size) * ${zoom / 100})`
- Read zoom from `tabStore.activeTab?.bookState?.zoom || 100`

## Best Practices

1. **Use the composable for interactive elements** - Let `useZoomHandler()` manage all user interactions
2. **Use pure functions in stores** - Keep store logic simple with `zoomIn()`, `zoomOut()`, `resetZoom()`
3. **Enable/disable based on context** - Only enable zoom on appropriate pages
4. **Apply zoom via CSS** - Use `transform: scale()` or `fontSize` multiplication
5. **Centralize zoom state** - Store zoom level in tab state, not component state

## Browser Compatibility

- **Keyboard**: All modern browsers
- **Wheel**: All modern browsers
- **Touch**: Requires touch screen support (mobile devices, touch-enabled laptops)
