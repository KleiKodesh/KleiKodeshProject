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

- Use unified CSS in assets folder for shared styles
- Create reusable CSS classes and variables
- Component-scoped styles are acceptable for component-specific styling
- Share common styles across components

## Code Brevity

- Code and CSS must be as short as possible
- Minimize line count without sacrificing clarity
- Remove duplication immediately
- Extract repeated patterns to utilities
- When adjusting code, remove garbage code and fix duplication as you go
- Clean up unused imports, variables, and dead code during every change

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
