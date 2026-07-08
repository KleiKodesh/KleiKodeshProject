# home

Home page with navigation tiles and a unified quick-search bar.

**HomePage.vue** — grid of navigation tiles plus the hero search bar (wide-screen only, hidden below 600px container width). The tile list must always be kept in sync with the destination list in `AppTitleBarNavDropdown.vue` — when adding, removing, or renaming a destination, update both files.

**HomePageTile.vue** — single tile with a filled colored icon and label. Navigates via `useAppNavigation` on tap. Add new tiles in `HomePage.vue` using this component.

**homeDateInfo.ts** — loads today's Hebrew date and Daf Yomi for the bottom date bar.

**dafYomiNavigation.ts** — navigates to the Daf Yomi book and line when the user taps the Daf Yomi entry in the date bar.

**useHomeSearch.ts** — unified search composable. Takes the search query ref and fans it out across three sources simultaneously: book catalog (instant, title-only, in-memory), HebrewBooks catalog (debounced 300ms, async via C# bridge), and Document Locator file system search (debounced 300ms, async via C# bridge). HebrewBooks and file search only fire when `isHosted`. Each source writes to its own result ref so the dropdown renders partial results as they arrive. Each source is capped at 5 results. Minimum query length is 2 characters.

**HomeSearchDropdown.vue** — results dropdown rendered below the search bar. Groups results into three sections (ספרים, היברו-בוקס, קבצים) with section headers and loading spinners. Shows only sections that have results or are loading. Emits `selectCatalogBook`, `selectHebrewBook`, `selectFile` — all navigation is handled by `HomePage.vue`, not the dropdown.

## Search bar wiring in HomePage.vue

The search bar wrapper (`searchBarRef`) is the `useDropdownClose` target — clicking anywhere outside the wrapper closes the dropdown. The dropdown opens on `@input` when the query is ≥ 2 chars, and on `@focus` when results already exist. Async results (HB, files) trigger a `watch` on their result refs to open the dropdown once they resolve. `closeSearchDropdown` clears results and resets the query.

## Tile visibility rules

The first two tiles are DB-dependent and swap based on DB state. All other tiles are always visible regardless of DB state.

When `isHosted && !dbReady`: the first two tiles are **הורד מסד ספרים** and **בחר מסד ספרים**, which let the user set up a database.

When DB is available (or not hosted): the first two tiles are **ספרים** and **חיפוש**, which require a DB to function.

Never hide or conditionally render any tile beyond these first two — the rest (פתח קובץ, היברו-בוקס, חיפוש קבצים, מילון, לוח שנה, מידות ושיעורים, סביבות עבודה, הגדרות) are always shown.
