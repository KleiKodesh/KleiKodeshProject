# Layered Architecture with Feature Organization

## Goal

Complete separation: data layer is a standalone app (no Vue, no UI knowledge), dumb UI components (pure presentation), smart components connect them.

## Structure

```
src/
├── data/                        # STANDALONE DATA APP (no Vue, no UI)
│   ├── stores/                  # All Pinia stores
│   │   ├── tabStore.ts
│   │   ├── workspaceStore.ts
│   │   ├── hebrewBooksStore.ts
│   │   ├── categoryTreeStore.ts
│   │   ├── settingsStore.ts
│   │   └── connectionTypesStore.ts
│   ├── services/                # All business logic services
│   │   ├── bookLineViewerService.ts
│   │   ├── bookTocService.ts
│   │   ├── bookCommentaryService.ts
│   │   ├── hebrewBooksService.ts
│   │   ├── hebrewBooksCsvLoader.ts
│   │   ├── hebrewBooksHandlers.ts
│   │   ├── hebrewBooksHistoryService.ts
│   │   ├── hebrewBooksPdfService.ts
│   │   ├── hebrewBooksSearchService.ts
│   │   ├── pdfService.ts
│   │   ├── bloomSearchService.ts
│   │   ├── bloomSearchCacheService.ts
│   │   ├── dbService.ts
│   │   ├── dbQueries.ts
│   │   └── webviewBridge.ts
│   ├── types/                   # All TypeScript types
│   │   ├── Tab.ts
│   │   ├── Book.ts
│   │   ├── BookToc.ts
│   │   ├── HebrewBook.ts
│   │   ├── BloomSearch.ts
│   │   ├── BookCategoryTree.ts
│   │   ├── Link.ts
│   │   ├── LinkGroup.ts
│   │   ├── ConnectionType.ts
│   │   └── Topic.ts
│   └── workers/                 # Web workers
│       └── searchWorker.ts
│
├── components/                  # ALL COMPONENTS (organized by feature)
│   ├── home/
│   │   └── HomePage.vue
│   ├── workspace/
│   │   ├── WorkspacesPage.vue
│   │   ├── TabContent.vue
│   │   ├── TabControl.vue
│   │   ├── TabListDropdown.vue
│   │   ├── Titlebar.vue
│   │   └── TitlebarDropdownMenu.vue
│   ├── book/
│   │   ├── BookViewPage.vue
│   │   ├── LineView.vue
│   │   ├── Line.vue
│   │   ├── LineViewToolbar.vue
│   │   ├── TocTree.vue
│   │   ├── TocTreeNode.vue
│   │   ├── TocTreeSearch.vue
│   │   └── TocTreeView.vue
│   ├── commentary/
│   │   ├── CommentaryView.vue
│   │   ├── CommentaryContentView.vue
│   │   ├── CommentaryViewToolbar.vue
│   │   ├── CommentaryFilterPanel.vue
│   │   ├── CommentaryConnectionTypeFilter.vue
│   │   └── CommentaryCheckedTreeNode.vue
│   ├── hebrew-books/
│   │   ├── HebrewbooksPage.vue
│   │   ├── HebrewBooksViewPage.vue
│   │   └── HebrewbooksListItem.vue
│   ├── pdf/
│   │   └── PdfViewPage.vue
│   ├── kezayitdb-search/
│   │   ├── KezayitSearchPage.vue
│   │   ├── FsCheckedTree.vue
│   │   └── FsCheckedCategoryNode.vue
│   ├── kezayitdb-fs/
│   │   ├── KezayitOpenFilePage.vue
│   │   ├── FsTree.vue
│   │   ├── FsBookNode.vue
│   │   ├── FsCategoryNode.vue
│   │   └── FsTreeSearch.vue
│   ├── settings/
│   │   ├── SettingsPage.vue
│   │   ├── ThemeCreator.vue
│   │   ├── ThemeSelector.vue
│   │   ├── ThemePreviewCard.vue
│   │   ├── ThemeToggleButton.vue
│   │   ├── ThemePreviewDropdown.vue
│   │   ├── ThemePreviewPair.vue
│   │   ├── ReadingBackgroundDropdown.vue
│   │   ├── DiacriticsToggleButton.vue
│   │   └── DiacriticsDropdownItem.vue
│   └── shared/
│       ├── AppTile.vue
│       ├── UniformGrid.vue
│       ├── SplitPane.vue
│       ├── CustomDialog.vue
│       ├── CircularProgress.vue
│       └── icons/
│
├── composables/                 # Connect data to components
│   ├── home/
│   │   └── useHome.ts
│   ├── workspace/
│   │   ├── useWorkspace.ts
│   │   └── useTabs.ts
│   ├── book/
│   │   ├── useBookViewer.ts
│   │   └── useToc.ts
│   ├── commentary/
│   │   └── useCommentary.ts
│   ├── hebrew-books/
│   │   └── useHebrewBooks.ts
│   ├── pdf/
│   │   └── usePdf.ts
│   ├── kezayitdb-search/
│   │   └── useKezayitSearch.ts
│   ├── kezayitdb-fs/
│   │   └── useKezayitFs.ts
│   ├── settings/
│   │   └── useSettings.ts
│   └── shared/                  # UI utilities
│       ├── useDialog.ts
│       ├── useDropdownPosition.ts
│       ├── useKeyboardShortcuts.ts
│       ├── useListKeyboardNavigation.ts
│       ├── useScrollToElement.ts
│       ├── useVirtualizedSearch.ts
│       ├── useVirtualScrollerKeyboard.ts
│       └── useVirtualScrollerPosition.ts
│
├── utils/                       # Pure utilities
│   ├── censorDivineNames.ts
│   ├── connectivityCheck.ts
│   ├── hebrewFonts.ts
│   ├── iconify-offline.ts
│   ├── lruStorage.ts
│   ├── theme.ts
│   └── zoom.ts
│
├── config/                      # Global config
│   ├── themes.ts
│   └── readingBackgrounds.ts
│
├── assets/                      # Global assets
│
├── App.vue                      # Root component
├── main.ts                      # Entry point
└── main.css                     # Global styles
```

