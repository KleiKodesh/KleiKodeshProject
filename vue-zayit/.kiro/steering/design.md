---
inclusion: always
---

# Design Principles

## Context

- This is a large-scale application with many compartments
- All design tactics stem from managing complexity at scale
- Organization and discipline are critical to maintainability
- Speed and responsiveness are essential - UI must feel fast and reactive

## Steering Files and Markdown

- One sentence or one paragraph per instruction
- No descriptions, only direct actionable rules
- Concise and straight to the point
- Never create markdown files unless explicitly requested by user

## App Architecture Concept

- Layered architecture: data layer → composables layer → components layer → UI
- Data layer (stores/services/types/workers) is framework-agnostic with no Vue imports
- Composables layer connects data to components and contains business logic
- Components layer is presentation-only (dumb components) organized by feature
- Utils layer contains pure functions with no framework dependencies
- Feature-based organization: components and composables colocated in functional folders (colocation pattern)
- Import boundaries: data imports nothing, composables import data/utils, components import composables/utils, utils import nothing
- See detailed architecture map: #[[file:.kiro/steering/architecture-map.md]]

## File Length Limits

- Vue components: 400 lines maximum
- TypeScript/JavaScript: 500 lines maximum
- C# classes: 500 lines maximum
- Hard stop at 1000 lines for any file
- Refactor before adding to files approaching limits

## Folder Organization

- Maximum 2 levels deep from src/components/
- Colocation over nesting: keep related files together at same level
- Feature folders group by domain, not by type
- Components and composables live together in feature folders
- No utils/helpers/common subfolders - keep utilities at top level of domain

## Component Design

- All components must be dumb (presentation-only)
- No direct store access in components
- All logic extracted to composables
- Components only handle rendering and user events
- Receive reactive state and methods from composables

## CSS Organization

- All shared styles live in src/assets/styles/ organized by category (button.css, input.css, layout.css, typography.css, etc.)
- ALWAYS check existing utility classes before writing custom CSS
- Component-scoped CSS should ONLY contain truly unique styles specific to that component
- NO duplicate button styles, input styles, or layout patterns in component CSS
- Use existing utilities: flex-row, flex-column, flex-center, flex-between, flex-110, bold, text-secondary, c-pointer, etc.
- Custom component CSS is ONLY for: unique layouts, specific positioning, component-specific colors/sizes that don't fit utilities
- When you see duplicate CSS patterns across components, extract to global utilities immediately
- Class ordering: layout → sizing → interactive → cursor → typography → component utilities → specific
- Parent controls child layout (parent applies flex-110, child defines internal structure only)
- Minimal changes: only modify the specific problem, don't change unrelated styles
- Example: Instead of `.my-button { display: flex; align-items: center; cursor: pointer; }` use `class="flex-center c-pointer"`

### CSS Simplicity Rule

**CRITICAL: Use the simplest CSS that achieves the desired effect. Excessive CSS is maintenance debt.**

- **Question every property** - Does removing this property change the visual result? If no, delete it
- **Default values are free** - Don't set `display: block` on a div, `position: static`, `flex-direction: row` on flex containers
- **Avoid redundant properties** - If parent has `display: flex`, child doesn't need `display: block` to work
- **Use CSS cascade** - Let parent styles cascade instead of repeating them on children
- **Shorthand over longhand** - Use `margin: 0` instead of `margin-top: 0; margin-right: 0; margin-bottom: 0; margin-left: 0`
- **Remove experimental properties** - Delete `-webkit-` prefixes if the standard property works in all target browsers
- **One property per concern** - If `width: 100%` achieves the goal, don't add `min-width: 100%; max-width: 100%`

### Examples of Excessive CSS

❌ **BAD - Excessive:**

```css
.container {
  display: flex;
  flex-direction: row; /* default for flex */
  flex-wrap: nowrap; /* default for flex */
  align-items: stretch; /* default for flex */
  position: relative; /* not needed if no absolute children */
  width: 100%;
  min-width: 100%; /* redundant with width: 100% */
  height: auto; /* default for most elements */
}
```

✅ **GOOD - Minimal:**

```css
.container {
  display: flex;
  width: 100%;
}
```

❌ **BAD - Excessive:**

```css
.button {
  display: inline-block;
  padding: 10px 20px;
  margin: 0;
  border: none;
  background: var(--accent-color);
  color: white;
  font-size: 14px;
  font-weight: normal; /* default */
  text-align: center;
  text-decoration: none; /* only needed for <a> tags */
  cursor: pointer;
  outline: none; /* accessibility issue */
  box-sizing: border-box; /* usually global */
}
```

