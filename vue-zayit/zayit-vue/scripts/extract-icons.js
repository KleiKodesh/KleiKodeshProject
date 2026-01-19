// Extract only the icons we use from the full Fluent icon set
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import fluentIcons from '@iconify-json/fluent/icons.json' with { type: 'json' };

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// List of all icons used in the app
const usedIcons = [
  'spinner-ios-20-regular',
  'spinner-ios-20-filled',
  'dismiss-16-regular',
  'dismiss-16-filled',
  'chevron-left-28-regular',
  'chevron-left-24-regular',
  'chevron-right-28-regular',
  'book-open-24-regular',
  'book-open-28-regular',
  'book-20-regular',
  'text-bullet-list-tree-24-regular',
  'more-vertical-28-regular',
  'text-align-right-24-regular',
  'text-align-justify-24-regular',
  'flash-28-regular',
  'leaf-24-regular',
  'weather-sunny-24-regular',
  'dark-theme-24-regular',
  'settings-28-regular',
  'info-28-regular',
  'library-28-regular',
  'document-pdf-28-regular',
  'open-28-regular',
  'search-28-filled',
  'search-28-regular',
  'panel-bottom-expand-20-filled',
  'panel-bottom-contract-20-filled',
  'home-28-regular',
  'add-16-filled',
  'arrow-download-20-regular',
  'error-circle-20-regular',
  'filter-28-regular',
  'eye-lines-28-regular'
];

// Extract only the icons we need
const extractedIcons = {
  prefix: 'fluent',
  icons: {},
  width: 24,
  height: 24
};

usedIcons.forEach(iconName => {
  if (fluentIcons.icons[iconName]) {
    extractedIcons.icons[iconName] = fluentIcons.icons[iconName];
  } else {
    console.warn(`Warning: Icon "${iconName}" not found in Fluent icon set`);
  }
});

console.log(`Extracted ${Object.keys(extractedIcons.icons).length} icons out of ${usedIcons.length} requested`);

// Write to TypeScript file
const outputPath = path.join(__dirname, '..', 'src', 'utils', 'iconify-offline.ts');
const tsContent = `// Offline icon configuration for Iconify
// Auto-generated - do not edit manually
// Run: node scripts/extract-icons.js to regenerate

import { addCollection } from '@iconify/vue'

const fluentIcons = ${JSON.stringify(extractedIcons, null, 2)}

export function initializeOfflineIcons() {
  addCollection(fluentIcons)
}
`;

fs.writeFileSync(outputPath, tsContent, 'utf8');
console.log(`Written to ${outputPath}`);
