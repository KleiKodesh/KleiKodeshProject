---
inclusion: fileMatch
fileMatchPattern: '**/BookCommentaryView.vue|**/CommentaryConnectionTypeFilter.vue|**/commentaryService*|**/connectionTypesStore*'
---

# Zayit Commentary Connection-Type Filter

## CRITICAL: Single Persistence System - Filter-Based Only

The commentary system now uses **ONLY ONE persistence mechanism**:
- **Filter-based persistence**: Each filter maintains its own position state
- **Legacy system REMOVED**: No more conflicting targetBookId preservation for line navigation

**Why This Approach**: Eliminates conflicts between filter switching and line navigation, provides clean separation of concerns, ensures reliable position restoration.

## Position Persistence Architecture

### Single Source of Truth
```typescript
// BookState interface - ONLY filter-based persistence
export interface BookState {
    commentaryFilterConnectionTypeId?: number; // Selected filter
    commentaryPositionsByFilter?: Record<string, { 
        groupIndex: number; 
        targetBookId?: number; 
        scrollPosition: number;
        groupName?: string;
    }>; // Position per filter
    // REMOVED: commentaryGroupIndex, commentaryTargetBookId (legacy)
}
```

### Filter Key System
```typescript
function getFilterKey(connectionTypeId: number | undefined): string {
    return connectionTypeId !== undefined ? `filter_${connectionTypeId}` : 'show_all'
}

// Examples:
// filter_3 (מפרשים)
// filter_4 (תרגומים) 
// filter_5 (קשרים)
// show_all (הצג הכל)
```

### Position Save/Restore Flow
```typescript
// Save position when filter changes or user scrolls/navigates
function savePositionForCurrentFilter() {
    const filterKey = getFilterKey(selectedConnectionTypeId.value)
    const currentGroup = linkGroups.value[currentGroupIndex.value]
    
    activeTab.bookState.commentaryPositionsByFilter[filterKey] = {
        groupIndex: currentGroupIndex.value,
        targetBookId: currentGroup?.targetBookId,
        scrollPosition: commentaryContentRef.value.scrollTop,
        groupName: currentGroup?.groupName
    }
}

// Restore position when loading commentary for current filter
function restorePositionForCurrentFilter(): boolean {
    const filterKey = getFilterKey(selectedConnectionTypeId.value)
    const savedPosition = activeTab.bookState.commentaryPositionsByFilter[filterKey]
    
    if (!savedPosition) return false
    
    // Try targetBookId first, then groupName for הצג הכל, then groupIndex
    // Multiple fallback strategies for reliable restoration
    return restoreByTargetBookId() || restoreByGroupName() || restoreByIndex()
}
```

## Commentary Service Architecture

### commentaryService.ts - Centralized Commentary Logic
```typescript
export class CommentaryService {
    // Get available filter options based on book flags
    getAvailableFilterOptions(book: Book): Array<{ label: string; value: number | undefined; isDefault?: boolean }>
    
    // Get smart default filter (prefers COMMENTARY/מפרשים)
    getDefaultFilter(book: Book): number | undefined
    
    // Load commentary with SQL-level filtering
    async loadCommentaryLinks(bookId: number, lineIndex: number, tabId: string, filterOptions?: CommentaryFilterOptions)
    
    // Check if filter should be shown (multiple connection types)
    shouldShowFilter(book: Book): boolean
}
```

### Key Features
- **SQL filtering**: `connectionTypeId` passed directly to database query
- **Smart defaults**: Automatically selects מפרשים (COMMENTARY) when available
- **Centralized logic**: All commentary operations in one service
- **Performance optimized**: No JavaScript filtering of large datasets

## Filter Component Architecture

### CommentaryConnectionTypeFilter.vue
- **Toggle button design**: Filter icon only (no chevron arrow)
- **Hebrew labels**: All UI text in Hebrew (הצג הכל, מקור, מפרשים, etc.)
- **Context-aware**: Only shows connection types that exist for current book
- **Compact design**: 24px height, fits in commentary titlebar

