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

### New Properties

```typescript
const currentWorkspaceId = ref<string>(DEFAULT_WORKSPACE_ID);
const workspaces = ref<string[]>([DEFAULT_WORKSPACE_ID]);
```

### Key Functions

- `createWorkspace(name: string): string` - Creates new workspace, returns ID
- `deleteWorkspace(workspaceId: string)` - Deletes workspace and all its data
- `switchWorkspace(workspaceId: string)` - Switches to different workspace
- `getWorkspaceName(workspaceId: string): string` - Gets display name
- `renameWorkspace(workspaceId: string, newName: string)` - Updates name
- `openWorkspaceManager()` - Opens workspace management page

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

- Content tabs only: `bookview`, `pdfview`, `hebrewbooks-view`
- Tab states: book positions, PDF states, etc.
- Next ID counter for tab generation

### What Doesn't Get Saved

- Temporary tabs: homepage, settings, workspace manager
- Search states (cleaned on save)
- Virtual URLs (recreated on load)

### Deletion Behavior

- **Complete data removal** - all tabs, states, and metadata deleted
- **Permanent action** - no recovery possible
- **Automatic fallback** - switches to default workspace if current workspace deleted

## Implementation Guidelines

### Adding Workspace Support to New Features

1. Check if feature needs workspace isolation
2. If yes, use workspace-specific storage keys
3. Ensure data loads/saves per workspace
4. Test workspace switching behavior

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
- [ ] Default workspace cannot be deleted
- [ ] Item counts display correctly
- [ ] Hebrew text uses "פריטים"

## Common Pitfalls

1. **Don't overcomplicate** - keep workspace system simple
2. **Don't add confirmation dialogs** - direct actions preferred
3. **Don't use separate workspace store** - integrate with tabStore
4. **Don't close workspace manager** - preserve user's current page
5. **Don't use "טאבים"** - use "פריטים" in Hebrew
6. **Don't make complex layouts** - keep workspace items compact
