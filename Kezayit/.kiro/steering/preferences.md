# Developer Preferences

## Code Philosophy

- Prefer clarity over rigid rules
- Don't overengineer — write only the code needed for the current requirement, not for imagined future ones
- Start simple. Extract only when complexity justifies it — never upfront
- Avoid indirection — every extra layer is a file someone has to find and open. Only abstract when it genuinely helps navigation and understanding
- Architecture evolves: simple component → logic grows → extract composable
- A file's contents must match what its name promises. A file named `BookViewPage.vue` is a page shell — it wires pieces together, it does not contain scroll sync logic, session restore logic, or search handlers. A composable named `useToc.ts` owns TOC loading — it does not contain commentary logic. If you cannot describe a file's entire contents using only its name, the mismatched code belongs in a different file.
- A composable or utility file must never import from a component (`.vue`) file. Components are consumers — they sit at the top of the dependency graph. Types shared between a composable and its child components belong in a dedicated `*Types.ts` file in the same feature folder, not in either the composable or the component. Importing a type from a `.vue` file in a `.ts` file inverts the dependency direction and is forbidden. The same rule applies to interfaces defined inside `.vue` files that are needed by `.ts` files — move them to a `*Types.ts` or `*types.ts` file and import from there in both the component and the composable.
- `.vue` files must never re-export types for the benefit of `.ts` files. If a type is needed by a `.ts` file, it does not belong in a `.vue` file at all — move it to a plain `.ts` types file. A `.vue` file that re-exports a type is a sign the type is in the wrong place.

## File Length & Refactoring Thresholds

These thresholds exist so that agentic AI can reliably read, edit, and reason about any file without losing context or making partial edits.

- Vue single-file components (`.vue`): soft limit 250 lines, hard limit 350 lines. Above 350, the component must be split — extract sub-components or move logic into a composable before adding more code.
- Composables (`.ts` in `composables/` or feature folders): soft limit 200 lines, hard limit 300 lines. Above 300, split by concern — each composable should own one clearly named behavior.
- Utility files (`src/utils/*.ts`): soft limit 150 lines, hard limit 250 lines. Above 250, group related functions into a new util file with a descriptive name.
- Store files (`src/stores/*.ts`): soft limit 200 lines, hard limit 300 lines. Above 300, extract a focused sub-store or move derived logic into a composable.
- When a file crosses its hard limit during an edit session, refactor it before completing the task — do not leave the file over the limit.
- Refactoring means splitting into smaller files with clear single responsibilities, not just moving code around. Every extracted file must have a name that describes exactly what it does.
- After any split, update the `README.md` of the affected folder and update `architecture.md` if the split introduces a new composable, component, or utility that belongs in the architecture map.

## Components

- When switching between a fixed set of components based on a single state value, use `<component :is="...">` with a map object rather than a chain of `v-if`/`v-else-if`. The map lives in `<script setup>` and the template stays clean.
- Components are dumb — no business logic, no data fetching, just props in, template out
- Small UI logic (a toggle, a local flag) can stay inline in the component — don't extract it just to follow a pattern
- Extract a sub-component when it has its own logic or when the markup is complex enough that inlining hurts readability — repetition alone is not enough reason
- Pages are always named `*Page.vue`
- Optimize for readability and clarity first, brevity second

## Composables

- Only create a composable when there is real logic to extract — a simple component with no logic needs no composable
- Name composables after the behavior they encapsulate — `useSearch.ts`, `useBookData.ts`, `usePagination.ts`
- Group related logic in one composable unless a concern is large enough to warrant splitting — avoid composable explosion
- Return a clean API — don't expose internal implementation details, but don't artificially restrict what's returned either
- Formatting, filtering, or mapping logic that doesn't need reactivity belongs in a plain `.ts` util file, not a composable

## Functions

- If you can't describe what a function does without using "and", split it
- Fetching data, transforming it, and updating state are three separate functions
- When functions share logic, extract it into a shared utility rather than duplicating it

## Naming

- Names should be descriptive enough that a comment isn't needed — code reads like prose
- Consistent and predictable naming means anyone can guess a filename without looking
- Never use shorthand or abbreviations in any name — variables, functions, components, files, database tables, columns, or CSS classes. Write the full word every time. Examples: use `abbreviation` not `abbrev`, `definition` not `def`, `configuration` not `config`, `navigation` not `nav`, `parameter` not `param`, `index` not `idx`, `button` not `btn`, `message` not `msg`, `error` not `err`, `reference` not `ref` (unless it is a Vue template ref), `source` not `src` (unless it is an HTML attribute), `destination` not `dest`, `previous` not `prev`, `next` is fine as it is a full word.
- The only exceptions are universally understood domain terms where the short form IS the name: `id`, `url`, `html`, `css`, `sql`, `api`, `db` when used as a suffix in a variable name referring to a database connection object.

## Dropdowns & Panels — Close Behavior

