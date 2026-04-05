# src/host

Database access and C# host bridge. Everything that communicates outside the Vue app lives here. Nothing outside this folder should call `fetch` against the DB or invoke C# actions directly.

**db.ts** — database access layer. Import `query<T>(sql, params)` to run SQL, `isHosted` to check if running inside the C# host, `dbReady` to reactively gate DB-dependent UI, and `onWebviewEvent(fn)` to subscribe to C# push events. Queries route through the C# host in production and the Vite dev middleware in development — callers do not need to know which.

**bridge.ts** — C# host actions for file operations: `pickFile()`, `restoreLocalPdf`, `restoreHbPdf`, `disposePdfHost`. All have dev-mode fallbacks. Import from here for any native file system interaction.

**queries.sql.ts** — all raw SQL strings in the app. Every new SQL query must be added here as a named constant. No inline SQL anywhere else in the codebase.
