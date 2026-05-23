# Agent Behavior Guidelines

## Markdown File Creation

**Rule:** Do NOT create markdown (.md) files unless explicitly requested by the user.

**Rationale:**
- Markdown files should be created only when the user specifically asks for documentation
- Default behavior is to implement features and make code changes without generating documentation files
- This keeps the repository focused on code rather than auto-generated docs

**When to Create .md Files:**
- User explicitly says "create a document", "write a guide", "document this", etc.
- User asks for a README or specific documentation file
- User requests a specification or design document

**When NOT to Create .md Files:**
- Implementing features
- Fixing bugs
- Refactoring code
- Analyzing code
- Making code changes

**Exception:** README files that are part of project structure (e.g., updating existing README.md) may be modified if necessary for the implementation.
