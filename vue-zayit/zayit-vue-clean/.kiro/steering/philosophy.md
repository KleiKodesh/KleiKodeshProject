# Project Philosophy

## Core Principles

- Prefer clarity over rigid rules
- Don't overengineer — write only the code needed for the current requirement, not for imagined future ones
- Start simple. Extract only when complexity justifies it — never upfront
- Avoid indirection — every extra layer is a file someone has to find and open. Only abstract when it genuinely helps navigation and understanding
- Small UI logic stays in the component. Reusable or complex logic moves to a composable. Features organize related code together
- Architecture evolves: simple component → logic grows → extract composable

## App Context

- Mobile-first, tabbed navigation app
- Features: browse books, full-text search books, etc.
- `src/components/` is organized by feature folder (e.g. `books/`, `search/`)

## Folder Structure

- Each feature gets its own folder under `src/components/`
- The feature folder contains the page component, its composables, and all sub-components with their composables
- Keep folders flat — no nested subfolders inside a feature. If a subfolder feels necessary, it probably deserves its own feature folder
- Shared composables used across features live in `src/composables/`
- Shared pure utility functions live in `src/utils/`
- Pinia stores live in `src/stores/` — never nest them under `src/data/` or any other folder

## Rules

### Components
- Components are dumb — no business logic, no data fetching, just props in, template out
- Small UI logic (a toggle, a local flag) can stay inline in the component — don't extract it just to follow a pattern
- Extract a sub-component when it has its own logic or when the markup is complex enough that inlining hurts readability — repetition alone is not enough reason
- Sub-components are named after their parent — `BookCard.vue` → `BookCardCover.vue`, `BookCardMeta.vue`
- Pages are always named `*Page.vue`
- Optimize for readability and clarity first, brevity second

### VueUse
- Prefer VueUse composables over hand-rolling equivalent logic — don't reinvent what's already there
- Check VueUse first before writing any browser API wrappers, event listeners, or common reactive utilities

### Composables
- Only create a composable when there is real logic to extract — a simple component with no logic needs no composable
- A composable used by one component lives in the same folder as that component
- A composable shared across features lives in `src/composables/`
- Name composables after the behavior they encapsulate — `useSearch.ts`, `useBookData.ts`, `usePagination.ts`
- Group related logic in one composable unless a concern is large enough to warrant splitting — avoid composable explosion
- Return a clean API — don't expose internal implementation details, but don't artificially restrict what's returned either
- Formatting, filtering, or mapping logic that doesn't need reactivity belongs in a plain `.ts` util file, not a composable

### Functions
- If you can't describe what a function does without using "and", split it
- Fetching data, transforming it, and updating state are three separate functions
- When functions share logic, extract it into a shared utility rather than duplicating it

### Naming
- Names should be descriptive enough that a comment isn't needed — code reads like prose
- Consistent and predictable naming means anyone can guess a filename without looking

### Search
- Search bars are always anchored to the bottom of the screen, iOS-style — never at the top
- Use `margin-top: auto` or flex column layout to push the search bar to the bottom

### CSS
- Layout is owned by the parent — a component never positions or spaces itself, the parent controls how its children are laid out

### Icons
- Always use Iconify via `@iconify-prerendered/vue-fluent` (or `@iconify-prerendered/vue-fluent-color` for colored variants) — never inline SVGs
- Pick the size variant that matches the context (e.g. `20` for UI controls, `24` for larger touch targets)
- Prefer `Regular` weight by default; use `Filled` for active/selected states

### Design Language
- The app follows Windows 11 Fluent Design principles
- Use `color-mix()` tinted backgrounds instead of solid fills for icon containers
- Prefer subtle hover/active states via `--hover-bg` / `--active-bg` over transforms or shadows
- Rounded corners: `4px` for small controls, `8px` for cards/tiles, `12px` for icon containers
- No drop shadows on interactive elements — depth comes from color and layering, not shadows
- Motion should be minimal and fast (100–150ms), only on background/color transitions

### Language & Direction
- The app is Hebrew-only and strictly RTL
- `dir="rtl"` and `lang="he"` are set on the root HTML element — do not change this
- All layout, spacing, and directional decisions assume RTL
- When the user says "left" they mean the left side of the screen, which is the end of the reading direction in RTL
- Use logical CSS properties (`margin-inline-start`, `padding-inline-end`, etc.) where possible to stay direction-agnostic

## Database

- All SQLite access goes through `src/db/db.ts` — never call `fetch` against the DB directly from a component or composable
- All raw SQL strings live in `src/db/queries.sql.ts` — no inline SQL anywhere else
- The DB client reads `VITE_DB_URL` from env to determine the server endpoint
  - Dev default: `http://localhost:4000` (set in `.env.development`)
  - Override for any other environment via `.env.production` or equivalent
- The server contract is a single `POST /query` endpoint: `{ sql, params? }` → `{ rows }`
- Run the dev SQLite server with `npm run dev:server` (uses `better-sqlite3` via `server/index.js`)
- `DB_PATH` env var on the server controls which `.db` file is opened (default: `./data.db`)
- Feature-level data fetching lives in a composable (e.g. `useBookData.ts`) that calls `query()` from `src/db/db.ts` using strings imported from `queries.sql.ts`
