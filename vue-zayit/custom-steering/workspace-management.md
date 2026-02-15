# Workspace Management Guidelines

## Overview

The workspace system allows users to organize their work into separate sessions, with each workspace maintaining its own set of tabs and data independently.

## Architecture

### Simple Integration Approach

- **No separate workspace store** - workspace functionality is integrated directly into the existing tabStore
- **Minimal changes** - only small tweaks to tabStore to support workspace IDs
- **Backward compatibility** - existing tab functionality remains unchanged

### Storage Structure

- Each workspace has its own storage key: `tabStore_${workspaceId}`
- Workspace metadata stored separately:
  - `zayit_workspace_name_${workspaceId}` - custom workspace name
  - `zayit_workspace_created_${workspaceId}` - creation timestamp
  - `zayit_workspaces_list` - array of all workspace IDs
  - `zayit_current_workspace` - currently active workspace ID

### Default Workspace

- ID: `'default'`
- Name: `'ברירת מחדל'` (Default)
- Cannot be deleted
- Always exists as fallback

## TabStore Integration

### Navigation Helper Functions

Two helper functions eliminate code duplication across all navigation functions:

#### navigateOrCreateTab(pageType, tabData?)

Handles homepage conversion pattern - if current tab is homepage, converts it to target page type.

**Returns**: `true` if homepage was converted, `false` if caller should create new tab

**Usage**:

```typescript
if (navigateOrCreateTab('kezayit-search', { searchState: {...} })) {
    return; // Homepage was converted
}
// Otherwise create new tab
```

#### switchToExistingOrCreate(pageType)

Enforces single-instance pages - if page already exists in any tab, switches to it.

**Returns**: `true` if existing tab found and switched, `false` if caller should create new tab

**Usage**:

```typescript
if (switchToExistingOrCreate("settings")) {
  return; // Found and switched to existing tab
}
// Otherwise create new tab
```

### Tab Instance Policies

**Single-instance pages** (only one tab allowed):

- Homepage - `resetTab()` switches to existing or converts current
- Settings - `openSettings()` switches to existing or creates new
- Workspace manager - `openWorkspaceManager()` switches to existing or creates new

**Multiple-instance pages** (multiple tabs allowed):

- Search - `openKezayitSearch()` always creates new tab
- Open file dialog - `openKezayitOpenFilePage()` always creates new tab
- HebrewBooks - `openHebrewBooks()` always creates new tab
- Book views - each book opens in its own tab
- PDFs - each PDF opens in its own tab

### Key Functions

- `createWorkspace(name: string): string` - Creates new workspace, returns ID
- `deleteWorkspace(workspaceId: string)` - Deletes workspace and all its data
- `switchWorkspace(workspaceId: string)` - Switches to different workspace
- `getWorkspaceName(workspaceId: string): string` - Gets display name
- `renameWorkspace(workspaceId: string, newName: string)` - Updates name
- `openWorkspaceManager()` - Opens workspace management page (single instance)

### Workspace Switching Behavior

- **Preserves workspace manager page** - if user is on workspace manager when switching, stays on workspace manager in new workspace
- **Loads workspace data** - switches to stored tabs for the new workspace
- **Creates default tab** - if new workspace has no tabs, creates default homepage tab
- **No automatic page closing** - switching/deleting workspaces doesn't close current page

## Workspace Manager Component

### Location

- Accessible via tab header dropdown menu: "ניהול סביבות עבודה"
- Uses apps icon (`fluent:apps-28-regular`)

### Layout (Simple & Compact)

1. **Header** - "ניהול סביבות עבודה"
2. **Create form** - Input field + "צור" button at top
3. **Workspace list** - Compact items underneath

### Workspace Items

- **Single line layout** - name, item count, action buttons
- **Active highlighting** - current workspace highlighted with accent color and shadow
- **Item count display** - shows "X פריטים" (not "טאבים")
- **Inline editing** - click edit button to rename
- **Direct actions** - no confirmation dialogs

### User Experience Principles

- **Stay on page** - switching/deleting workspaces keeps user on workspace manager
- **Immediate feedback** - changes reflect immediately
- **No unnecessary complexity** - no confirmation dialogs, verbose descriptions, or complex layouts
- **Hebrew terminology** - use "פריטים" not "טאבים"

## Data Persistence

### What Gets Saved Per Workspace

- Content tabs: `bookview`, `pdfview`, `hebrewbooks-view`, `kezayit-search`
- Tab states: book positions, PDF states, search queries, scroll positions
- Next ID counter for tab generation

### What Doesn't Get Saved

