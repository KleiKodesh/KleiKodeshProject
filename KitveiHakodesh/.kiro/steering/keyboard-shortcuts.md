---
inclusion: manual
---

# Keyboard Shortcuts Reference

This document catalogs all keyboard shortcuts implemented in the application, including their descriptions, functionality, and implementation locations.

## Tab Management

| Shortcut | Description | Frontend File | Backend |
|----------|-------------|---------------|---------|
| Alt+N | Open new tab | `src/layout/AppTitleBar.vue` | — |
| Alt+T | Open tabs list dropdown | `src/layout/AppTitleBar.vue` | — |
| Ctrl+W | Close current tab | `src/layout/AppTitleBar.vue` | — |
| Ctrl+X | Close all tabs | `src/layout/AppTitleBar.vue` | — |

## Navigation

| Shortcut | Description | Frontend File | Backend |
|----------|-------------|---------------|---------|
| Alt+Home | Go to home page | `src/layout/AppTitleBar.vue` | — |
| Alt+M | Open navigation menu | `src/layout/AppTitleBar.vue` | — |

## Theme & Display

| Shortcut | Description | Frontend File | Backend |
|----------|-------------|---------------|---------|
| Alt+L | Toggle theme (dark/light) | `src/theme/ThemeToggle.vue` | — |
| Alt+F | Toggle title bar visibility | `src/composables/useUiChromeVisibility.ts` | — |
| F11 | Toggle fullscreen mode | `src/layout/AppTitleBar.vue` | `CSharpBackend/KitveiHakodeshLib/AppViewer.cs` |

## Book View Controls

| Shortcut | Description | Frontend File | Backend |
|----------|-------------|---------------|---------|
| Ctrl+B | Toggle toolbar (book view/PDF) | `src/layout/AppTitleBar.vue` | — |
| Ctrl+J | Toggle commentary panel (book view) | `src/layout/AppTitleBar.vue` | — |
| Ctrl+F | Open search bar (book view) — works from anywhere when book view is active | `src/layout/AppTitleBar.vue`, `src/stores/bookViewStore.ts`, `src/features/book-view/BookViewPage.vue` | — |

## Zoom Controls

| Shortcut | Description | Frontend File | Backend |
|----------|-------------|---------------|---------|
| Ctrl++ | Increase zoom | `src/composables/useZoom.ts` | — |
| Ctrl+- | Decrease zoom | `src/composables/useZoom.ts` | — |
| Ctrl+0 | Reset zoom to default | `src/composables/useZoom.ts` | — |

## Special Functions

| Shortcut | Description | Frontend File | Backend |
|----------|-------------|---------------|---------|
| F7 | Enable text cursor (like text editor) | `src/layout/AppTitleBar.vue` | — |

## Implementation Details

### Frontend Architecture

**Main keyboard event listener:** `src/layout/AppTitleBar.vue`
- Always mounted (even when title bar is hidden)
- Uses `useEventListener('keydown', ...)` from VueUse with `{ capture: true }` flag
- Listens in capture phase to intercept shortcuts before child elements can consume them
- Handles most tab management, navigation, and view control shortcuts

**Specialized handlers:**
- `src/composables/useZoom.ts` — zoom shortcuts (Ctrl++, Ctrl+-, Ctrl+0)
- `src/composables/useUiChromeVisibility.ts` — Alt+F (toggle title bar)
- `src/theme/ThemeToggle.vue` — Alt+L (toggle theme)

**Book view search signal:**
- AppTitleBar calls `bookViewStore.openSearch()` on Ctrl+F when book view is active
- BookViewPage watches `openSearchSignal` and opens the search bar
- This allows Ctrl+F to work from anywhere (title bar, toolbar, side panel) without requiring focus on the book view scroller

### Backend Implementation

**C# Fullscreen Handler:** `CSharpBackend/KitveiHakodeshLib/AppViewer.cs`

The `HandleToggleFullscreen` method is called when F11 is pressed. It:
1. Finds the parent Form dynamically using `FindForm()`
2. Toggles between fullscreen (FormBorderStyle.None + Maximized) and normal mode
3. Executes on the UI thread using `Invoke` if necessary

Bridge entry point: `case "toggleFullscreen": HandleToggleFullscreen(id); break;` in the action dispatcher

## Bridge Communication Flow

For F11 fullscreen toggle:

```
Frontend (AppTitleBar.vue)
    ↓ F11 keydown
Vue event handler calls toggleFullscreen()
    ↓
Frontend (bridge.ts)
    ↓ calls action('toggleFullscreen')
via window.__webviewAction (injected by JsBridge.cs)
    ↓
Backend (AppViewer.cs)
    ↓ action dispatcher switch statement
HandleToggleFullscreen() called
    ↓ calls _bridge.Reply()
Result sent back to frontend
```

## Rules for Adding New Shortcuts

1. **Always add to AppTitleBar.vue keyboard listener** unless the shortcut is context-specific
2. **Use capture phase for global shortcuts** — the AppTitleBar listener uses `{ capture: true }` to intercept events before child elements, ensuring shortcuts work from anywhere
3. **Update the Settings page** (`src/features/settings/SettingsPage.vue`) with the new shortcut in the appropriate category
4. **For C# backend actions:** Add case to the switch statement in `AppViewer.cs` and implement the handler method
5. **Use logging sparingly** — only during debugging, remove before committing
6. **Test keyboard events** work when the title bar is hidden (use Alt+F to hide, then test your shortcut)
7. **Update this document** when adding, removing, or modifying shortcuts

## Event Capture Phase

The AppTitleBar keyboard listener uses `{ capture: true }` to listen in the **capture phase** rather than the bubbling phase. This ensures shortcuts are intercepted globally and work even when nested elements (like the book view scroller) have focus.

### Why capture phase matters:

- **Bubbling phase** (default): Event fires on target, then bubbles up to ancestors. Child listeners can prevent default before parent hears it.
- **Capture phase**: Event starts at the root and travels down to the target. Parent listeners fire before children, so global shortcuts always work first.

### Example scenario:

User is in book view, focus is on the scroller, and presses Ctrl+F:
1. Event fires at scroller element
2. AppTitleBar listener (capture phase) intercepts it first
3. AppTitleBar prevents default and opens search bar
4. Scroller never sees the event (prevented at capture phase)

### When NOT to use capture:

- Context-specific shortcuts (e.g., Ctrl+A in a search input should select text, not perform app-wide action)
- Shortcuts that should only work when specific elements are focused
- Use bubbling phase or data attributes like `data-ctrlf-enabled` to opt-in to special handling

## Keyboard Event Lifecycle

1. User presses key combination
2. Browser fires `keydown` event on window
3. `useEventListener('keydown', ...)` in AppTitleBar.vue captures it
4. Handler checks key combination and calls appropriate action
5. If backend action needed: call `action('actionName')` via bridge
6. Backend handler processes and calls `_bridge.Reply()` to return result
7. Frontend Promise resolves with result (or silently fails with `.catch(() => {})`)

All keyboard shortcuts in this app follow this flow for consistency.
