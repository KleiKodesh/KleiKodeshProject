# src/components

Shared reusable components used across multiple features. Only add a component here if it is used by two or more features.

**TreeView.vue / TreeNode.vue** — generic recursive tree with expand/collapse. Use this for any new tree UI rather than building a custom one.

**treeTypes.ts** — TypeScript interface `TreeNodeItem` (id, parentId, level, hasChildren, text) used by `TreeView.vue` and consumers.

**SplitPane.vue** — resizable split pane. Divider is 1px visually with a 20px touch target via `::before`. Use this for any resizable two-panel layout.

**BottomSearchBar.vue** — compact search bar for use at the bottom of panels.

**FloatingSearchBar.vue** (`common/`) — floating search bar that overlays content. Supports match navigation, mode toggle, and auto-focus.

**ContextMenu.vue** — right-click / long-press context menu.

**ConfirmDialog.vue** — modal confirmation dialog. Use this for any destructive action confirmation.

**AlertDialog.vue** — modal alert dialog with a single confirm button. Use this for informational messages that require acknowledgment.

**LoadingAnimation.vue** — loading spinner. Use this for any async loading state.

**HintIcon.vue** — small info icon with a tooltip on hover. Pass the hint string as a prop.

**IconEverythingSearch.vue** — custom SVG icon for the everything-search feature.

**IconTreeRtl.vue** — `IconTextBulletListTree` pre-flipped for RTL (`transform: scaleX(-1)`). Always use this wrapper instead of the raw icon — the Fluent icon is LTR-designed and always needs the flip in this app.
