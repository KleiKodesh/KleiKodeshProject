# Regex Find Tool - Vite Build Setup

This project has been configured to use Vite for building a compact single-file version of the Regex Find Tool.

## Project Structure

```
├── src/
│   └── main.js          # Main entry point for Vite
├── js/                  # Original JavaScript modules
├── css/                 # Stylesheets
├── Resources/           # Assets (SVGs, JSON)
├── index.html           # Vite entry HTML
├── regex-find.html      # Original HTML (preserved)
├── package.json         # Dependencies and scripts
├── vite.config.js       # Vite configuration
└── dist/
    └── index.html       # Built single file (56KB)
```

## Build Commands

```bash
# Install dependencies
npm install

# Development server
npm run dev

# Build single file
npm run build

# Preview built file
npm run preview
```

## Features

- **Single File Output**: Everything bundled into one HTML file (~56KB)
- **All Assets Inlined**: CSS, JavaScript, SVGs, and JSON data
- **ES Modules**: Modern JavaScript with proper imports/exports
- **Development Server**: Hot reload during development
- **Compact Build**: Minified and optimized for production

## Built File

The built file `dist/index.html` contains:
- All CSS styles inlined in `<style>` tags
- All JavaScript bundled and minified in `<script>` tags
- All SVG icons as data URLs
- Regex tips JSON data embedded in JavaScript
- Complete functionality in a single 56KB file

## Usage

The built file can be used standalone - just open `dist/index.html` in any modern browser or embed it in other applications.