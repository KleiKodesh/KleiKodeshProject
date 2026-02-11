# Popout Functionality

## Overview

The popout feature allows users to move the Zayit viewer from its host container (VSTO task pane or main form) into a separate floating window. This is particularly useful in VSTO scenarios where the task pane can be restrictive.

## Architecture

### Component Hierarchy

```
ZayitViewerHost (UserControl)
  └── ZayitViewer (WebView2)
      └── Vue Application
```

### Key Design Principle

**The popout logic is self-contained within `ZayitViewerHost`**. This allows the host control to manage its own visibility state, which is critical for VSTO task pane integration where the task pane visibility is bound to the host control's visibility.

## Implementation Details

### Location

- **File**: `Zayit-cs/ZayitLib/Viewer/ZayitViewerHost.cs`
- **Trigger**: Vue application calls `webviewBridge.call('TogglePopOut')` from the menu

### State Management

```csharp
private ZayitViewer _zayitViewer;
private Form _popoutWindow;
private bool _isPopoutActive = false;
```

### Flow Diagram

#### Popping Out

1. User clicks "הצג בחלונית" in Vue menu
2. Vue calls `webviewBridge.call('TogglePopOut')`
3. C# ServiceProvider routes to `ZayitViewerHost.TogglePopOut()`
4. `MoveViewerToPopout()` executes:
   - Removes `_zayitViewer` from host control
   - Sets `this.Visible = false` (hides VSTO task pane)
   - Creates new `Form` window
   - Adds `_zayitViewer` to popup window
   - Shows popup window

#### Popping Back In

Three ways to trigger:

1. **User clicks button again**: Same flow, calls `MoveViewerToHost()`
2. **User closes popup window**: `PopoutWindow_FormClosing` event handler
3. **Host becomes visible externally**: `VisibleChanged` event handler

All three paths execute similar logic:

- Remove `_zayitViewer` from popup window
- Close and dispose popup window
- Add `_zayitViewer` back to host control
- Set `this.Visible = true` (shows VSTO task pane)

## Critical Implementation Details

### 1. Visibility Binding for VSTO

The host control's `Visible` property controls the VSTO task pane visibility:

```csharp
// When popping out
this.Visible = false;  // Hides task pane

// When popping back in
this.Visible = true;   // Shows task pane
```

### 2. Automatic Pop-In on Visibility Change

If external code or binding makes the host control visible while popped out, it automatically pops back in:

```csharp
private void ZayitViewerHost_VisibleChanged(object sender, EventArgs e)
{
    if (this.Visible && _isPopoutActive)
    {
        MoveViewerToHost();
    }
}
```

This prevents the scenario where the task pane is visible but empty.

### 3. Window Closing Behavior

When the popup window closes, the viewer must return to the host:

```csharp
private void PopoutWindow_FormClosing(object sender, FormClosingEventArgs e)
{
    if (_isPopoutActive)
    {
        // Unsubscribe to prevent recursion
        _popoutWindow.FormClosing -= PopoutWindow_FormClosing;

        // Move viewer back
        _popoutWindow.Controls.Remove(_zayitViewer);
        this.Controls.Add(_zayitViewer);
        this.Visible = true;

        // Let window close naturally (no e.Cancel)
    }
}
```

**Important**: Do NOT use `e.Cancel = true` here, as it prevents the window from closing. Instead, move the viewer first, then let the window close.

### 4. Control Lifecycle

The `ZayitViewer` control is never destroyed during popout - it's just moved between containers:

```csharp
// Remove from one parent
parentA.Controls.Remove(_zayitViewer);

// Add to another parent
parentB.Controls.Add(_zayitViewer);
```

This preserves the WebView2 state, including the loaded Vue application and all its data.

## Vue Integration

### Menu Button

Location: `zayit-vue/src/components/TabHeaderMenu.vue`

```vue
<div
  v-if="isWebViewAvailable"
  @click.stop="handlePopoutClick"
  class="flex-row flex-center-start hover-bg c-pointer dropdown-item"
>
  <Icon icon="fluent:open-28-regular" />
  <span class="dropdown-label">הצג בחלונית</span>
</div>
```

### Handler

```typescript
const handlePopoutClick = async () => {
  if (isWebViewAvailable.value) {
    try {
      const { webviewBridge } = await import("../services/webviewBridge");
      await webviewBridge.call("TogglePopOut");
    } catch (error) {
      console.error("[TabHeaderMenu] Failed to toggle popout:", error);
    }
  }
  closeDropdown();
};
```

### Bridge Method

Location: `Zayit-cs/ZayitLib/Services/ServiceProvider.cs`

```csharp
public void TogglePopOut() => _popOutAction?.Invoke();
```

The `_popOutAction` is set by `ZayitViewer` during initialization:

```csharp
_zayitViewer.SetPopOutToggleAction(TogglePopOut);
```

## VSTO Integration

In VSTO projects, the task pane visibility should be bound to the host control:

```csharp
// Example VSTO setup (not in current codebase)
var taskPane = CustomTaskPanes.Add(new ZayitViewerHost(), "Zayit");
taskPane.VisibleChanged += (s, e) => {
    // Sync visibility if needed
};
```

The host control manages its own visibility, so the task pane automatically shows/hides based on popout state.

## Standalone Application

In standalone applications (like `ZayitWrapper`), the popout works the same way, but the host control visibility doesn't affect anything else:

```csharp
// MainForm.cs - Simple setup
_zayitViewerHost = new ZayitViewerHost();
_zayitViewerHost.Dock = DockStyle.Fill;
this.Controls.Add(_zayitViewerHost);
```

No additional wiring needed - the host control handles everything internally.

## Cleanup and Disposal

The host control properly cleans up the popup window on disposal:

```csharp
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        if (_isPopoutActive && _popoutWindow != null)
        {
            _popoutWindow.FormClosing -= PopoutWindow_FormClosing;
            _popoutWindow.Close();
            _popoutWindow.Dispose();
        }
        this.VisibleChanged -= ZayitViewerHost_VisibleChanged;
    }
    base.Dispose(disposing);
}
```

## Testing Scenarios

1. **Basic popout/pop-in**: Click button twice
2. **Close popup window**: Verify viewer returns to host
3. **External visibility change**: Set `host.Visible = true` while popped out
4. **Dispose while popped out**: Verify no exceptions or leaks
5. **Multiple rapid toggles**: Ensure state remains consistent

## Common Issues and Solutions

### Issue: Popup window doesn't close

**Cause**: Using `e.Cancel = true` in `FormClosing` event
**Solution**: Remove `e.Cancel`, move viewer first, then let window close

### Issue: Task pane is visible but empty

**Cause**: Host control became visible while popped out
**Solution**: Already handled by `VisibleChanged` event handler

### Issue: WebView2 state is lost

**Cause**: Creating new `ZayitViewer` instead of moving existing one
**Solution**: Always move the same control instance between containers

### Issue: Memory leak with popup windows

**Cause**: Not unsubscribing from events or disposing window
**Solution**: Always unsubscribe in `FormClosing` and dispose in `Dispose()`
