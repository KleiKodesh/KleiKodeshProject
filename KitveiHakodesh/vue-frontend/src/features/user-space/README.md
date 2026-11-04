# user-space

UserSpace (אזור אישי): the user's own catalog, built from folders they add - catalog categories and folders on the computer.

**Only the roots are stored** (`stores/userSpaceStore.ts`, IDB `app-user-space`). Everything under a root is read when opened: a catalog root is the catalog's own category node (booksDataStore already holds the whole catalog); a computer root is filled one level at a time from the service (`listLocalFolder` → Core `LocalFolderListing`, openable files only).

**UserSpacePage.vue** - the catalog page's shape: `BookCatalogTitleBar` and the four catalog views (list / cards / tiles / tree), with its own view setting (`settingsStore.userSpaceView`). Add buttons sit in the bar's `actions` slot; remove shows while the path is inside a root (flat views).

**useUserSpace.ts** - builds the catalog-shaped tree (computer folders are `CategoryNode`s with `files`), the browse path, and lazy loading (`ensureLoaded`, called on enter and on the tree's `expandFolder`).

**UserSpaceCatalogPicker.vue** - dialog that walks the catalog's folders and returns the chosen one.

**useUserSpaceSearch.ts** - one `userSpaceSearch` call (Core UserSpaceSearch): the catalog index filtered to the root categories (category-ancestor field) and the file index filtered to the root folders (path prefix), merged and ranked by the catalog search's own rules - a file ranks like a book title, the user's root order is the last tiebreak. Results render in the catalog's own BookCatalogSearch.

Computer roots are limited to folders file search covers (`checkUserSpaceFolder`): local NTFS drives, not excluded, not a skipped system folder.