### Book Flag + Service Mapping
```typescript
// Book flags → Service logic → Hebrew labels + defaults
hasCommentaryConnection: 1 → commentaryService.getAvailableFilterOptions() → 'מפרשים' (DEFAULT)
hasSourceConnection: 1     → commentaryService.getAvailableFilterOptions() → 'מקור'
hasTargumConnection: 1     → commentaryService.getAvailableFilterOptions() → 'תרגומים'
hasReferenceConnection: 1  → commentaryService.getAvailableFilterOptions() → 'קשרים'
hasOtherConnection: 1      → commentaryService.getAvailableFilterOptions() → 'אחר'
```

### Default Filter Logic
```typescript
// commentaryService.ts
getDefaultFilter(book: Book): number | undefined {
    const options = this.getAvailableFilterOptions(book)
    const defaultOption = options.find(opt => opt.isDefault && opt.value !== undefined)
    return defaultOption?.value // Returns COMMENTARY ID if available
}
```

### Available Options Logic
```typescript
// commentaryService.ts
getAvailableFilterOptions(book: Book): Array<{ label: string; value: number | undefined; isDefault?: boolean }> {
    const options = [{ label: 'הצג הכל', value: undefined }]
    
    const connectionTypeMap = [
        { flag: book.hasCommentaryConnection, name: 'COMMENTARY', isDefault: true }, // Default to מפרשים
        { flag: book.hasSourceConnection, name: 'SOURCE' },
        { flag: book.hasTargumConnection, name: 'TARGUM' },
        { flag: book.hasReferenceConnection, name: 'REFERENCE' },
        { flag: book.hasOtherConnection, name: 'OTHER' }
    ]

    connectionTypeMap.forEach(({ flag, name, isDefault }) => {
        if (flag > 0) {
            const connectionTypeId = connectionTypesStore.getConnectionTypeId(name)
            if (connectionTypeId) {
                options.push({
                    label: connectionTypesStore.getHebrewLabel(name),
                    value: connectionTypeId,
                    isDefault: isDefault || false
                })
            }
        }
    })
    
    return options
}
```

## App Initialization

### main.ts Integration
```typescript
// Initialize connection types on app startup
import { useConnectionTypesStore } from './stores/connectionTypesStore'

const connectionTypesStore = useConnectionTypesStore()
connectionTypesStore.loadConnectionTypes()
```

### Store Structure
```typescript
// connectionTypesStore.ts
export const useConnectionTypesStore = defineStore('connectionTypes', () => {
    const connectionTypes = ref<ConnectionType[]>([])
    const isLoaded = ref(false)
    
    // Hebrew labels (static UI translations)
    const hebrewLabels: Record<string, string> = {
        'SOURCE': 'מקור',
        'OTHER': 'אחר',
        'COMMENTARY': 'מפרשים',
        'TARGUM': 'תרגומים',
        'REFERENCE': 'קשרים'
    }
    
    const loadConnectionTypes = async () => {
        connectionTypes.value = await dbManager.getConnectionTypes()
        isLoaded.value = true
    }
    
    return { connectionTypes, isLoaded, loadConnectionTypes, getHebrewLabel, getConnectionTypeId }
})
```

## Database Layer Changes

### SQL Query Enhancement (Fixed for Dev Server)
```typescript
// sqlQueries.ts: getLinks now returns parameterized query object
getLinks: (lineId: number, connectionTypeId?: number) => ({
  query: `
    SELECT ... FROM link l
    WHERE l.sourceLineId = ?
    ${connectionTypeId !== undefined ? 'AND l.connectionTypeId = ?' : ''}
    ORDER BY bk.title
  `,
  params: connectionTypeId !== undefined ? [lineId, connectionTypeId] : [lineId]
})
```

### Database Manager Update
```typescript
// dbManager.ts: Handles both dev server and C# bridge
async getLinks(lineId: number, tabId: string, bookId: number, connectionTypeId?: number) {
    if (this.isWebViewAvailable()) {
        const queryObj = SqlQueries.getLinks(lineId, connectionTypeId)
        // Send query string to C# bridge
        this.csharp.send('GetLinks', [lineId, tabId, bookId, queryObj.query])
    } else if (this.isDevServerAvailable()) {
        // Use parameterized query for dev server
        links = await sqliteDb.getLinks(lineId, connectionTypeId)
    }
}
```

