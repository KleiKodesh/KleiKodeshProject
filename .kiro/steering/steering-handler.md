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

- **WebView/Communication issues** → `webview_integration.md`, `core_communication.md`
- **RegexFind problems** → `regexfind_architecture.md` 
- **Build issues** → `core_build.md`, `installer_system.md`
- **Vue/UI problems** → `vue_development.md`, `vue_theming.md`

### Emergency Steering Check
If you encounter ANY of these symptoms, STOP and check steering:
- Property/data not found or null
- Communication between components failing
- Build output not matching expectations
- Method signatures not working as expected

## New Steering System Architecture

The steering system has been reorganized into a centralized, targeted approach located in `.steering/` with component-specific files that load only when relevant.

## How to Use This System

### For AI Assistant
Kiro automatically loads relevant steering files based on their frontmatter inclusion patterns. You don't need to manually determine which files to load - Kiro handles this automatically:

1. **Files with `inclusion: always`** - Automatically loaded in every session
2. **Files with `inclusion: fileMatch`** - Automatically loaded when working with matching file patterns
3. **This handler file** - Documents the system mapping for reference

**HOWEVER**: If automatic loading fails or you miss context, MANUALLY check steering files!

### File Naming Convention
All steering files use prefix naming for easy categorization:

**Core Principles** (always loaded):
- `core_` - Fundamental development principles and rules

**Technology-Specific** (loaded by file patterns):
- `csharp_` - C# language patterns and best practices
- `vue_` - Vue.js development standards and patterns
- `javascript_` - JavaScript/TypeScript patterns
- `css_` - CSS and styling guidelines

**Component-Specific** (loaded by file patterns):
- `webview_` - WebView2 integration patterns
- `regexfind_` - RegexFind module guidance
- `zayit_` - Zayit project patterns
- `[component]_` - Other specific components

**System-Specific** (loaded by file patterns):
- `installer_` - Installation and deployment
- `update_` - Update system patterns
- `build_` - Build processes and automation
- `[system]_` - Other infrastructure concerns

## Steering File Mapping

### Always Load (Core Knowledge)
- `core_development.md` - Essential development principles and rules
- `core_build.md` - Build system requirements and processes
- `core_communication.md` - C# ↔ JavaScript communication patterns

### C# Development
**File Patterns**: `**/*.cs`
- `csharp_patterns.md` - Modern C# features, JSON handling, async patterns

### Vue Development
**File Patterns**: `**/*.vue`, `**/vite.config.*`, `**/package.json`
- `vue_development.md` - Vue standards, single-file output, component patterns
- `vue_theming.md` - CSS variables, theme system, flat list design

### WebView2 Integration
**File Patterns**: `**/WebView*`, `**/UserControl*`, `**/*Host*`, `**/*Dialog*`
- `webview_integration.md` - Component structure, command dispatch, virtual hosts
- `webview_dialogs.md` - Dialog freezing prevention, WebViewDialogHelper

### RegexFind Module
**File Patterns**: `**/regx-find-html/**`, `**/RegexFind/**`, `**/color-picker*`
- `regexfind_architecture.md` - Module structure, communication protocol, Hebrew/Arabic support
- `regexfind_color-system.md` - Color data structure, Word compatibility, theme colors

### Zayit Project
**File Patterns**: `**/vue-zayit/**`, `**/Zayit*`, `**/HebrewBooks*`, `**/PdfView*`
- `zayit_architecture.md` - Component overview, bridge patterns, key features
- `zayit_pdf-system.md` - Local PDF and Hebrew Books systems, virtual hosts, cache
- `zayit_reading-backgrounds.md` - Reading background colors, dark mode detection

### Installer System
**File Patterns**: `**/KleiKodeshVstoInstallerWpf/**`, `**/Build/**`
- `installer_system.md` - Two-tier architecture, version management, cleanup

### Update System
**File Patterns**: `**/UpdateChecker*`, `**/TaskpaneManager*`, `**/ThisAddIn*`
- `update_system.md` - TLS configuration, non-disruptive updates, deferred installation

## Adding New Steering Files

### Step-by-Step Process

1. **Identify the Category**
   - **Core**: Always-loaded fundamental principles → `core_[topic].md`
   - **Technology**: Language/framework specific → `[tech]_[aspect].md` (e.g., `vue_`, `csharp_`)
   - **Component**: Specific modules/features → `[component]_[aspect].md` (e.g., `regexfind_`, `zayit_`)
   - **System**: Infrastructure concerns → `[system]_[aspect].md` (e.g., `installer_`, `update_`)

2. **Create the File**
   ```
   Location: .steering/[prefix]_[descriptive-name].md
   Examples:
   - .steering/vue_components.md
   - .steering/zayit_database.md  
   - .steering/webview_security.md
   - .steering/build_automation.md
   ```

3. **Add Frontmatter**
   ```yaml
   ---
   inclusion: fileMatch
   fileMatchPattern: '**/pattern/**|**/*keyword*'
   ---
   ```
   
   **Common Patterns:**
   - Vue files: `**/*.vue|**/vite.config.*`
   - C# files: `**/*.cs`
   - Specific folders: `**/ComponentName/**`
   - File types: `**/*Dialog*|**/*Manager*`

4. **Update This Handler**
   Add the new file to the appropriate mapping section below with:
   - File patterns that trigger it
   - Brief description of what it covers

5. **Test the Pattern**
   - Open a file that should trigger the steering
   - Verify the steering loads automatically
   - Adjust fileMatchPattern if needed

### Content Guidelines

**Structure Each File:**
```markdown
---
inclusion: fileMatch
fileMatchPattern: 'your/pattern/here'
---

# [Component/Technology] [Aspect]

## CRITICAL: [Most Important Thing]
[Key issue or pattern that causes problems]

## [Section 1]
[Actionable guidance with code examples]

## [Section 2]  
[More specific patterns or solutions]
```

**Writing Principles:**
- **Lead with problems** - Start with what goes wrong
- **Provide solutions** - Show the correct way immediately
- **Use code examples** - Concrete patterns over abstract descriptions
- **Keep it short** - 50-100 lines max per file
- **Focus on "how"** - Not "why" or background theory

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