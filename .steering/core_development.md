---
inclusion: always
---

# Core Development Principles

## CRITICAL: File Reading Rules

### NEVER Read Built/Output Files
❌ **WRONG**: `**/bin/**`, `**/dist/**`, `**/obj/**`, `*.min.js`, `*.min.css`
✅ **CORRECT**: Source files in project directories (`.cs`, `.vue`, `.js`, `.html`)

**WHY**: Built files are minified/compiled and unreadable.

### NEVER Run Applications Directly
❌ **WRONG**: `npm run dev`, `yarn start`, running executables
✅ **CORRECT**: Use `npm run build` to check for issues, or ask user to run the app

**WHY**: Long-running processes block execution and cause issues.

## Modern C# Standards
- Use `new()` instead of `new Type()`
- Use `using var` for automatic disposal
- Use `=>` for simple methods and properties
- Prefer `System.Text.Json` over `Newtonsoft.Json`

## Code Organization
- **Single Responsibility**: Each class has one clear purpose
- **Minimal Classes**: Only necessary properties and methods
- **Concise Code**: Readable over verbose
- **Clear Naming**: Descriptive names that explain intent

## Error Handling
- Always catch exceptions in command handlers
- Provide fallbacks when operations fail
- Include context in error messages
- Properly dispose resources with `using`

## Documentation Policy
**NEVER create standalone README.md files.** All documentation goes in steering files with proper frontmatter inclusion patterns.