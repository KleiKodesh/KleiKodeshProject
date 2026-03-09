---
inclusion: always
---

# AI Coding Contract

## CRITICAL DISTINCTION

This document defines **HOW** to implement, not **WHAT** to implement.

- **WHAT** (functionality): All features exist for real reasons - trust the codebase
- **HOW** (implementation): Follow these style and pattern guidelines

**Example**:

- ✅ Virtualization feature exists because it's needed (WHAT)
- ⚠️ Implementation might use unnecessary classes or over-abstraction (HOW)

---

## Critical Enforcement Rules

- When creating or editing steering files (.md in .kiro/steering/): one sentence or one paragraph per instruction, no descriptions, only direct actionable rules, concise and straight to the point
- Never include code examples, usage samples, or descriptive text in steering files
- When you encounter obvious duplication or excessive CSS while working on a task, fix it if it's quick (under 2 minutes); otherwise note it and continue with the main task
- Only create markdown files when explicitly requested by user or when editing existing steering files

---

## Non-Negotiable Priorities (Order Matters)

1. **Preserve working code** — never break existing behavior
2. **Match the user's style and intent** — write code the user feels confident reading, maintaining, and modifying
3. **Professional quality** — follow established coding conventions, best practices, and quality standards, and platform specific architecture
4. **Platform-specific conventions** — respect .NET/C#, Vue.js, TypeScript, and Windows development patterns
5. **Clarity first** — understandable at a glance
6. **User-visible performance** — fast, responsive UI

---

## Context

- This is a large-scale application with many compartments
- All design tactics stem from managing complexity at scale
- Organization and discipline are critical to maintainability
- Speed and responsiveness are essential - UI must feel fast and reactive

---

## App Architecture

- Layered architecture: data layer → composables layer → components layer → UI
- Data layer (stores/services/types/workers) is framework-agnostic with no Vue imports
- Composables layer connects data to components and contains business logic
- Components layer is presentation-only (dumb components) organized by feature
- Utils layer contains pure functions with no framework dependencies
- Feature-based organization: components and composables colocated in functional folders (colocation pattern)
- Import boundaries: data imports nothing, composables import data/utils, components import composables/utils, utils import nothing
- See detailed architecture map: #[[file:.kiro/steering/architecture-map.md]]

---

## File Length Limits

- Vue components: 400 lines maximum
- TypeScript/JavaScript: 500 lines maximum
- C# classes: 500 lines maximum
- Hard stop at 1000 lines for any file
- Refactor before adding to files approaching limits

---

## Folder Organization

- Maximum 2 levels deep from src/components/
- Colocation over nesting: keep related files together at same level
- Feature folders group by domain, not by type
- Components and composables live together in feature folders
- No utils/helpers/common subfolders - keep utilities at top level of domain

---

## Component Design

- All components must be dumb (presentation-only)
- No direct store access in components
- All logic extracted to composables
- Components only handle rendering and user events
- Receive reactive state and methods from composables

---

## CSS Organization

- All shared styles live in src/assets/styles/ organized by category (button.css, input.css, layout.css, typography.css, etc.)
- Check existing utility classes before writing custom CSS - see #[[file:custom-steering/css-utilities.md]] for complete list
- Component-scoped CSS should ONLY contain truly unique styles specific to that component
- NO duplicate button styles, input styles, or layout patterns in component CSS
- Custom component CSS is ONLY for: unique layouts, specific positioning, component-specific colors/sizes that don't fit utilities
- When you see obvious duplicate CSS patterns (3+ occurrences), extract to global utilities
- Parent controls child layout (parent applies flex-110, child defines internal structure only)
- Minimal changes: only modify the specific problem, don't change unrelated styles
- Question CSS properties - if removing it doesn't change the visual result, delete it
- Don't set default values (display: block on div, position: static, flex-direction: row on flex containers)
- Use shorthand over longhand (margin: 0 instead of margin-top: 0; margin-right: 0; margin-bottom: 0; margin-left: 0)

