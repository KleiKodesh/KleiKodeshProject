# Developer Preferences

## Code Philosophy

- Prefer clarity over rigid rules
- Don't overengineer — write only the code needed for the current requirement, not for imagined future ones
- Start simple. Extract only when complexity justifies it — never upfront
- Avoid indirection — every extra layer is a file someone has to find and open. Only abstract when it genuinely helps navigation and understanding
- Architecture evolves: simple component → logic grows → extract composable

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
