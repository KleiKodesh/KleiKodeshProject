# common

Shared reusable components used across multiple features. Only add a component here if it is used by two or more features.

**TreeView.vue / TreeNode.vue** — generic recursive tree with expand/collapse. Use this for any new tree UI rather than building a custom one.

**SplitPane.vue** — resizable split pane. Divider is 1px visually with a 20px touch target via `::before`. Use this for any resizable two-panel layout.

**BottomSearchBar.vue** — compact search bar for use at the bottom of panels.

**ContextMenu.vue** — right-click / long-press context menu.

**ConfirmDialog.vue** — modal confirmation dialog. Use this for any destructive action confirmation.

**LoadingAnimation.vue** — loading spinner. Use this for any async loading state.

**IconTreeRtl.vue** — `IconTextBulletListTree` pre-flipped for RTL (`transform: scaleX(-1)`). Always use this wrapper instead of the raw icon — the Fluent icon is LTR-designed and always needs the flip in this app.
