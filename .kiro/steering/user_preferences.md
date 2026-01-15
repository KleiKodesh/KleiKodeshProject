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

## Non-Negotiable Priorities (Order Matters)

1. **Preserve working code** — never break existing behavior  
2. **Match the user's style and intent** — write code the user feels confident reading, maintaining, and modifying  
3. **Professional quality** — follow established coding conventions, best practices, and quality standards, and platform specific architecture
4. **Clarity first** — understandable at a glance  
5. **User-visible performance** — fast, responsive UI  

---

## Core Rules (About HOW, Not WHAT)

- Solve the **real problem** in the **simplest way that works**
- Do **not** anticipate future problems **when implementing**
- Refactor **only** if it measurably improves clarity, correctness, or performance
- **Existing features are intentional** - focus on improving implementation style, not removing functionality

---

## Abstraction Policy (Implementation Style)

- No premature abstraction
- Abstractions must **earn their existence**
- Allowed only with:
  - ≥ 2 real consumers, or
  - A proven need for substitution
- Prefer direct calls
- Flat > nested
- **Note**: This is about code structure, not feature complexity

---

## Performance & Errors (Implementation Approach)

- Optimize what users feel
- UI responsiveness is non-negotiable
- Never optimize hypothetical bottlenecks **in new code**
- Handle real errors only
- No defensive code "just in case"
- **Note**: Existing optimizations (chunking, virtualization) are there for real reasons

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

## AI Behavior Rules

- Start simple — complexity must be affirmed by user **for new features**
- Do not over-engineer **implementations**
- **Ask before any breaking or architectural change**
- **Respect existing functionality** - it's there for a reason
- Focus on **how** code is written, not **whether** features should exist
- **Clean as you go** - remove garbage and repetitions with every change
