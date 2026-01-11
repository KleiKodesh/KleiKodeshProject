---
inclusion: always
---

# General Development Guidelines

## CRITICAL: File Reading Rules

### NEVER Read Built/Output Files
**ALWAYS read source files, NEVER built/output files:**

❌ **WRONG FILES TO READ**:
- Any files in `**/bin/**` directories (build output)
- Any files in `**/dist/**` directories (build output)  
- Any files in `**/obj/**` directories (build output)
- Minified files (`.min.js`, `.min.css`)
- Files copied from build processes
- Any file that looks minified or compiled

✅ **CORRECT SOURCE FILES**:
- Original source files in project directories
- `.cs` files in source directories
- Original `.js`, `.html`, `.css` files in source projects
- Configuration files (`.json`, `.xml`, `.config`)

**WHY**: Built files are minified, compiled, or processed - they don't reflect the actual source code structure and are unreadable.

**RULE**: If a file looks unreadable or minified, find the original source file instead.

## Development Commands

### Vue Projects
- **NEVER run `npm run dev`** unless explicitly requested by user
- **ALWAYS run `npm run build`** for testing and deployment
- Only use dev server when user specifically asks for live development

## Documentation Policy
- **Do not create markdown files** to summarize work or document processes unless explicitly requested
- Focus on creating functional code and configuration files only
- Only create documentation when user specifically asks or when essential for functionality

## Coding Style
- **Concise Code**: Prefer compact, readable code over verbose implementations
- **Single Responsibility**: Each class should have one clear purpose
- **Minimal Classes**: Only include necessary properties and methods, remove unused code