# Web Workers in This Project

## Build Constraint

This project uses `vite-plugin-singlefile` with `inlineDynamicImports: true` in Rollup output options. The entire app — all JS, CSS, assets — is bundled into a single `index.html` file for distribution as a self-contained WebView app.

## Why Standard Web Workers Don't Work

Web Workers require a separate script file (or a blob URL derived from one). Vite supports two worker modes:

- **`?worker`** — emits the worker as a separate `.js` file. Incompatible with single-file output — the file won't exist at runtime.
- **`?worker&inline`** — bundles the worker code and delivers it as a base64 blob URL string inlined into the main bundle. This is the intended solution for single-file builds.

However, `inlineDynamicImports: true` (set by `vite-plugin-singlefile`) treats the entire app as a single Rollup entry point. Workers are separate entry points by nature. Rollup cannot inline dynamic imports **and** handle multiple entry points simultaneously — these are mutually exclusive constraints. The build fails.

`vite-plugin-singlefile`'s own documentation explicitly lists "Worklets" as unsupported, and Web Workers share the same constraint.

## What Was Tried / Considered

| Approach | Verdict |
|---|---|
| `?worker` (separate file) | Fails — file doesn't exist in single-file output |
| `?worker&inline` (base64 blob) | Fails at build — conflicts with `inlineDynamicImports: true` |
| Comlink + `?worker&inline` | Same build failure as above |
| `scheduler.yield` / `setTimeout(0)` | Works but is a hack — yields to browser between tasks, not true parallelism |
| Drop `viteSingleFile` | Not viable — single-file output is required for the WebView host |

## Current Decision

The CPU-heavy work (`buildTocSearchPaths` + matching loop) runs on the main thread after the async DB query resolves. Two mitigations are in place:

1. **LRU-1 cache** — path building only runs once per unique set of candidate books. Subsequent queries with the same book set (user refining the TOC fragment) skip the DB query and path building entirely.
2. **Debounce (300ms)** — limits how often the search fires while typing.

The blocking is most noticeable on the first TOC search for a given book set. After that, cache hits make it instant.

## If the Build Constraint Changes

If `viteSingleFile` is ever dropped or the build is restructured to allow multiple output files, the correct implementation is:

- `src/workers/tocSearch.worker.ts` — pure worker file, imports `buildTocSearchPaths` and `matchWords` from `tocSearchSplit.ts`, exposes `buildAndMatch(rows, tocWords)` via **Comlink**
- Main thread imports worker with `?worker&inline`, wraps with `Comlink.wrap<WorkerApi>(worker)`
- `useBooksFsSearch.ts` calls `workerApi.buildAndMatch(rows, tocWords)` as a plain async function
- Install: `npm install comlink`

This would move all CPU work off the main thread with minimal code — Comlink eliminates all `postMessage`/`onmessage` boilerplate.
