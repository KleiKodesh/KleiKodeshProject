// Extract only the icons we use from the full Fluent icon set
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import fluentIcons from '@iconify-json/fluent/icons.json' with { type: 'json' };
import fluentColorIcons from '@iconify-json/fluent-color/icons.json' with { type: 'json' };

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// List of all icons used in the app
const usedIcons = [
  'spinner-ios-20-regular',
  'spinner-ios-20-filled',
  'dismiss-16-regular',
  'dismiss-16-filled',
  'dismiss-24-regular',
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
  'search-24-regular',
  'search-sparkle-24-filled',
  'panel-bottom-expand-20-filled',
  'panel-bottom-contract-20-filled',
  'panel-bottom-20-filled',
  'panel-bottom-20-regular',
  'home-28-regular',
  'add-16-filled',
  'add-24-regular',
  'arrow-download-20-regular',
  'error-circle-20-regular',
  'filter-28-regular',
  'filter-24-regular',
  'eye-lines-28-regular',
  'apps-28-regular',
  'checkmark-24-regular',
  'edit-24-regular',
  'delete-24-regular'
];

// List of fluent-color icons used in the app
const usedColorIcons = [
  'settings-24'
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

// Extract fluent-color icons
const extractedColorIcons = {
  prefix: 'fluent-color',
  icons: {},
  width: 24,
  height: 24
};

usedColorIcons.forEach(iconName => {
  if (fluentColorIcons.icons[iconName]) {
    extractedColorIcons.icons[iconName] = fluentColorIcons.icons[iconName];
  } else {
    console.warn(`Warning: Icon "${iconName}" not found in Fluent Color icon set`);
  }
});

console.log(`Extracted ${Object.keys(extractedColorIcons.icons).length} color icons out of ${usedColorIcons.length} requested`);

// Write to TypeScript file
const outputPath = path.join(__dirname, '..', 'src', 'utils', 'iconify-offline.ts');
const tsContent = `// Offline icon configuration for Iconify
// Auto-generated - do not edit manually
// Run: node scripts/extract-icons.js to regenerate

import { addCollection } from '@iconify/vue'

const fluentIcons = ${JSON.stringify(extractedIcons, null, 2)}

const fluentColorIcons = ${JSON.stringify(extractedColorIcons, null, 2)}

export function initializeOfflineIcons() {
  addCollection(fluentIcons)
  addCollection(fluentColorIcons)
}
`;

fs.writeFileSync(outputPath, tsContent, 'utf8');
console.log(`Written to ${outputPath}`);
