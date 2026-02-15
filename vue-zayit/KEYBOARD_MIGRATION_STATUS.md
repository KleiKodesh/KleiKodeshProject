# Keyboard Hooks Migration Status

## Status: Migration Complete ✅

All keyboard navigation has been successfully migrated from custom `KeyboardNavigator` class to VueUse's `useEventListener` with proper `event.code` handling.

### Critical Discovery: Hebrew Keyboard Layout Issue

**Problem**: `useMagicKeys` doesn't work with non-English keyboard layouts because it checks `event.key` (the character produced) instead of `event.code` (the physical key position).

**Example**: On Hebrew keyboard layout:

- Pressing the 'A' key produces 'ש' (shin character)
- `useMagicKeys` checks for `keys['Ctrl+A']` which looks for Ctrl+'a' character
- This fails because the key produces 'ש', not 'a'

**Solution**: Use `useEventListener` with `event.code`:

- `event.code` represents physical key position (always 'KeyA')
- `event.key` represents character produced (varies by layout)
- Check `event.code === 'KeyA'` instead of `event.key === 'a'`

### Implementation Pattern

```typescript
import { useEventListener, useFocus } from "@vueuse/core";

const containerRef = ref<HTMLElement>();
const { focused: hasFocus } = useFocus(containerRef);

useEventListener("keydown", (event: KeyboardEvent) => {
  if (!hasFocus.value) return;

  const hasCtrlOrMeta = event.ctrlKey || event.metaKey;

  // Use event.code for keyboard layout independence
  if (hasCtrlOrMeta && event.code === "KeyF") {
    event.preventDefault();
    openSearch();
  }

  if (hasCtrlOrMeta && event.code === "KeyA") {
    event.preventDefault();
    selectAll();
  }
});
```

### Completed Migrations

#### Migrated Components (Using useEventListener + event.code)

1. ✅ `App.vue` - Ctrl+W (KeyW), Ctrl+X (KeyX)
2. ✅ `BookLineViewer.vue` - Ctrl+F (KeyF), Ctrl+A (KeyA) with focus check + custom copy handler
3. ✅ `BookCommentaryView.vue` - Ctrl+F (KeyF), Ctrl+A (KeyA) with focus check + custom copy handler
4. ✅ `CustomDialog.vue` - Enter, Escape
5. ✅ `Combobox.vue` - Arrow keys, Enter, Escape
6. ✅ `BookTocTreeView.vue` - Escape
7. ✅ `BookTree.vue` - Arrow navigation with useListKeyboardNavigation
8. ✅ `BookTreeSearch.vue` - Arrow navigation with useListKeyboardNavigation
9. ✅ `BookTocTree.vue` - Arrow navigation with useListKeyboardNavigation
10. ✅ `BookTocTreeSearch.vue` - Arrow navigation with useListKeyboardNavigation
11. ✅ `HebrewbooksPage.vue` - Arrow navigation with useListKeyboardNavigation, Tab/Arrow from search input

#### Custom Copy Handler for Virtual Scrollers

Both `BookLineViewer` and `BookCommentaryView` implement custom copy handlers to solve the virtual scroller limitation:

**Problem**: Virtual scrollers only render visible items, so browser's default "Select All" only selects visible content.

**Solution**:

1. Ctrl+A selects all visible content in the container
2. Custom copy event handler intercepts the copy operation
3. Copies ALL source content (not just visible items) as both HTML and plain text
4. Uses `useEventListener` for automatic cleanup

```typescript
// Handle copy event to copy full source content
function handleCopy(event: ClipboardEvent) {
  const selection = window.getSelection();
  if (!selection || selection.rangeCount === 0) return;

  // Check if selection is within our container
  const containerEl = containerRef.value;
  if (!containerEl) return;

  const range = selection.getRangeAt(0);
  if (!containerEl.contains(range.commonAncestorContainer)) return;

  // Copy ALL source content (not just visible virtual items)
  let htmlContent = "";
  let textContent = "";

  // ... build content from source data ...

  event.clipboardData?.setData("text/html", htmlContent);
  event.clipboardData?.setData("text/plain", textContent);
  event.preventDefault();
}

// Set up copy event listener with automatic cleanup
useEventListener(containerRef, "copy", handleCopy);
```

#### Global preventDefault

`main.ts` has global preventDefault for Ctrl+F only:

- Uses `event.code === 'KeyF'` for keyboard layout independence
- Uses `capture: true, passive: false` to prevent browser search immediately
- Ctrl+A is handled by individual components (not globally) for proper focus checking

### Remaining Components

#### Simple Template Handlers (No Migration Needed)

These use simple element-specific handlers that work correctly:

- `BookTocTreeNode.vue` - @keydown.enter, @keydown.space
- `BookTreeNode.vue` - @keydown.enter
- `BookTreeCategoryNode.vue` - @keydown.enter, @keydown.space
- `WorkspaceManager.vue` - @keydown.enter, @keydown.esc
- `GenericSearch.vue` - @keydown.enter, @keydown.shift.enter, @keydown.esc
- `SearchPage.vue` - @keydown.enter, @keydown.esc
- `KezayitOpenFilePage.vue` - @keydown handler
- `HebrewbooksPage.vue` - @keydown.tab
- `HebrewbooksListItem.vue` - @keydown.enter

#### Composables (Now Using useEventListener)

- `useListKeyboardNavigation.ts` - Migrated from useMagicKeys to useEventListener for all keyboard handling (Arrow keys, Tab, Escape)
- `useKeyboardShortcuts.ts` - Optional helper (not actively used, still has useMagicKeys but not critical)

**Migration**: The `useListKeyboardNavigation` composable now uses `useEventListener` directly on the container element, eliminating the need for `@keydown="handleKeyDown"` in templates. This provides:

- Automatic event listener setup and cleanup
- Consistent keyboard handling across all tree/list components
- Tab key support to return focus to parent (search input)
- Arrow key navigation that works immediately without template handlers

### Legacy Code Cleanup

✅ **Completed**

- `KeyboardNavigator.ts` - Deleted (replaced by useEventListener approach)
- All imports of KeyboardNavigator - Removed from all components
- Manual event listeners in migrated components - Replaced with useEventListener
- `useMagicKeys` for letter-based shortcuts - Replaced with useEventListener + event.code

### Benefits of Migration

1. **Keyboard Layout Independence** - Works with Hebrew, Russian, Arabic, and any other keyboard layout
2. **Automatic cleanup** - `useEventListener` handles cleanup automatically
3. **Focus-based activation** - Each component only responds when it has focus
4. **Type-safe** - Better TypeScript support with VueUse
5. **Cleaner codebase** - Removed legacy class-based approach
6. **Custom copy handler** - Solves virtual scroller limitation for Ctrl+A → Ctrl+C workflow

### Summary

The keyboard handling infrastructure has been successfully migrated to use VueUse's `useEventListener` with `event.code` for keyboard layout independence. All letter-based shortcuts (Ctrl+F, Ctrl+A, Ctrl+W, Ctrl+X) now work correctly on any keyboard layout. Virtual scroller components implement custom copy handlers to ensure "Select All" copies the complete source content, not just visible items.

### Key Takeaways

- **Always use `event.code`** for letter-based keyboard shortcuts
- **Use `useFocus`** to check if component has focus before responding
- **Use `useEventListener`** from VueUse for automatic cleanup
- **`useMagicKeys` is fine** for arrow keys and special keys (they don't vary by layout)
- **Custom copy handlers** are needed for virtual scrollers to copy all content
