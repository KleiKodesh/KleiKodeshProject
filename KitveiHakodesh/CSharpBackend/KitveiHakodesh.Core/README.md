# KitveiHakodesh.Core

The single home for all non-UI logic shared by the two orchestrators.

```
                    KitveiHakodesh.Core
              (all logic — net48 + net10.0-windows)
                    |                    |
          KitveiHakodeshLib      KitveiHakodeshService
        (direct WebView2 access)    (HTTP/IPC to dev)
                    |                    |
                    +---- vue-frontend --+
                       ONE typed API surface
```

`KitveiHakodeshLib` and `KitveiHakodeshService` are **thin orchestrators**. They must behave
identically; the only permitted difference is transport — Lib talks straight to WebView2,
dev goes through the service.

Below Core sits **FtsLib**, the generic full-text engine. Core feeds it; it never opens the
corpus.

---

## Two constraints that govern everything here

### ⚠ The Service ships as NATIVE AOT

`KitveiHakodeshService` sets `PublishAot=true` and references this project, so Core's
`net10.0-windows` leg is compiled by ILC. **No runtime code generation, no reflection-driven
behaviour** — which in practice means **no Dapper** (raw ADO only), source-generated
serialization, and no `Activator.CreateInstance` / `Expression.Compile` / `Emit`. Anything
needing those lives on the net48 leg only.

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

`net48` **and** `net10.0-windows`.

net48 is non-negotiable: the Word VSTO add-in only supports it, and it consumes Core through
`KitveiHakodeshLib`. `net10.0-windows` (not plain `net10.0`) because `DocumentLocator`'s net10
leg is `-windows` and registry access needs Windows.

Per-leg differences use `<Compile Remove>` or `#if NET10_0_OR_GREATER`, following the
`DocumentLocator` / `DocConvertLib` pattern already established in this repo.

---

## Layout

```
Common/               reusable, knows nothing about this app. FLAT — no subfolders;
                      the file names already say what each one is. Holds AppFileLocator,
                      which is how Core finds its own files (see below)
Settings/             registry-backed app settings + seforim DB path resolution
SeforimDb/            SQL strings, queries, models — the ONE reader for seforim.db
SeforimDbFullTextSearch/   feeds and searches the FTS index (engine logic is FtsLib's)
SeforimDbCatalog/     Lucene TOC index over the same corpus
Dictionary/  HebrewBooks/
UserAnnotations/      highlights + notes — user CONTENT, the only write path here.
                      (Preferences are Settings/; the DB file keeps its old
                      user_settings.db name because existing installs hold real data.)
Resources/            Dictionary.db, HebrewBooksCatalog.db — ONE copy of each, ever
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