## Integration Points

### BookCommentaryView Integration
- Uses `commentaryService.loadCommentaryLinks()` with SQL-level filtering
- Smart default filter selection via `commentaryService.getDefaultFilter()`
- Filter state managed in component with automatic defaults
- **ONLY filter-based persistence** - no legacy targetBookId logic

### CommentaryConnectionTypeFilter Integration
- Uses `commentaryService.getAvailableFilterOptions()` for dropdown options
- Uses `commentaryService.shouldShowFilter()` to determine visibility
- Shows default indicator (●) for מפרשים option
- Only displays when multiple connection types exist

### BookViewPage Data Flow
- Current book passed from `categoryTreeStore.allBooks`
- Book data flows: CategoryTreeStore → BookViewPage → BookCommentaryView → Filter Service

## UI/UX Patterns

### Filter Button Design
```vue
<button class="filter-toggle-btn" @click="toggleDropdown">
    <Icon icon="fluent:filter-28-regular" class="filter-icon" />
</button>
```

### Dropdown Behavior
- Opens on button click
- Closes on selection or outside click
- Right-to-left layout for Hebrew
- Selected option highlighted

## Filter Persistence Per Tab - WORKING ✅

### ✅ **Confirmed Working Behavior:**

1. **Filter Switching**: 
   - User switches from Filter A to Filter B
   - Position saved for Filter A before switch
   - Position restored for Filter B after loading
   - Each filter maintains independent state

2. **Position Restoration**:
   - Uses targetBookId matching for most reliable restoration
   - Falls back to group index if targetBookId doesn't match
   - Restores exact scroll position within commentary group

3. **Session Persistence**:
   - Filter selection survives app restart through localStorage
   - Position data persists across sessions via tab store
   - Each tab maintains independent filter state

### 🔧 **Performance Optimizations:**

- **Throttled scroll saving**: Position saving during scroll throttled to 100ms
- **Reduced logging**: Essential position tracking only
- **Clean separation**: No interference between filter switching and line navigation

### 🎯 **User Experience:**

- **Seamless filter switching**: Users can switch between filters and return to exact position
- **Reliable restoration**: Uses targetBookId matching for most reliable position restoration
- **Fallback strategy**: Falls back to group index if targetBookId doesn't match
- **Session persistence**: Positions survive app restarts through localStorage

```typescript
// BookCommentaryView.vue - Simplified persistence implementation
async function loadCommentaryLinks(bookId: number, lineIndex: number) {
    linkGroups.value = await commentaryService.loadCommentaryLinks(...)

    // ONLY filter-based persistence - no other logic
    const restored = restorePositionForCurrentFilter()
    if (restored) {
        return
    }

    // Default to first group if no saved position
    currentGroupIndex.value = 0
}

const handleFilterChange = async (connectionTypeId: number | undefined) => {
    // Save current position before changing filter
    savePositionForCurrentFilter()
    
    selectedConnectionTypeId.value = connectionTypeId
    await loadCommentaryLinks(props.bookId, props.selectedLineIndex)
}
```

### Persistence Behavior
- **Per-tab memory**: Each tab remembers its own filter selection and positions
- **Session persistence**: Filter selection and positions survive app restart via localStorage
- **Default state**: `undefined` means "show all" (הצג הכל)
- **Automatic saving**: Changes saved immediately to tab store
- **Position persistence**: Each filter remembers user's scroll position and commentary group
- **Filter switching**: User can switch between filters and return to exact position
- **No conflicts**: Filter switching completely separate from line navigation

## Performance Considerations

### No Extra Database Queries
- Uses existing book flags from category tree store
- No need to query connection_type table for availability
- Filter options computed client-side using store data

### Existing Caching Preserved
- Works with current commentary caching system
- **Legacy persistence REMOVED** - no more conflicting systems
- Doesn't break scroll position or group selection

### Navigation Consistency
- All navigation logic uses `processedLinkGroups.value` for consistency
- Prevents combobox from becoming empty during scroll
- Ensures bounds checking matches displayed content

## Hebrew Labels and Constants

