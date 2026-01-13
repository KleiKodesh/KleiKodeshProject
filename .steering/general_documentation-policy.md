---
inclusion: always
---

# Documentation Policy

## CRITICAL: No Standalone README Files

### Policy
**NEVER create standalone README.md files for individual projects or components.** All documentation must be integrated into the steering system.

### Why This Policy Exists
- **Scattered Documentation**: README files create fragmented documentation across the codebase
- **Maintenance Burden**: Multiple README files become outdated and inconsistent
- **Discovery Issues**: Important information gets buried in project-specific files
- **Duplication**: Same information repeated across multiple README files

### What to Do Instead

**✅ CORRECT Approach:**
1. **Add to Existing Steering Files**: Update relevant steering files with new information
2. **Create Targeted Steering**: Create specific steering files for new components/patterns
3. **Use Frontmatter Inclusion**: Configure automatic loading based on file patterns
4. **Centralized Knowledge**: Keep all guidance in the `.steering/` directory

**❌ WRONG Approach:**
- Creating `README.md` in project directories
- Documenting setup instructions in standalone files
- Writing project-specific documentation outside steering

### Implementation Guidelines

#### For New Projects
```
Instead of: ProjectName/README.md
Create: .steering/project_projectname-overview.md
```

#### For Build Instructions
```
Instead of: ProjectName/BUILD.md
Update: .steering/build_projectname-process.md
```

#### For API Documentation
```
Instead of: ProjectName/API.md
Create: .steering/integration_projectname-api.md
```

### Migration Process

When you encounter existing README files:
1. **Extract Important Information**: Identify valuable content
2. **Integrate into Steering**: Add content to appropriate steering files
3. **Delete README File**: Remove the standalone file
4. **Update References**: Fix any links or references to the old README

### Steering File Organization

Use the established naming convention:
- `general_` - Core principles and policies
- `build_` - Build processes and requirements
- `ui_` - User interface guidelines
- `integration_` - Component integration patterns
- `project_` - Project-specific guidance

### File Pattern Triggers

Configure steering files with appropriate frontmatter:
```yaml
---
inclusion: fileMatch
fileMatchPattern: '**/ProjectName/**'
---
```

This ensures relevant documentation loads automatically when working on specific projects.

### Benefits of This Approach

1. **Centralized Discovery**: All documentation in one location
2. **Automatic Loading**: Relevant guidance appears when needed
3. **Consistent Format**: Standardized documentation structure
4. **Easy Maintenance**: Single source of truth for each topic
5. **Context-Aware**: Documentation loads based on what you're working on

### Examples

**ZayitWrapper Project Documentation:**
- ❌ `vue-zayit/Zayit-cs/ZayitWrapper/README.md`
- ✅ `.steering/project_zayit-wrapper.md`

**Build Process Documentation:**
- ❌ `vue-zayit/BUILD.md`
- ✅ `.steering/build_zayit-process.md`

**WebView Integration Guide:**
- ❌ `ZayitLib/WebView-Guide.md`
- ✅ `.steering/integration_zayit-webview.md`

This policy ensures all documentation remains discoverable, maintainable, and contextually relevant.