# Component Relationship Map

This document maps parent-child relationships and component-composable connections in the Vue app.

## Book Feature

```
BookViewPage.vue (page)
├── uses: useBookView.ts, useBookViewer.ts
├── LineView.vue (main view)
│   ├── uses: useLineViewScroll.ts, useLineViewSelection.ts, useLineViewCopy.ts
│   ├── uses: useLineViewEvents.ts, useLineViewContextMenu.ts
│   ├── uses: useLineViewVirtualItems.ts, useLineViewCenterObserver.ts
│   ├── Line.vue (child - individual line)
│   └── LineViewToolbar.vue (child - toolbar)
│       └── uses: useLineViewToolbar.ts, useLineViewToolbarPosition.ts
└── TocTreePanel.vue (TOC panel)
    ├── uses: useToc.ts
    ├── TocTree.vue (tree container)
    │   └── TocTreeNode.vue (recursive node)
    └── TocTreeSearch.vue (search within TOC)
```

## Commentary Feature

```
CommentaryView.vue (main view)
├── uses: useCommentaryView.ts, useCommentary.ts
├── uses: useCommentaryLoader.ts, useCommentaryNavigation.ts
├── CommentaryToolbar.vue (child - toolbar)
├── CommentaryContent.vue (child - content display)
│   └── uses: useCommentaryContent.ts, useCommentarySearch.ts
└── CommentaryFilterPanel.vue (child - filter UI)
    ├── uses: useCommentaryFilters.ts
    ├── CommentaryConnectionTypeFilter.vue (child - connection filter)
    └── CommentaryCheckedTreeNode.vue (child - tree node)
```

## Hebrew Books Feature

```
HebrewBooksPage.vue (page - list view)
├── uses: useHebrewBooks.ts
└── HebrewBooksListItem.vue (child - list item)

HebrewBookViewPage.vue (page - single book view)
└── uses: useHebrewBooks.ts
```

## Settings Feature

```
SettingsPage.vue (page)
├── uses: useSettingsPage.ts, useSettings.ts
├── GeneralSettingsTab.vue (child - tab)
│   └── uses: useGeneralSettingsTab.ts
└── ReadingSettingsTab.vue (child - tab)
    ├── uses: useReadingSettingsTab.ts, useFontSelection.ts
    ├── FontSelector.vue (child - font picker)
    ├── DiacriticsToggleButton.vue (child - diacritics control)
    │   └── DiacriticsDropdownItem.vue (child - dropdown item)
    ├── ThemeToggleButton.vue (child - theme control)
    ├── ThemeSelector.vue (child - theme picker)
    │   └── ThemePreviewDropdown.vue (child - dropdown)
    │       └── ThemePreviewPair.vue (child - preview item)
    │           └── ThemePreviewCard.vue (child - card)
    ├── ThemeCreator.vue (child - theme builder)
    │   └── uses: useThemeBuilder.ts
    ├── ReadingBackgroundDropdown.vue (child - background picker)
    ├── PdfFilterToggleButton.vue (child - PDF filter)
    └── SliderSetting.vue (child - slider control)
```

## Workspace Feature

```
App.vue (root)
└── Titlebar.vue (top bar)
    ├── uses: useTabs.ts, useWorkspace.ts
    ├── TabControl.vue (child - tab bar)
    │   ├── uses: useTabs.ts
    │   └── TabListDropdown.vue (child - tab overflow)
    │       └── uses: useTabListDropdown.ts
    ├── TitlebarDropdownMenu.vue (child - menu)
    │   └── uses: useTitlebarDropdown.ts
    └── TabContent.vue (child - content area)

WorkspacesPage.vue (page - workspace manager)
└── uses: useWorkspace.ts, useWorkspaceEditor.ts
```

## Zayit DB Filesystem Feature

```
ZayitOpenFilePage.vue (page)
├── uses: useZayitFs.ts
├── FsTree.vue (tree container)
│   ├── FsCategoryNode.vue (child - category node)
│   └── FsBookNode.vue (child - book node)
└── FsTreeSearch.vue (search within tree)
```

## Zayit DB Search Feature

```
ZayitSearchPage.vue (page)
├── uses: useZayitSearchPage.ts, useZayitSearch.ts, useBloomSearch.ts
├── SearchBar.vue (child - search input)
├── FsCheckedTree.vue (child - filter tree)
│   └── FsCheckedCategoryNode.vue (child - checkbox node)
└── SearchResultsList.vue (child - results)
    └── uses: useSearchResultsList.ts
```

## PDF Feature

```
PdfViewPage.vue (page)
└── uses: usePdf.ts
```

## Home Feature

```
HomePage.vue (page)
├── uses: useHome.ts
└── AppTile.vue (child - app launcher tile)
```

## Shared Components (Reusable)

```
Combobox.vue → uses: useCombobox.ts
ContextMenu.vue → uses: useContextMenu.ts
CustomDialog.vue → uses: useCustomDialog.ts, useDialog.ts
GenericSearch.vue → uses: useGenericSearch.ts, useVirtualizedSearch.ts
SplitPane.vue → uses: useSplitPane.ts
UniformGrid.vue → uses: useUniformGrid.ts
CircularProgress.vue (no composable)
LoadingSpinner.vue (no composable)
```

## Shared Composables (Utility)

These are used across multiple components:

- `useKeyboardShortcuts.ts` - keyboard handling
- `useScrollToElement.ts` - scroll utilities
- `useZoom.ts` - zoom functionality
- `useDropdownPosition.ts` - dropdown positioning
- `useListKeyboardNavigation.ts` - list navigation
- `useVirtualScrollerKeyboard.ts` - virtual scroller keyboard
- `useVirtualScrollerPosition.ts` - virtual scroller positioning
- `useConnectivity.ts` - network status

## Naming Issues Identified

### Unclear Parent-Child Relationships

1. `TocTree.vue` vs `TocTreeView.vue` - which is the parent?
2. `CommentaryView.vue` vs `CommentaryContentView.vue` - redundant "View"
3. `HebrewbooksPage.vue` vs `HebrewBooksViewPage.vue` - inconsistent naming

### Composable Confusion

1. `useBookViewPage.ts` - redundant "Page" suffix
2. `useLineViewToolbarActions.ts` - too specific, could be `useLineViewToolbar.ts`
3. Multiple aspect composables for `LineView` - good pattern but could be clearer

### Recommended Renames

#### High Priority

- `TocTreeView.vue` → `TocTreePanel.vue` (clearer distinction)
- `CommentaryContentView.vue` → `CommentaryContent.vue` (remove redundant View)
- `useBookViewPage.ts` → `useBookView.ts` (remove redundant Page)
- `HebrewBooksViewPage.vue` → `HebrewBookViewPage.vue` (singular consistency)

#### Medium Priority

- `useLineViewToolbarActions.ts` → `useLineViewToolbar.ts` (simpler)
- `useToolbarPosition.ts` → `useLineViewToolbarPosition.ts` (clearer scope)

## Best Practices Going Forward

1. **Page components**: Always end with `Page` (e.g., `BookViewPage.vue`)
2. **Child components**: Use parent name as prefix (e.g., `BookViewToolbar.vue`)
3. **Component composables**: Match component name (e.g., `LineView.vue` → `useLineView.ts`)
4. **Aspect composables**: Use component + aspect (e.g., `useLineViewScroll.ts`)
5. **Feature composables**: Use feature name (e.g., `useBookViewer.ts`)
6. **Shared components**: Descriptive names without feature prefix
7. **Collocation**: Keep components and composables together in feature folders