Every dropdown, popover, or overlay panel must use `useDropdownClose` from `src/composables/useDropdownClose.ts` — never `onClickOutside` directly. `useDropdownClose` also closes on window blur (iframe clicks, app switching), which `onClickOutside` alone does not handle.

When the panel has a dedicated toggle button, pass it as the `toggleButton` option so the composable can suppress the close handler on that element and prevent the toggle-button race condition (pointerdown closes → click reopens).

Every dropdown or panel must be rendered with `v-if`, not `v-show`. `v-if` ensures the component is fully unmounted when closed — child composables, watchers, and focus state are all torn down. `v-show` keeps the subtree alive and is only appropriate for tab-pane switching where preserving scroll position or expensive state across tab changes is intentional (e.g. the settings tab panes).

## VueUse

- Prefer VueUse composables over hand-rolling equivalent logic — don't reinvent what's already there
- Check VueUse first before writing any browser API wrappers, event listeners, or common reactive utilities

## CSS

- Layout is owned by the parent — a component never positions or spaces itself, the parent controls how its children are laid out
- Use logical CSS properties (`margin-inline-start`, `padding-inline-end`, etc.) where possible to stay direction-agnostic

## UI Density

- Always prefer the most compact layout that still meets the 44px touch target rule
- Reduce padding, margins, and font sizes to the minimum that remains comfortable — never add whitespace for aesthetics alone
- Dropdown list items (pickers, selects, year/month lists) use `height: 26px`, `font-size: 12px`, `padding: 0`, `line-height: 1` — this is the established balance between touch usability and compactness; do not increase item height in dropdowns without a specific reason

## Icons

- Always use Iconify via `@iconify-prerendered/vue-fluent` (or `@iconify-prerendered/vue-fluent-color` for colored variants) — never inline SVGs
- Pick the size variant that matches the context (e.g. `20` for UI controls, `24` for larger touch targets)
- Prefer `Regular` weight by default; use `Filled` for active/selected states
- Never use the fluent color package for icons that don't have a color variant — fall back to filled + explicit color instead

## Steering Files

- No code samples in steering files — describe rules and patterns in plain prose only
- Whenever the architecture changes — new components, composables, stores, routes, or feature folders added or removed — update `architecture.md` to reflect the change

## Navigation Controls (RTL)

In RTL layout, the leftmost button is always the "next/advance" button. For prev/next navigation pairs, always use `IconChevronLeft` for next (advance) and `IconChevronRight` for prev (back). The correct button order reading left→right on screen is: `<` (next) · `>` (prev) · home · toggle. Never reverse this — the chevron icon direction matches the reading direction, not the semantic direction.

## Git

- Never use git commands unless explicitly asked by the user

## Prettier Compatibility

This project uses Prettier with `printWidth: 100`. The Vue template compiler rejects multiline expressions inside attribute quote strings — Prettier causes this by wrapping long inline handlers across lines.

- Never put multi-statement logic inline in Vue template event handlers — Prettier will reformat them into newline-separated statements inside the quotes, which the Vue template compiler rejects
- Never use ternary expressions inside Vue template attribute bindings if the full line exceeds ~100 chars — Prettier will wrap them into a multiline form that breaks the parser
- Never use template literals (backticks) inside Vue template attribute strings
- The fix in all cases is to extract the logic to a named function or `computed` in `<script setup>` and reference it by name in the template — this is Prettier-safe and also cleaner
- Arrow functions in `<script setup>` are fine — this rule applies only to inline expressions inside template attribute values

## Documentation

- Every folder in the app (`src/components/*`, `src/composables/`, `src/utils/`, `src/stores/`, `src/host/`, `src/theme/`) must have its own `README.md`
- When the user says "add documentation" or "document this", they mean: create or update the local `README.md` in the relevant folder
- READMEs must be purely functional and concise — describe what each file does, where to add new code, what to import from where, and what patterns or constraints to follow
- READMEs are written to help an AI agent make correct decisions without reading every file — they should answer "where does this go?" and "what should I use for this?"
- No code samples in READMEs — prose only
- No padding, no intros, no summaries — every sentence must carry information
- Whenever functionality changes — files added, removed, renamed, or behavior meaningfully altered — update the README of the affected folder immediately

## Book Search Query Normalization

Book catalog search applies Hebrew-specific text transformations so that variant spellings and abbreviations all match the same results. These transformations live exclusively in `src/utils/bookQueryNormalizer.ts` and must be applied symmetrically to both sides: indexed titles (in `booksCategoryTree.ts` `assignFullPaths`) and user queries (in `useFileSystemSearch.ts` `toWords`).

Current rules:
- שו"ע / שוע → שלחן ערוך (abbreviation expansion)
- שולחן → שלחן (standalone word normalization — applies wherever the word appears, not only in שלחן ערוך)

When adding a new normalization rule — a new abbreviation, a new spelling variant, a new title alias — add it only to `bookQueryNormalizer.ts`. Never add book-search-specific normalization to `normalizeText.ts` or inline it in a composable.

## Scripts & Tooling

