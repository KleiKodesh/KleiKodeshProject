# home

Home page with navigation tiles.

**HomePage.vue** — grid of navigation tiles. The tile list must always be kept in sync with the destination list in `AppTitleBarNavDropdown.vue` — when adding, removing, or renaming a destination, update both files.

**HomePageTile.vue** — single tile with a filled colored icon and label. Navigates via `useAppNavigation` on tap. Add new tiles in `HomePage.vue` using this component.

**useHomeDateInfo.ts** — loads today's Hebrew date and Daf Yomi for the bottom date bar.

**useDafYomiNavigation.ts** — navigates to the Daf Yomi book and line when the user taps the Daf Yomi entry in the date bar.

## Tile visibility rules

The first two tiles are DB-dependent and swap based on DB state. All other tiles are always visible regardless of DB state.

When `isHosted && !dbReady`: the first two tiles are **התקן כתבי הקודש** and **בחר מסד נתונים**, which let the user set up a database.

When DB is available (or not hosted): the first two tiles are **ספרים** and **חיפוש**, which require a DB to function.

Never hide or conditionally render any tile beyond these first two — the rest (פתח קובץ, היברו-בוקס, פתח קיוויקס, מילון, לוח שנה, מידות ושיעורים, סביבות עבודה, הגדרות) are always shown.
