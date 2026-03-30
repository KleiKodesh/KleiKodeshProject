# Developer Preferences

## Code Philosophy

- Prefer clarity over rigid rules
- Don't overengineer — write only the code needed for the current requirement, not for imagined future ones
- Start simple. Extract only when complexity justifies it — never upfront
- Avoid indirection — every extra layer is a file someone has to find and open. Only abstract when it genuinely helps navigation and understanding
- Architecture evolves: simple component → logic grows → extract composable

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

## Icons

- Always use Iconify via `@iconify-prerendered/vue-fluent` (or `@iconify-prerendered/vue-fluent-color` for colored variants) — never inline SVGs
- Pick the size variant that matches the context (e.g. `20` for UI controls, `24` for larger touch targets)
- Prefer `Regular` weight by default; use `Filled` for active/selected states
- Never use the fluent color package for icons that don't have a color variant — fall back to filled + explicit color instead

## Git

- Never use git commands unless explicitly asked by the user

## Prettier Compatibility

This project uses Prettier with `printWidth: 100`. The Vue template compiler rejects multiline expressions inside attribute quote strings — Prettier causes this by wrapping long inline handlers across lines.

- Never put multi-statement logic inline in Vue template event handlers — Prettier will reformat them into newline-separated statements inside the quotes, which the Vue template compiler rejects
- Never use ternary expressions inside Vue template attribute bindings if the full line exceeds ~100 chars — Prettier will wrap them into a multiline form that breaks the parser
- Never use template literals (backticks) inside Vue template attribute strings
- The fix in all cases is to extract the logic to a named function or `computed` in `<script setup>` and reference it by name in the template — this is Prettier-safe and also cleaner
- Arrow functions in `<script setup>` are fine — this rule applies only to inline expressions inside template attribute values