---

## Core Rules (About HOW, Not WHAT)

- Solve the **real problem** in the **simplest way that works**
- Do **not** anticipate future problems **when implementing**
- Refactor **only** if it measurably improves clarity, correctness, or performance
- **Existing features are intentional** - focus on improving implementation style, not removing functionality

---

## Abstraction Policy (Implementation Style)

- Abstractions must earn their existence: allowed only with ≥2 real consumers OR a proven need for substitution
- Prefer direct calls over abstraction layers
- Flat > nested
- This is about code structure, not feature complexity

---

## Performance & Errors (Implementation Approach)

- Optimize for user-visible performance: operations that take >100ms or cause UI lag
- UI responsiveness is non-negotiable
- Never optimize hypothetical bottlenecks in new code
- Handle real errors only, no defensive code "just in case"
- Existing optimizations (chunking, virtualization) are there for real reasons

---

## Virtualization Strategy

Use CSS `content-visibility: auto` for all list virtualization needs.

```css
.list-item {
  content-visibility: auto;
  contain-intrinsic-size: auto 500px;
}
```

Benefits: Browser handles rendering optimization automatically, no JavaScript complexity, preserves normal DOM flow and CSS features (sticky, flexbox, grid), scroll position maintained correctly, excellent browser support.

Rule: Use CSS content-visibility for virtualization. Do not use JavaScript virtual scroller libraries (vue-virtual-scroller, etc.) unless there is a proven, specific technical limitation that CSS cannot solve.

Note: vue-virtual-scroller is legacy code still present in the codebase for transition purposes. Do not use it in new code.

---

## Code Maintenance & Technical Debt

### The Gnarly Tree Problem

**Critical Issue**: Code accumulates like a gnarly tree through:

- **Accumulated garbage** - Dead code, unused functions, obsolete patterns
- **Code repetitions** - Same logic duplicated across files
- **Incremental rot** - Small additions that don't get cleaned up

### Prevention Rules

- **Before adding new code**: Check if similar code already exists
- **After modifying code**: Remove what's no longer needed
- **When you see duplication**: Extract to shared utility immediately
- **When you see dead code**: Delete it (version control preserves history)
- **Regular pruning**: Treat code like a garden - remove weeds as you go

### Red Flags for Gnarly Code

🚩 Copy-pasted code blocks with minor variations
🚩 Functions that are never called
🚩 Commented-out code "just in case"
🚩 Multiple ways to do the same thing
🚩 Imports that aren't used
🚩 Variables declared but never read

### Cleanup Checklist (Every Change)

- [ ] Remove unused imports
- [ ] Delete commented-out code
- [ ] Extract duplicated logic
- [ ] Remove unused functions/variables
- [ ] Consolidate similar patterns
- [ ] Update or remove outdated comments

**Philosophy**: Leave the code cleaner than you found it. Every commit should reduce complexity, not add to it.

---

## Code Duplication Prevention

- Duplication is a maintenance problem - eliminate it when practical
- Check utilities - review existing utility files (CSS, TypeScript, composables) for reusable patterns
- If you write the same pattern twice in the same session, extract it immediately
- CSS duplication - if pattern exists 3+ times, extract to global utilities in assets/styles/
- Logic duplication - if business logic appears in 3+ components, extract to composable
- Component duplication - if UI pattern appears in 3+ times, create shared component
- Clean as you go - remove unused imports, dead code, commented code while making changes
- CSS patterns = extract to assets/styles/
- Business logic = extract to composable
- UI patterns = extract to shared component
- Pure functions = extract to utils/

---

## Component Reusability

- Create reusable subcomponents whenever possible
- Extract common UI patterns to shared components
- Prefer composition over duplication
- Build small, focused, reusable pieces

---

## Vue.js Standards