### ConnectionType.ts Structure
```typescript
export interface ConnectionType {
    id: number
    name: string
}

export const SHOW_ALL_LABEL = 'הצג הכל'
```

**Note**: No hardcoded constants - all connection type data comes from the database via the store.

## Common Implementation Patterns

### ✅ Correct Service Usage
```vue
<!-- CommentaryConnectionTypeFilter.vue -->
<template>
    <div class="connection-type-filter" v-if="shouldShowFilter">
        <!-- Filter UI with default indicator -->
        <div class="filter-option" :class="{ 'default': option.isDefault }">
            {{ option.label }}
            <span v-if="option.isDefault" class="default-indicator">●</span>
        </div>
    </div>
</template>

<script setup>
import { commentaryService } from '../services/commentaryService'

const availableOptions = computed(() => {
    if (!props.book) return []
    return commentaryService.getAvailableFilterOptions(props.book)
})

const shouldShowFilter = computed(() => {
    if (!props.book) return false
    return commentaryService.shouldShowFilter(props.book)
})
</script>
```

### ✅ Smart Default Filter Implementation
```typescript
// BookCommentaryView.vue - Smart default selection
const selectedConnectionTypeId = computed({
    get: () => {
        const saved = tabStore.activeTab?.bookState?.commentaryFilterConnectionTypeId
        // If no saved filter and we have a book, use the default filter
        if (saved === undefined && props.book) {
            const defaultFilter = commentaryService.getDefaultFilter(props.book)
            if (defaultFilter !== undefined) {
                // Set the default filter in the tab state
                const activeTab = tabStore.activeTab
                if (activeTab?.bookState) {
                    activeTab.bookState.commentaryFilterConnectionTypeId = defaultFilter
                }
                return defaultFilter
            }
        }
        return saved
    },
    set: (value: number | undefined) => {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.commentaryFilterConnectionTypeId = value
        }
    }
})
```

### ✅ SQL-Level Filtering
```typescript
// commentaryService.ts - Filtering happens in database
async loadCommentaryLinks(bookId: number, lineIndex: number, tabId: string, filterOptions?: CommentaryFilterOptions) {
    const lineId = await dbManager.getLineId(bookId, lineIndex)
    
    // SQL filtering - no JavaScript processing of large datasets
    const connectionTypeId = filterOptions?.connectionTypeId
    const links = await dbManager.getLinks(lineId, tabId, bookId, connectionTypeId)
    
    // Only grouping logic in JavaScript
    return this.groupLinksByTitle(links)
}
```

## Troubleshooting

### Filter Not Working in Development Mode
**Symptom**: Console shows "508 total links vs 508 filtered links" for all connection types

**Root Cause**: Vite dev server plugin expects parameterized queries, but SQL was built with string interpolation

**Solution**: Use proper parameterized queries in `sqlQueries.ts`:
```typescript
getLinks: (lineId: number, connectionTypeId?: number) => ({
  query: `
    SELECT ... FROM link l
    WHERE l.sourceLineId = ?
    ${connectionTypeId !== undefined ? 'AND l.connectionTypeId = ?' : ''}
    ORDER BY bk.title
  `,
  params: connectionTypeId !== undefined ? [lineId, connectionTypeId] : [lineId]
})
```

**Files to Update**:
- `sqlQueries.ts`: Change getLinks to return object with query and params
- `sqliteDb.ts`: Handle the new query structure
- `dbManager.ts`: Extract query from the returned object for C# bridge

### Position Persistence Issues
**Symptom**: Filter switching doesn't restore correct position

**Root Cause**: Conflicting persistence systems interfering with each other

**Solution**: **FIXED** - Legacy persistence system completely removed:
- ❌ **REMOVED**: `saveGroupIndexToTab()` function
- ❌ **REMOVED**: `commentaryGroupIndex` and `commentaryTargetBookId` from BookState
- ❌ **REMOVED**: All calls to legacy persistence functions
- ✅ **ONLY**: Filter-based persistence using `commentaryPositionsByFilter`

### Filter Not Showing
- Check if `book` prop is passed to CommentaryConnectionTypeFilter
- Verify book has multiple connection types (filter only shows when > 1 option)
- Ensure book flags are properly loaded from categoryTreeStore

