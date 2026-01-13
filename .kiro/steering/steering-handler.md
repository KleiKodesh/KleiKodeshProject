---
inclusion: always
---

# Steering System Handler

## CRITICAL: AI Assistant Workflow

### MANDATORY FIRST STEP FOR ANY ISSUE
**BEFORE investigating ANY problem, ALWAYS:**

1. **Check if steering exists** for the component/area
2. **Read relevant steering files** based on file patterns
3. **Apply steering guidance** before making assumptions

### Common Failure Pattern
❌ **WRONG**: User reports issue → Start investigating code directly
✅ **CORRECT**: User reports issue → Check steering first → Apply documented solutions

### File Pattern Triggers
When working with these patterns, steering MUST be checked first:

- **WebView/Communication issues** → `integration_communication.md`
- **RegexFind problems** → `project_regexfind-build.md` 
- **Build issues** → `build_*.md` files
- **UI problems** → `ui_*.md` files

### Emergency Steering Check
If you encounter ANY of these symptoms, STOP and check steering:
- Property/data not found or null
- Communication between components failing
- Build output not matching expectations
- Method signatures not working as expected

## New Steering System Architecture

The steering system has been reorganized into a centralized, targeted approach located in `C:\Users\Admin\source\KleiKodeshProject\.steering\` with component-specific files that load only when relevant.

## How to Use This System

### For AI Assistant
Kiro automatically loads relevant steering files based on their frontmatter inclusion patterns. You don't need to manually determine which files to load - Kiro handles this automatically:

1. **Files with `inclusion: always`** - Automatically loaded in every session
2. **Files with `inclusion: fileMatch`** - Automatically loaded when working with matching file patterns
3. **This handler file** - Documents the system mapping for reference

**HOWEVER**: If automatic loading fails or you miss context, MANUALLY check steering files!

### File Naming Convention
All steering files use prefix naming for easy handling:
- `general_` - Always loaded core principles
- `build_` - Build system and processes  
- `ui_` - User interface and styling
- `integration_` - Component integration patterns
- `project_` - Project-specific guidance

## Steering File Mapping

### Always Load (General Knowledge)
- `general_guidelines.md` - Core development principles, documentation policy
- `general_build-requirements.md` - Build system requirements (dotnet build, etc.)
- `general_coding-standards.md` - Code style, modern C# features

### Build System (Load when working with build files)
**File Patterns**: `**/build*`, `**/*.csproj`, `**/*.targets`, `**/package.json`, `**/vite.config.*`
- `build_installer-system.md` - NSIS wrapper, WPF installer, version management
- `build_vsto-automation.md` - VSTO build automation, MSBuild targets
- `build_vue-config.md` - Vue/Vite single-file output configuration

### UI Development (Load when working with UI files)
**File Patterns**: `**/*.vue`, `**/*.css`, `**/*.html`, `**/style*`, `**/theme*`
- `ui_vue-styling.md` - Vue component styling, design system
- `ui_theming-system.md` - CSS variables, theme toggle implementation
- `ui_javascript-standards.md` - Function-based JS, data flow, UI patterns
- `ui_html-principles.md` - HTML/CSS standards (applies to Vue projects too)

### Integration Patterns (Load when working with integration)
**File Patterns**: `**/WebView*`, `**/UserControl*`, `**/*Host*`
- `integration_vsto-webview.md` - WebView2 integration, component structure
- `integration_communication.md` - C# ↔ JavaScript communication patterns

### Project-Specific (Load when working on specific projects)
**RegexFind Project** - `**/regx-find-html/**`, `**/RegexFind/**`
- `project_regexfind-build.md` - RegexFind build process, data structures
- `project_regexfind-color.md` - Color picker system, Word compatibility

**Zayit Project** - `**/vue-zayit/**`
- `project_zayit-bridge.md` - Zayit C# ↔ Vue bridge patterns, PDF manager integration

**Installer Project** - `**/KleiKodeshVstoInstallerWpf/**`
- `project_installer-details.md` - Installer-specific implementation details

**Update System** - `**/UpdateChecker*`, `**/TaskpaneManager*`, `**/ThisAddIn*`
- `project_update-system.md` - Update flow, TLS configuration, deferred installation

## Updating Steering for New Projects

When adding steering for a new project:
1. **Update this mapping section** with new project name and file patterns
2. **Create project-specific files** with `project_[name]-` prefix
3. **Add file patterns** that trigger loading of the new steering
4. **Keep files small and focused** - prefer multiple small files over large ones

## Key Principles

### File Size and Focus
- **Prefer smaller files** over large comprehensive ones
- **Single responsibility** - each file covers one specific area
- **Focused content** - only actionable guidance, no historical context
- **Clear boundaries** - avoid overlap between files

### Loading Strategy
- **Automatic loading** - Kiro loads files based on frontmatter inclusion patterns
- **Context-aware** - `fileMatch` patterns trigger loading when working with relevant files
- **Always available core** - `inclusion: always` files loaded in every session
- **Minimal noise** - Only relevant steering is loaded automatically

### HTML Principles Apply to Vue
All HTML/CSS/JavaScript principles and standards documented in the UI files apply equally to Vue projects. Vue is treated as an extension of HTML/CSS/JS, not a separate paradigm.

## File Management Commands

### Adding New Steering
```
Create: .steering/[prefix]_[component]-[aspect].md
Update: This mapping in steering-handler.md
Test: File pattern matching works correctly
```

### Updating Existing Steering
```
Edit: Relevant targeted file in .steering/
Keep: Content focused and actionable
Remove: Outdated or irrelevant information
```

This system ensures targeted, relevant guidance while maintaining clean organization and easy management.