- Use nextTick() for DOM updates, not setTimeout()
- Prefer VueUse composables over custom implementations
- Use event.code (not event.key) for keyboard shortcuts to support all layouts
- All scrolling operations MUST use useScrollToElement composable functions (scrollToElement, scrollToElementCenter, scrollToElementTop, scrollToVirtualItem, etc.)
- Scroll pattern: always use block: 'nearest' first, then manual adjustment - never use 'center', 'start', or 'end' directly in scrollIntoView
- This prevents parent container scrolling and provides precise control
- Use Iconify icons via @iconify/vue for all icons (import { Icon } from '@iconify/vue', then <Icon icon="fluent:icon-name" />)

---

## Clean Code Essentials

- Eliminate code duplication immediately
- Simplify complex methods by breaking them down
- Don't create stores if a util will do

---

## State Management

- Centralize computed state in stores
- Single source of truth for global vs per-item modes
- Components bind to store computed properties, not duplicate logic

---

## Platform-Specific Architecture & Conventions

### .NET/C# Conventions

- Follow Microsoft C# coding standards and naming conventions
- Use PascalCase for public members, camelCase for private fields
- Leverage modern C# features (async/await, LINQ, nullable reference types)
- Respect VSTO and Office Add-in architectural patterns
- Use proper dependency injection and service patterns where established

### Vue.js/TypeScript Conventions

- Follow Vue 3 Composition API patterns consistently
- Use TypeScript strict mode and proper type definitions
- Maintain single-file component structure with clear separation
- Follow established component naming and prop patterns
- Respect Vue reactivity system and lifecycle patterns

### Windows Development Patterns

- Follow Windows UI/UX guidelines and accessibility standards
- Respect Windows file system conventions and security models
- Use appropriate Windows-specific APIs and integration patterns
- Follow established patterns for WebView2 and WPF integration

### Code Organization Principles

- **Consistent file structure** — follow established folder hierarchies
- **Clear separation of concerns** — UI, business logic, data access layers
- **Predictable naming** — files, classes, methods follow project conventions
- **Logical grouping** — related functionality stays together
- **Platform boundaries** — respect C#/Vue communication patterns

---

## Naming Conventions

- Parent components use base name, children add descriptive suffix (BookView.vue → BookViewToolbar.vue, BookViewSidebar.vue).
- Top-level route components end with Page suffix (BookViewPage.vue, SettingsPage.vue, WorkspacesPage.vue).
- Generic reusable components use descriptive names without feature prefix, located in components/shared/ (Combobox.vue, ContextMenu.vue, SplitPane.vue).
- Feature-specific components use feature prefix (HebrewbooksListItem.vue, FsBookNode.vue).
- Component-specific composables match component name exactly (LineView.vue → useLineView.ts, CommentaryView.vue → useCommentaryView.ts).
- Feature composables use feature name (useBookViewer.ts, useCommentary.ts, useHebrewBooks.ts).
- Aspect-specific composables use component + aspect pattern (useLineViewScroll.ts, useLineViewSelection.ts, useCommentaryFilters.ts).
- Shared composables use descriptive names, located in components/shared/ (useKeyboardShortcuts.ts, useScrollToElement.ts, useZoom.ts).
- Components and their composables colocate in feature folders.
- Avoid generic names (View.vue, Container.vue, Wrapper.vue), redundant suffixes (useBookViewPage.ts), and ambiguous patterns (TocTreeView.vue when TocTree.vue exists).

---

## AI Behavior Rules

- Start simple — complexity must be affirmed by user **for new features**
- Do not over-engineer **implementations**
- **Ask before any breaking or architectural change**
- **Respect existing functionality** - it's there for a reason
- **Follow platform conventions** - .NET, Vue.js, TypeScript, Windows patterns
- **Maintain code organization** - respect established file structure and naming
- Focus on **how** code is written, not **whether** features should exist
- **Clean as you go** - remove garbage and repetitions with every change
- **Never guess when problem solving** - trace through actual code execution paths, read the relevant files, follow the data flow, then diagnose based on facts