## Layer Rules

**data/** - Standalone data infrastructure

- ZERO Vue imports
- ZERO UI knowledge
- Could run in Node.js
- Stores, services, business logic, types, workers
- Framework-agnostic

**components/** - All Vue components (organized by feature)

- Can be dumb (props/events) or use composables
- Organized by feature for easy discovery
- Pages in components/pages/

**composables/** - Connect data to components

- Feature composables (useWorkspace, useBookViewer, etc.) - access stores/services
- Shared composables (useDialog, useDropdownPosition, etc.) - UI utilities only
- Handle all data fetching and state management
- Components just call composables

**utils/** - Pure functions

- No Vue
- No stores
- Framework-agnostic

## Import Rules

```
data/        → NOTHING (standalone)
components/  → composables/ + utils/
composables/ → data/ + utils/
utils/       → NOTHING (pure functions)
```

## Example Usage

**Component:**

```vue
<script setup>
  import { useWorkspace } from "@/composables/workspace/useWorkspace";

  const { tabs, activeTab, switchTab, closeTab } = useWorkspace();
</script>

<template>
  <div v-for="tab in tabs" @click="switchTab(tab.id)">
    {{ tab.title }}
  </div>
</template>
```

**Composable:**

```ts
// composables/workspace/useWorkspace.ts
import { useTabStore } from "@/data/stores/tabStore";

export function useWorkspace() {
  const tabStore = useTabStore();

  return {
    tabs: computed(() => tabStore.tabs),
    activeTab: computed(() => tabStore.activeTab),
    switchTab: (id) => tabStore.setActiveTab(id),
    closeTab: (id) => tabStore.closeTab(id),
  };
}
```

## Migration Steps

1. ✅ **Create folder structure** (data/, components/, composables/, utils/)
2. ✅ **Move data layer** - All stores, services, types, workers to `data/`
3. ✅ **Move components** - Organize by feature in `components/`
4. ✅ **Move common components** - `components/common/*` → `components/shared/`
5. ✅ **Create feature composables** - New composables for each feature
6. ✅ **Move existing composables** - To `composables/shared/`
7. ✅ **Update path aliases** in vite.config.ts and tsconfig.json:
   - `@/data/*` → `src/data/*`
   - `@/components/*` → `src/components/*`
   - `@/composables/*` → `src/composables/*`
   - `@/utils/*` → `src/utils/*`
   - `@/config/*` → `src/config/*`
   - `@/assets/*` → `src/assets/*`
8. 🔄 **Update all imports** - Use VS Code's auto-refactor or manual update
9. 🔄 **Update components** - Replace direct store access with composables
10. ⏳ **Validate layer boundaries** - Check no violations
11. ⏳ **Test everything works** - Run app and verify all features

## Created Composables

✅ All feature composables created:

- `composables/home/useHome.ts` - Home page workspace management
- `composables/workspace/useWorkspace.ts` - Workspace and tab management
- `composables/workspace/useTabs.ts` - Tab-specific operations (already existed)
- `composables/book/useBookViewer.ts` - Book viewing and line loading
- `composables/book/useToc.ts` - Table of contents operations
- `composables/commentary/useCommentary.ts` - Commentary loading and filtering
- `composables/hebrew-books/useHebrewBooks.ts` - Hebrew books library
- `composables/pdf/usePdf.ts` - PDF viewing
- `composables/kezayitdb-search/useKezayitSearch.ts` - Bloom filter search
- `composables/kezayitdb-fs/useKezayitFs.ts` - Category tree file system
- `composables/settings/useSettings.ts` - Application settings

## Path Alias Configuration

**vite.config.ts:**

```ts
resolve: {
  alias: {
    '@': fileURLToPath(new URL('./src', import.meta.url)),
    '@/data': fileURLToPath(new URL('./src/data', import.meta.url)),
    '@/components': fileURLToPath(new URL('./src/components', import.meta.url)),
    '@/composables': fileURLToPath(new URL('./src/composables', import.meta.url)),
    '@/utils': fileURLToPath(new URL('./src/utils', import.meta.url)),
    '@/config': fileURLToPath(new URL('./src/config', import.meta.url)),
    '@/assets': fileURLToPath(new URL('./src/assets', import.meta.url))
  }
}
```

**tsconfig.json:**

```json
"paths": {
  "@/*": ["./src/*"],
  "@/data/*": ["./src/data/*"],
  "@/components/*": ["./src/components/*"],
  "@/composables/*": ["./src/composables/*"],
  "@/utils/*": ["./src/utils/*"],
  "@/config/*": ["./src/config/*"],
  "@/assets/*": ["./src/assets/*"]
}
```

## Special Considerations

1. **App.vue and main.ts** - Stay at `src/` root (entry points)
2. **main.css** - Stays at `src/` root (global styles)
3. **Workers** - `searchWorker.ts` goes in `data/workers/`
4. **Common components** - All from `components/common/` move to `components/shared/`
5. **Icons** - Stay in `components/shared/icons/`
6. **Feature composables** - Create new ones to replace direct store access in components
7. **Existing composables** - All move to `composables/shared/` (they're UI utilities)

## Benefits

- Data layer is testable without Vue
- Data layer could be used in CLI, mobile app, etc.
- Components are simpler (just call composables)
- Composables handle all data logic
- Clear boundaries, no mixing
- Easy to test each layer independently
- No "smart vs dumb" component distinction needed

## Next Steps

### 1. Validate Layer Boundaries ✅

Validation complete:

- ✅ Stores use Vue/Pinia (acceptable - Pinia is designed for Vue)
- ✅ Type definitions in data/types (vue-virtual-scroller types are acceptable)
- ✅ Utils are pure functions (moved reactive utils to composables/shared)
- ✅ Services are mostly framework-agnostic (created pure BookLineLoader)

**Note on bookLineViewerService.ts**: The old service with Vue reactivity still exists and is used directly in LineView.vue. This is a pragmatic design for complex stateful operations. New code should use the pure `bookLineLoader.ts` + `useBookViewer` composable pattern.

Check that no layer violations exist:

```bash
# Check data layer has no Vue imports
grep -r "from 'vue'" Zayit-vue-layered/src/data/

# Check data layer has no component imports
grep -r "@/components" Zayit-vue-layered/src/data/

# Check utils has no Vue imports
grep -r "from 'vue'" Zayit-vue-layered/src/utils/

# Check utils has no store imports
grep -r "@/data/stores" Zayit-vue-layered/src/utils/
```

### 2. Update Components to Use Composables

Components should not directly import stores. Instead, they should use composables:

**Before:**

```vue
<script setup>
  import { useTabStore } from "@/data/stores/tabStore";
  const tabStore = useTabStore();
</script>
```

**After:**

```vue
<script setup>
  import { useWorkspace } from "@/composables/workspace/useWorkspace";
  const { tabs, activeTab, switchTab } = useWorkspace();
</script>
```

### 3. Refactor Components

Priority order for refactoring:

1. Page components (HomePage, WorkspacesPage, etc.)
2. Feature components (BookViewPage, CommentaryView, etc.)
3. Shared components (only if they need data access)

### 4. Test Each Feature

After refactoring each feature area, test:

- Basic functionality works
- State updates correctly
- No console errors
- Performance is acceptable

## Validation Checklist

- [ ] No Vue imports in `data/` layer
- [ ] No component imports in `data/` layer
- [ ] No Vue imports in `utils/` layer
- [ ] No store imports in `utils/` layer
- [ ] Components use composables instead of direct store access
- [ ] All imports use path aliases (@/data, @/components, etc.)
- [ ] App builds without errors
- [ ] All features work correctly
- [ ] No performance regressions

## Benefits Achieved

Once complete, the architecture provides:

1. **Testability** - Data layer can be tested without Vue
2. **Portability** - Data layer could be used in CLI, mobile app, etc.
3. **Simplicity** - Components just call composables
4. **Clarity** - Clear boundaries between layers
5. **Maintainability** - Easy to find and update code
6. **Scalability** - Easy to add new features following the pattern