- Temporary tabs: homepage, settings, workspace manager
- Search results (loaded from cache on restore)
- Virtual URLs (recreated on load)
- Search bar open/closed state

### Tab Persistence Rules

**Persisted tabs** (survive session restart):

- Book views - with position, TOC state, commentary state
- PDFs - with file path for recreation
- Hebrew books - with book state
- Search tabs - with query, scroll position, hasSearched flag

**Temporary tabs** (not persisted):

- Homepage - always recreated fresh
- Settings - single instance, not persisted
- Workspace manager - single instance, not persisted
- Open file dialog - not persisted

### Search Tab Persistence

Search tabs are fully persisted with:

- Search query text (displayed in tab title)
- Scroll position (firstVisibleItemIndex for virtual scroll)
- hasSearched flag
- Results loaded from cache on restore (memory-efficient)

### Deletion Behavior

- **Complete data removal** - all tabs, states, and metadata deleted
- **Permanent action** - no recovery possible
- **Automatic fallback** - switches to default workspace if current workspace deleted

## Implementation Guidelines

### Navigation Pattern Examples

#### Single-Instance Page (Settings, Workspaces)

```typescript
const openSettings = () => {
  // Try to convert homepage first
  if (navigateOrCreateTab("settings")) {
    return;
  }

  // Check if settings tab already exists (single instance)
  if (switchToExistingOrCreate("settings")) {
    return;
  }

  // Create new settings tab
  tabs.value.forEach((tab) => (tab.isActive = false));
  const existingIds = new Set(tabs.value.map((t) => t.id));
  let newId = 1;
  while (existingIds.has(newId)) {
    newId++;
  }

  const newTab: Tab = {
    id: newId,
    title: PAGE_TITLES.settings,
    isActive: true,
    currentPage: "settings",
  };
  tabs.value.push(newTab);
  nextId.value = Math.max(newId + 1, nextId.value);
};
```

#### Multiple-Instance Page (Search, Books, PDFs)

```typescript
const openKezayitSearch = () => {
  // Try to convert homepage first
  if (
    navigateOrCreateTab("kezayit-search", {
      searchState: {
        searchQuery: "",
        scrollPosition: 0,
        hasSearched: false,
      },
    })
  ) {
    return;
  }

  // Otherwise create new tab (allow multiple search tabs)
  tabs.value.forEach((tab) => (tab.isActive = false));
  const existingIds = new Set(tabs.value.map((t) => t.id));
  let newId = 1;
  while (existingIds.has(newId)) {
    newId++;
  }

  const newTab: Tab = {
    id: newId,
    title: PAGE_TITLES["kezayit-search"],
    isActive: true,
    currentPage: "kezayit-search",
    searchState: {
      searchQuery: "",
      scrollPosition: 0,
      hasSearched: false,
    },
  };

  tabs.value.push(newTab);
  nextId.value = Math.max(newId + 1, nextId.value);
};
```

### Adding Workspace Support to New Features

1. Check if feature needs workspace isolation
2. If yes, use workspace-specific storage keys
3. Ensure data loads/saves per workspace
4. Test workspace switching behavior
5. Decide if page should be single-instance or multiple-instance
6. Use appropriate helper functions (`navigateOrCreateTab`, `switchToExistingOrCreate`)

### Storage Key Patterns

```typescript
// Workspace-specific data
const storageKey = `featureName_${currentWorkspaceId.value}`;

// Global data (not workspace-specific)
const storageKey = "globalFeatureName";
```

### Workspace Manager Integration

- Add to tab header dropdown menu
- Use appropriate Hebrew terminology
- Keep UI simple and compact
- Ensure actions don't close the page

## Testing Checklist

- [ ] Create new workspace
- [ ] Switch between workspaces
- [ ] Rename workspace
- [ ] Delete workspace (non-default)
- [ ] Workspace manager stays open during operations
- [ ] Tab data persists per workspace
- [ ] Search tabs persist with queries
- [ ] Multiple search tabs can be open
- [ ] Search results load from cache on restore
- [ ] Default workspace cannot be deleted
- [ ] Item counts display correctly
- [ ] Hebrew text uses "פריטים"
- [ ] Single-instance pages (settings, workspaces) switch to existing
- [ ] Multiple-instance pages (search, books, PDFs) create new tabs

## Common Pitfalls

1. **Don't overcomplicate** - keep workspace system simple
2. **Don't add confirmation dialogs** - direct actions preferred
3. **Don't use separate workspace store** - integrate with tabStore
4. **Don't close workspace manager** - preserve user's current page
5. **Don't use "טאבים"** - use "פריטים" in Hebrew
6. **Don't make complex layouts** - keep workspace items compact
