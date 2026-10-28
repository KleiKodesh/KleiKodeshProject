# KitveiHakodesh.Core

The single home for all non-UI logic. The service is transport only; Core owns everything.

```
     vue-frontend (hosted)                    vue-frontend (browser dev)
  posts to http://kitvei-service             vite proxy /khs-endpoint
             |                                        |
   KitveiHakodeshLib (net48)                          |
   Bridge/ServiceRpcRelay                             |
             |  named pipe                            |  loopback HTTP (HttpHostServer)
             +------------> KitveiHakodeshService <---+
                            (net10.0-windows, Native AOT, Dispatcher)
                                       |
                             KitveiHakodesh.Core (all logic)
                                       |
                              FtsLib, DocConvertLib
```

`KitveiHakodeshLib` has **no project reference to Core**. It is a net48 WebView2 host that
relays the frontend's RPC calls to the service over a named pipe (`ServiceRpcRelay`). The
service is the only Core consumer at runtime and adds nothing but transport. Any host must be
able to do everything the service does through Core alone.

Below Core sits **FtsLib**, the generic full-text engine. Core feeds it; it never opens the
corpus.

---

## Two constraints that govern everything here

### ⚠ The Service ships as NATIVE AOT

`KitveiHakodeshService` sets `PublishAot=true` and references this project, so Core is
compiled by ILC. **No runtime code generation, no reflection-driven
behaviour** — which in practice means **no Dapper** (raw ADO only), source-generated
serialization, and no `Activator.CreateInstance` / `Expression.Compile` / `Emit`. Core has
no other leg to put them on.

### ⚠ MessagePack is the only wire format

Core is MessagePack-native; its models are serialized directly by both transports, with no
mapping layer. **Never decode MessagePack and re-encode it as JSON** (or the reverse) — if a
payload changes format between hops, that is a bug. JSON is kept only where a human reads it:
config, hand-edited data files, logs, diagnostics.

---

## Why this project exists

The same logic existed twice — once in `KitveiHakodeshLib` (net48, hosted) and once in
`KitveiHakodeshService` (net10, dev) — and the copies drifted. Verified duplicates included:

- three separate seforim-DB readers (`ZayitDb`, `DbAccess`, `SeforimDbService`)
- two cross-process index locks using **different primitives**, so neither was authoritative
- two placeholder rewriters, two `IsValidUtf8`, two niqqud normalizers, two DB-path resolvers
- three copies of the HebrewBooks catalog DB, two of `Dictionary.db`

Core exists so each of those is written once.

---

## Targets

`net10.0-windows` only. Not plain `net10.0`, because registry access and Office COM need
Windows. There is no net48 leg: the Word VSTO add-in reaches Core only through the service.

`../KitveiHakodesh.Core.Portable` compiles a subset of Core's own source files (seforim DB
reader, FTS wrapper, catalog) on plain `net10.0` for cross-platform tools; it adds no source
of its own. Windows-only calls in those files sit behind `OperatingSystem.IsWindows()`.

---

## Layout

```
Common/               reusable, knows nothing about this app. FLAT — no subfolders;
                      the file names already say what each one is. Holds AppFileLocator,
                      which is how Core finds its own files (see below)
Settings/             registry-backed app settings + seforim DB path resolution
SeforimDb/            SQL strings, queries, models — the ONE reader for seforim.db
SeforimLibraryDownload/  downloads Otzaria's library when none is installed, resumably, and
                      keeps that copy current with update patches (the ONE writer of seforim.db)
SeforimDbFullTextSearch/   feeds and searches the FTS index (engine logic is FtsLib's)
SeforimDbCatalog/     Lucene TOC index over the same corpus
Dictionary/  HebrewBooks/
UserAnnotations/      highlights + notes — user CONTENT, the only write path here.
                      (Preferences are Settings/; the DB file keeps its old
                      user_settings.db name because existing installs hold real data.)
Dictionary/ HebrewBooks/  Dictionary.db, HebrewBooksCatalog.db — ONE copy of each, ever (Content in Core csproj)
```

`Common/` holds genuinely reusable code — file location, SQLite connections, update
checking, file fingerprinting, Office COM, font enumeration, environment probes. Nothing in
it may know the KitveiHakodesh app exists.

There is **no `CoreOptions`** and no injected path bag. Core finds its own files through
`Common/AppFileLocator`, which probes candidate roots in order and takes the first that
exists, falling back to the installer's `%LocalAppData%\KleiKodesh`. This is not a
preference: the service keeps data beside its binary, the VSTO add-in is shadow-copied so its
own location is a temp folder, and the **portable** DemoApp runs from a path that changes per
run and may be read-only. Probing answers all three. Reading and writing are separate
questions — `ResolveWritablePath` *tests* writability rather than assuming it.

Exceptions are **specific types living beside the code that throws them** — no `Exceptions/`
folder (that groups by kind) and never one catch-all `CoreException`.

---

## Working here

Read `CLAUDE.md` in this folder before changing anything — it carries the full rule set
(AOT, MessagePack, naming, layering). The migration plan and its slice-by-slice sequence
live in `../MIGRATION-PLAN.md`.