✅ **GOOD - Minimal:**

```css
.button {
  padding: 10px 20px;
  background: var(--accent-color);
  color: white;
  font-size: 14px;
  cursor: pointer;
}
```

### CSS Audit Checklist

Before committing CSS, ask:

1. **Can I delete this property without changing the visual result?** - If yes, delete it
2. **Is this property already set by a parent/global style?** - If yes, delete it
3. **Is this a default value?** - If yes, delete it
4. **Can I use a utility class instead?** - If yes, use the utility
5. **Does this property actually do anything?** - Test by removing it; if nothing changes, delete it

## Code Duplication Prevention

**CRITICAL: Duplication is the root of all maintenance problems. Eliminate it ruthlessly.**

### Before Writing Any Code

1. **Search first** - Use grep/search to find similar code patterns before writing new code
2. **Check utilities** - Review existing utility files (CSS, TypeScript, composables) for reusable patterns
3. **Ask: "Does this already exist?"** - If similar logic exists anywhere, extract and reuse it

### When Creating New Code

1. **If you write the same pattern twice, extract it immediately** - No exceptions
2. **CSS duplication** - Check assets/styles/ before writing component CSS; if pattern exists 2+ times, move to global utilities
3. **Logic duplication** - If business logic appears in 2+ components, extract to composable
4. **Component duplication** - If UI pattern appears 2+ times, create shared component

### When Modifying Existing Code

1. **Look for duplication you're creating** - Your change might duplicate existing code elsewhere
2. **Look for duplication you can remove** - If you touch a file, scan for duplicate patterns to extract
3. **Check if your change makes existing code duplicate** - Adding feature A might make code B and C identical; consolidate them
4. **Clean as you go** - Remove unused imports, dead code, commented code while making changes

### Red Flags for Duplication

🚩 Copy-pasting code between files
🚩 Similar CSS in multiple components (buttons, inputs, layouts)
🚩 Same computed properties in multiple components
🚩 Identical event handlers across components
🚩 Repeated validation/formatting logic
🚩 Multiple components accessing stores the same way

### Extraction Rules

- **2+ occurrences = extract to utility/composable/component**
- **CSS patterns = extract to assets/styles/**
- **Business logic = extract to composable**
- **UI patterns = extract to shared component**
- **Pure functions = extract to utils/**

### Examples

❌ **BAD - Duplication:**

```typescript
// ComponentA.vue
const formatDate = (date: Date) => date.toLocaleDateString("he-IL");

// ComponentB.vue
const formatDate = (date: Date) => date.toLocaleDateString("he-IL");
```

✅ **GOOD - Extracted:**

```typescript
// utils/dateFormatting.ts
export const formatDate = (date: Date) => date.toLocaleDateString("he-IL");

// Both components import and use it
```

❌ **BAD - CSS Duplication:**

```css
/* ComponentA.vue */
.my-button {
  padding: 10px;
  background: var(--accent-color);
  border-radius: 8px;
}

/* ComponentB.vue */
.action-btn {
  padding: 10px;
  background: var(--accent-color);
  border-radius: 8px;
}
```

✅ **GOOD - Global Utility:**

```css
/* assets/styles/components.css */
.btn-primary {
  padding: 10px;
  background: var(--accent-color);
  border-radius: 8px;
}

/* Both components use class="btn-primary" */
```

### Enforcement

- **Every code review** - Reject PRs with duplication
- **Every change** - Scan for duplication to remove
- **Every new feature** - Check if similar code exists first
- **Zero tolerance** - Duplication is never acceptable, even "just this once"

## Component Reusability

- Create reusable subcomponents whenever possible
- Extract common UI patterns to shared components
- Prefer composition over duplication
- Build small, focused, reusable pieces

## Abstraction Policy

- No premature abstraction
- Abstractions must earn their existence with ≥2 real consumers or proven need for substitution
- Prefer direct calls over unnecessary layers
- Flat structure over nested hierarchies

## Vue.js Standards

- Use nextTick() for DOM updates, not setTimeout()
- Prefer VueUse composables over custom implementations
- Use event.code (not event.key) for keyboard shortcuts to support all layouts
- Use block: 'nearest' with scrollIntoView() to prevent aggressive scrolling

## Clean Code Essentials

- Keep constructors simple (no I/O or heavy computation)
- Single responsibility per class/function
- Proper error handling (fail fast, specific exceptions)
- Eliminate code duplication immediately
- Simplify complex methods by breaking them down
- Use proper resource management (using statements, disposal patterns)

## State Management

- Centralize computed state in stores
- Single source of truth for global vs per-item modes
- Components bind to store computed properties, not duplicate logic