- For any script that runs outside the app — database building, data processing, file generation, migrations, or any other standalone tooling — use Python, not Node.js
- Never write a Node.js script for out-of-process tasks; if a task needs a script, it's Python

## Misc Folder

`Misc/` is the workspace-level folder for assets and scripts that live outside the app but belong to the project.

- `Misc/scripts/` — reusable standalone scripts only. Every script here must be something that can and will be run again (build pipelines, data imports, DB rebuilds, verification tools). One-off scripts do not belong here.
- `Misc/scripts/dictionary/` — scripts for building and maintaining the dictionary databases. Has its own `README.md` that documents the full rebuild pipeline.
- `Misc/pdfjs-backup/` — backup of the PDF.js viewer used in the app.
- Other files in `Misc/` (e.g. `.zim` files) are static assets used by the app or for testing.

## Cleanup After One-Off Work

When completing a task that involved temporary or investigative work, always clean up before finishing:

- Delete any script written purely to gather data, audit content, or investigate a problem once the results have been acted on — these have no future value and clutter the repo.
- Delete output files produced by one-off scripts (markdown reports, JSON dumps, analysis files) once their contents have been used.
- Delete any temporary files created during the task (scratch files, test outputs, intermediate data files).
- A script is reusable if it can be run again in the future to produce useful output — build scripts, import scripts, verification scripts, and optimization scripts qualify. Audit scans, one-off data mutations, and diagnostic dumps do not.
- If a cleanup script mutates data and its changes are now permanently baked into the database or codebase, delete the script — it cannot safely be run again and keeping it implies it can.
- Apply this rule to `Misc/scripts/`, the workspace root, and any other location where temporary work files accumulate.

## IDB-Backed LRU Cache Pattern

When the user asks to add an LRU cache for a feature, always ask for the cap limit before implementing — never hardcode a guess.

The established pattern for an IDB-backed LRU cache depends on how many places consume it. If multiple stores or composables need to read and write the cache, use a Pinia store in `src/stores/` — see `searchCacheStore.ts` as the canonical reference. If only a single feature touches the cache, use a plain module (no Pinia) co-located in the feature folder — see `dictCache.ts` in `src/components/dictionary/` as the reference for that case.

Either way the internal structure is the same:

- A dedicated IDB database registered in the `handles` map in `persistence.ts`, with matching `idb{Name}Get/Set/Delete` exports added there.
- The database is added to `idbClearAll` in `persistence.ts` so it is wiped on full app reset.
- A `PREFIX` constant for all entry keys and a separate `LRU_KEY` entry that holds the ordered list of cached keys as a JSON array.
- `get` reads the entry, returns null on miss, and calls `touchLru` on hit to move the key to the most-recently-used end.
- `set` calls `evictIfNeeded` before writing — eviction removes the least-recently-used entry (the first element of the LRU array) when the array length without the current key is already at the cap.
- `clear` deletes all entries listed in the LRU array plus the LRU key itself.
- Results are never cached in memory — only the LRU key list may be kept in memory if needed. Large result sets belong in IDB only.
- The full app reset (`idbClearAll`) drops the database entirely — no explicit `clear()` call or settings button is needed for the full reset path.

## Screaming Architecture

Folder and file names must scream the domain they belong to — not the technology or pattern used. A developer (or AI agent) glancing at the folder structure should immediately understand what the system does, not how it is built.

The core principle, from Robert C. Martin: if you walked up to a building and could identify it as a library, school, or hospital purely from its architecture — without a sign — that is the goal. Code structure should do the same.

Practical rules for this codebase:

- Feature folders are named after the domain concept they represent, not the technical role. `book-catalog/` not `file-system/` or `fs/`. `search-db/` not `bloom/`. `book-view/` not `reader/`.
- Framework and infrastructure concerns (HTTP, IDB, WebView bridge) live at the edges — in `src/host/` and `src/utils/persistence.ts` — never mixed into feature folders.
- Business logic (what the app does) is always more prominent than technical plumbing (how it does it). A new developer should be able to name every feature of the app by reading only the folder names under `src/components/`.
- When a folder name could apply to any app (e.g. `utils`, `services`, `helpers`, `file-system`), it is wrong. Every folder name must be specific enough to belong only to this app.
- Technical layer names (`controllers`, `repositories`, `models`) are forbidden as top-level organizers. Organize by feature first; layer distinctions live inside the feature folder if needed at all.
- Abbreviations in folder and file names fail the screaming test twice — they obscure both the domain and the intent. `books-fs` says neither "book catalog" nor "browser". Write the full domain name every time.
- When a name feels generic or could belong to any project, stop and ask: what is the actual domain concept here? `file-system` describes a technology. `book-catalog` describes the feature. Always prefer the domain word.
- A rename is not complete until every reference is updated: imports, exported function names, CSS class names, comments, READMEs, steering files, and the old folder deleted. A partial rename leaves the codebase in a worse state than before — half the names scream the old concept, half scream the new one.
- Thin wrapper components that only forward props and delegate method calls do not earn their own file. Inline them into the parent. Every file must justify its existence with logic that cannot live elsewhere.