### Hebrew Labels Missing
- Import CONNECTION_TYPE_LABELS from types/ConnectionType
- Check SHOW_ALL_LABEL constant is used for default option
- Verify dropdown direction is set to RTL

## Backward Compatibility

- All existing commentary functionality preserved
- Filter is optional - when no filter selected, shows all links
- Existing C# bridge communication patterns unchanged
- **Legacy persistence REMOVED** - cleaner, conflict-free system
- No breaking changes to existing components

## Complete Feature Architecture

### Files Created/Modified
- `commentaryService.ts` - **NEW**: Centralized commentary service with SQL filtering
- `connectionTypesStore.ts` - Enhanced with default connection type support
- `CommentaryConnectionTypeFilter.vue` - Updated to use service + show defaults
- `BookCommentaryView.vue` - **CLEANED**: Uses service + smart defaults + ONLY filter-based persistence
- `BookViewPage.vue` - Book data flow to filter component (unchanged)
- `dbManager.ts` - Enhanced to support filtering in both dev and production (unchanged)
- `sqlQueries.ts` - Fixed parameterized queries for dev server compatibility (unchanged)
- `sqliteDb.ts` - Updated to handle new query structure (unchanged)
- `ConnectionType.ts` - Removed hardcoded constants, kept interface (unchanged)
- `Tab.ts` - **CLEANED**: Removed legacy fields, kept only commentaryFilterConnectionTypeId and commentaryPositionsByFilter
- `main.ts` - Added connection types store initialization (unchanged)
- `commentaryManager.ts` - **DELETED**: Replaced by commentaryService.ts

### Key Architectural Decisions
1. **Centralized service** instead of scattered commentary logic
2. **SQL-level filtering** instead of JavaScript filtering
3. **Smart defaults** (מפרשים preferred) instead of "show all"
4. **One-time loading** of connection type meanings on app startup
5. **Service-based architecture** for better maintainability and testing
6. **Single persistence system** - ONLY filter-based persistence, no conflicts
7. **Clean separation** - Filter switching completely independent of line navigation

## Troubleshooting

### Combobox Becomes Empty During Scroll
**Symptom**: Commentary navigation combobox becomes empty when scrolling in certain filtered views (קשרים, תרגומים)

**Root Cause**: Scroll handler using `linkGroups.value` instead of `processedLinkGroups.value` for bounds checking

**Solution**: Ensure all navigation logic uses `processedLinkGroups.value`:
```typescript
// ❌ Wrong: Using raw linkGroups
if (linkGroups.value.length === 0) return
if (currentGroupIndex.value < linkGroups.value.length - 1)

// ✅ Correct: Using processed groups
if (processedLinkGroups.value.length === 0) return  
if (currentGroupIndex.value < processedLinkGroups.value.length - 1)
```

**Files to Check**: BookCommentaryView.vue scroll handlers and navigation functions

### Filter Showing Wrong Content
**Symptom**: Selecting תרגומים shows קשרים content and vice versa

**Root Cause**: Using hardcoded CONNECTION_TYPE_IDS instead of database-loaded values

**Solution**: Use connectionTypesStore with database-loaded connection types:
```typescript
// ❌ Wrong: Hardcoded constants
const targumId = CONNECTION_TYPE_IDS.TARGUM

// ✅ Correct: Database-loaded values
const targumId = connectionTypesStore.getConnectionTypeId('TARGUM')
```

### Connection Types Not Loading
**Symptom**: Filter dropdown is empty or shows no options

**Root Cause**: Connection types store not initialized or not loaded

**Solution**: Ensure store is loaded before using:
```typescript
// Check if store is loaded
if (!connectionTypesStore.isLoaded) {
    await connectionTypesStore.loadConnectionTypes()
}
```

### Hebrew Labels Missing
**Symptom**: Filter shows English names instead of Hebrew

**Root Cause**: Not using store's getHebrewLabel method

**Solution**: Use store method for Hebrew labels:
```typescript
// ❌ Wrong: Direct database name
label: connectionType.name

// ✅ Correct: Hebrew label from store
label: connectionTypesStore.getHebrewLabel(connectionType.name)
```