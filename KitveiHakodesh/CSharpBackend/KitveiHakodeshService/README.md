# KitveiHakodeshService — .NET Background Service Host

A .NET 10 HTTP API service that exposes the KitveiHakodesh backend over localhost. Serves SQL queries, full-text search, dictionary lookups, HebrewBooks catalogue, and document location via HTTP + Server-Sent Events. The Vue frontend connects to this service instead of the WebView2 message bridge.

All domain folders are scaffolded with empty stub classes ready for implementation. The HTTP server layer lives in `Server/`.

## Folder Structure

```
KitveiHakodeshService/
├── Program.cs                      — ASP.NET Core host bootstrap; configures Kestrel, routes, and CORS
├── Worker.cs                       — BackgroundService entry point (unused in Web SDK — routes registered in Program.cs)
├── appsettings.json                — Logging configuration (base)
├── appsettings.Development.json    — Logging configuration (development overrides)
├── Properties/
│   └── launchSettings.json         — Launch profile; sets DOTNET_ENVIRONMENT=Development
├── Server/                         — HTTP server scaffolding
│   ├── SseManager.cs               — Manages Server-Sent Events connections and push event routing
│   └── ApiEndpoints.cs             — All route registrations (minimal APIs)
├── Seforim/
│   ├── DbManager.cs                — Opens and queries the seforim SQLite database via Dapper
│   ├── Catalog.cs                  — Book catalog queries (categories, books, TOC entries)
│   ├── FullTextSearch.cs           — FTS index lifecycle and search execution via Lucene.Net
│   └── Service.cs                  — HTTP route handlers for all seforim endpoints
├── Dictionary/
│   └── DbManager.cs                — Opens and queries KitveiHakodesh_dictionary.db
├── HebrewBooks/
│   ├── DbManager.cs                — Opens and queries HebrewBooks.db (local catalogue)
│   └── Service.cs                  — HTTP route handlers for HebrewBooks catalogue and download
└── DocumentLocator/
    └── Service.cs                  — HTTP route handlers that proxy to the DocumentLocator named-pipe service
```

## Runtime Stack

- Target framework: .NET 10, `Microsoft.NET.Sdk.Web`
- SQLite access: `Microsoft.Data.Sqlite` + `Dapper`
- Full-text search: `Lucene.Net` 4.8 with `Lucene.Net.Analysis.Common` and `Lucene.Net.QueryParser`
- HTTP server: ASP.NET Core Kestrel with minimal APIs

## Databases

Both SQLite databases are embedded as `Content` items (copied to output with `PreserveNewest`):

| File | Location at runtime | Managed by |
| ---- | ------------------- | ---------- |
| `KitveiHakodesh_dictionary.db` | `Dictionary/` | `Dictionary.DbManager` |
| `HebrewBooks.db` | `HebrewBooks/` | `HebrewBooks.DbManager` |

The seforim database is user-supplied — its path is provided at runtime via configuration (to be wired in `Seforim.DbManager`).

## API Surface

### SQL queries — two databases

`POST /query` — seforim database  
`POST /query-dict` — dictionary database

Request: `{ sql: string, params: any[] }`  
Response: `{ rows: object[] }`

### Request/response actions

| Endpoint | Method | Description |
|---|---|---|
| `/search/start` | POST | Start a full-text search, returns `{ searchId }` |
| `/search/cancel` | POST | Cancel a running search by `searchId` |
| `/search/progress` | GET | Poll current FTS index build state |
| `/search/reset` | POST | Wipe and rebuild the FTS index |
| `/hebrewbooks/search` | POST | Search the HebrewBooks catalogue DB |
| `/documentlocator/search` | POST | Search via DocumentLocator named-pipe service |

### Push events (server → client)

`GET /events` — Server-Sent Events stream

The client opens this endpoint once and keeps the connection alive. The service writes events down the stream whenever they occur:

| Event | Trigger |
|---|---|
| `searchBatch` | Chunk of FTS results (keyed by `searchId`) |
| `searchComplete` | FTS stream done |
| `searchCancelled` | FTS search cancelled |
| `searchError` | FTS error |
| `ftsIndexProgress` | Index build tick |
| `ftsIndexInvalidated` | Index corrupt/missing, rebuild started |
| `fileSystemIndexingStatus` | DocumentLocator ready/indexing state |

## Domain Modules

### Seforim

Owns everything related to the main seforim database: opening and pooling the connection (`DbManager`), book catalog queries (`Catalog`), and full-text search index build and query (`FullTextSearch`). `Service` exposes these as HTTP endpoints consumed by the Vue frontend.

The full-text search implementation here uses Lucene.Net rather than the custom FtsLib engine used in the WinForms host. The query surface presented to the Vue app should remain identical — the frontend should not need to know which engine is backing it.

### Dictionary

Wraps read-only access to `KitveiHakodesh_dictionary.db`. `DbManager` is the only file here; endpoint registration will live in the Seforim service or a dedicated route file once the HTTP server layer is built.

### HebrewBooks

`DbManager` queries the local HebrewBooks catalogue database. `Service` handles HTTP routes for catalogue browsing and PDF download initiation. Unlike the WinForms host (which intercepts downloads via WebView2's `DownloadStarting` event), this service must initiate downloads server-side and stream or cache the result.

### DocumentLocator

Proxies document location requests to the `DocumentLocator` Windows service via its named-pipe protocol. The `DocumentLocator` project (`CSharpBackend/DocumentLocator/`) owns the pipe server; this module owns the pipe client side adapted for HTTP.

### Server

ASP.NET Core minimal-API setup: route registration, CORS policy for localhost frontend, and SSE channel management. `SseManager` tracks all open SSE connections and broadcasts push events to subscribed clients. `ApiEndpoints` registers all routes in `Program.cs`.

## Intended Startup Sequence

1. `Program.cs` builds and starts the Kestrel HTTP server.
2. Configure CORS to allow `http://localhost:5173` (Vite dev server) or any other frontend origin.
3. Register all routes via `Server.ApiEndpoints`.
4. Initialise `Seforim.DbManager` with the configured seforim database path (from `appsettings.json` or environment variable).
5. Trigger `Seforim.FullTextSearch` index build in the background if no valid index exists.
6. Listen on `http://localhost:5000` (or configured port).

## Relationship to KitveiHakodeshLib

`KitveiHakodeshLib` serves the same feature set but is tightly coupled to WebView2 and the VSTO Word add-in. `KitveiHakodeshService` is the standalone, process-isolated equivalent — same domain logic, different host boundary. When implementation matures, shared SQLite and search logic should be extracted into a common library rather than duplicated between the two.
