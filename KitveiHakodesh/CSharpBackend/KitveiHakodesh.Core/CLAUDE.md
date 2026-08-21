# KitveiHakodesh.Core — rules for working in this project

Read this before writing a line of code here. The full migration plan is
`../MIGRATION-PLAN.md`; this file is the short version of what binds *this* project.

---

## ⚠ 1. The Service ships as NATIVE AOT

`KitveiHakodeshService` sets `PublishAot=true` and references this project, so **everything
in Core's `net10.0-windows` leg is compiled by ILC**. No runtime IL generation, no
reflection-driven behaviour.

- **NO DAPPER.** It emits materializers as IL at runtime — impossible under native AOT.
  Use raw ADO: `CreateCommand` / `ExecuteReader`.
- **No** `Activator.CreateInstance`, `Type.GetType`, `Expression.Compile`, `MakeGenericType`,
  `Assembly.Load`, `dynamic`, or `Emit`.
- Serialization must be **source-generated** — MessagePack attributes on the type;
  `JsonSerializerContext` if JSON is ever unavoidable.
- Anything that genuinely needs the above goes on the **net48 leg only**, excluded from
  net10 with `<Compile Remove>` (e.g. Office Interop).
- Keep `IsAotCompatible` on the net10 leg so warnings surface here, not at publish time.

## ⚠ 2. MessagePack is the only wire format

Core is **MessagePack-native**. Models carry `[MessagePackObject(keyAsPropertyName: true)]`
and are serialized directly by both transports — there is no mapping layer.

**Never decode MessagePack and re-encode the same payload as JSON**, or the reverse. It is
invisible in a diff and costs a full serialize+parse per hop. If a payload changes format
between two hops, that is a bug.

JSON is allowed only where a human reads it: config, hand-edited data files, logs,
diagnostics. Wire = MessagePack.

## 3. Core knows nothing about its callers

No `using KitveiHakodeshService.*`, no `KitveiHakodeshLib` types, no transport types, no
`WebBridge`, no HTTP, no WebView2.

- **All config is injected** (`CoreOptions`) — Core reads **no environment variables** and
  resolves no host state.
- **No UI.** No `MessageBox`, no dialogs, no windows. Return data.
- **Return data or throw** — never swallow an error into `Debug.WriteLine` (a no-op in
  Release). The orchestrator decides what the user sees.
- Core's public surface is set by **production callers, never by tests**.

## 4. The logic lives in FtsLib; Core supplies data and calls it

FtsLib must never open `seforim.db`. Equally, **Core must not re-implement or wrap engine
algorithms** — search, ranking, snippets, merging, index state are FtsLib's.

The recurring trap is *engine algorithm + one corpus query*, which looks like Core logic and
is not. Split at that seam: the query is Core's, the algorithm is FtsLib's.

## 5. Naming

- Would **any** developer know what this file is at a glance, without opening it?
- Name the **full subject**: `SeforimDb`, never `Seforim`.
- Use the **conventional** word — `Models`, `Options`, `Factory`, `Provider`, `Resources`.
  Never invent taxonomy.
- **No `*Service` suffix** — Core is a library, not a service.
- **Split by JOB, not by noun.** *Stamps, version, policy, state, probe* are usually
  bookkeeping owned by a job, not jobs themselves.
- Queries live in `<Subject>DbQueries.cs`. SQL gets its **own** file
  (`<Subject>DbSqlStrings.cs`) only when it would bury the code around it — today that is
  **seforim.db alone** (379 lines of it). Everywhere else: `const`s at the top of the
  queries file, so the SQL sits beside its caller.

## 6. Both legs, always

`<TargetFrameworks>net48;net10.0-windows</TargetFrameworks>`.
net48 is **non-negotiable** — the Word VSTO add-in only supports it. Every change must
compile on both legs, and the net48 consumers (VSTO, `KitveiHakodeshDemoApp`) must still
resolve.

`InvariantGlobalization=true` is set on the Service leg only, so **always specify
`Ordinal`/`Invariant` explicitly** — never rely on culture-sensitive defaults
(`StartsWith(string)`, `EndsWith(string)`, `IndexOf(string)`, `string.Compare`).

## 7. One copy of everything

One `Dictionary.db`, one `HebrewBooksCatalog.db`, in `Resources/`. No second copy anywhere, no
"keep them identical" rule. If a consumer seems to need its own copy, that is a delivery
problem to solve, not a copy to create.

## 8. Encoding

UTF-8 **without BOM**. Never use PowerShell `Get-Content`/`Set-Content` on source files —
it silently corrupts Hebrew. Several files here are dense with Hebrew source text; check
before bulk-editing.
