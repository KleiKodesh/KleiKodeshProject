# KleiKodesh Project Steering System

## Overview
This directory contains targeted guidance files for the KleiKodesh project - a Microsoft Word VSTO add-in with Hebrew text processing tools.

## File Organization

### Always Loaded (Core Knowledge)
- `core_development.md` - Essential development principles and rules
- `core_build.md` - Build system requirements and processes
- `core_communication.md` - C# ↔ JavaScript communication patterns

### Component-Specific (Auto-loaded by file patterns)
- `regexfind_*.md` - RegexFind module guidance
- `zayit_*.md` - Zayit project guidance  
- `installer_*.md` - Installation system guidance
- `webview_*.md` - WebView2 integration patterns

### Technology-Specific (Auto-loaded by file patterns)
- `vue_*.md` - Vue.js development standards
- `csharp_*.md` - C# coding patterns
- `ui_*.md` - User interface guidelines

## Usage
Steering files automatically load based on frontmatter inclusion patterns. When working on specific components, relevant guidance appears automatically.

## Key Principles
- **Concise and actionable** - Only essential information
- **Problem-focused** - Address common issues and patterns
- **Up-to-date** - Reflects current codebase state
- **Functional** - Organized by what you're working on, not abstract concepts