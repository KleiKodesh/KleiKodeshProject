// Rebuilds the app's .ico files from their source art, with every size Windows asks for
// (16-64 for title bars, taskbar, Alt+Tab, Explorer lists; 256 for large views), so Windows
// never has to shrink one big picture and blur it.
//
// Run from the KleiKodeshProject folder:  node Build/icons/make-icons.mjs
// Uses Playwright's Chromium from KitveiHakodesh/vue-frontend (headless, no window).
//
// Small sizes are stored as 32-bit BMP (every Windows icon reader, .NET Framework included),
// 256 as PNG (the standard for the large size).

import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const ROOT = process.cwd()
const require = createRequire(path.join(ROOT, 'KitveiHakodesh/vue-frontend/package.json'))
const { chromium } = require('playwright')

const SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
const PNG_SIZE = 500

// source art -> the .ico files built from it (copies of the same icon kept in step)
const ICONS = [
  {
    source: 'KleiKodeshVsto/Resources/KitveiHakodesh.svg',
    targets: [
      'KitveiHakodesh/CSharpBackend/KitveiHakodeshDesktopApp/KitveiHakodesh.ico',
      'KitveiHakodesh/vue-frontend/public/favicon.ico',
      'KleiKodeshVsto/Resources/KitveiHakodesh.ico',
      'KleiKodeshVsto/Resources/favicon.ico',
    ],
  },
  {
    source: 'KleiKodeshVsto/Resources/KitveiHakodeshLarge.svg',
    targets: [
      'KitveiHakodesh/CSharpBackend/KitveiHakodeshDesktopApp/KitveiHakodeshLarge.ico',
      'KleiKodeshVsto/Resources/KitveiHakodeshLarge.ico',
    ],
  },
  {
    // Redrawn from the old 500px KleiKodesh_Main.png: box and wrench exact shapes, quill fitted curves.
    source: 'KleiKodeshVsto/Resources/KleiKodesh_Main.svg',
    targets: ['KleiKodeshVsto/KleiKodesh_Main.ico', 'Build/installer-rs/assets/kleikodesh.ico'],
    // 500px PNG copies (add-in panes, about page, installer screens)
    pngs: [
      'KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/KleiKodesh_Main.png',
      'KitveiHakodesh/vue-frontend/public/images/KleiKodesh_Main.png',
      'KleiKodeshVsto/KleiKodesh_Main.png',
      'KleiKodeshVsto/Resources/KleiKodesh_Main.png',
      'Build/installer-rs/ui/images/kleikodesh-logo.png',
    ],
  },
  { source: 'Build/installer-rs/ui/images/calendar.svg', targets: ['Build/installer-rs/assets/calendar.ico'] },
  { source: 'Build/installer-rs/assets/tutor-icon.svg', targets: ['Build/installer-rs/assets/tutor.ico'] },
]

// Draws the source at one size and returns straight (non-premultiplied) RGBA pixels and a PNG.
// Big pictures are halved step by step before the last resize: one big jump skips pixels.
async function renderAll(page, file, sizes = SIZES) {
  const bytes = fs.readFileSync(path.join(ROOT, file))
  const mime = file.endsWith('.svg') ? 'image/svg+xml' : 'image/png'
  const url = `data:${mime};base64,${bytes.toString('base64')}`
  return page.evaluate(async ({ url, sizes, isSvg }) => {
    const img = new Image()
    img.src = url
    await img.decode()
    const out = []
    for (const size of sizes) {
      let src = img
      let w = isSvg ? size : img.naturalWidth
      if (!isSvg) {
        while (w / 2 >= size) {
          const c = document.createElement('canvas')
          c.width = c.height = Math.round(w / 2)
          const g = c.getContext('2d')
          g.imageSmoothingQuality = 'high'
          g.drawImage(src, 0, 0, c.width, c.height)
          src = c
          w = c.width
        }
      }
      const c = document.createElement('canvas')
      c.width = c.height = size
      const g = c.getContext('2d', { colorSpace: 'srgb' })
      g.imageSmoothingQuality = 'high'
      g.drawImage(src, 0, 0, size, size)
      const rgba = Array.from(g.getImageData(0, 0, size, size).data)
      const png = c.toDataURL('image/png').split(',')[1]
      out.push({ size, rgba, png })
    }
    return out
  }, { url, sizes, isSvg: mime === 'image/svg+xml' })
}

// 32-bit BMP icon entry: header, bottom-up BGRA rows, then the 1-bit AND mask (all zero:
// the alpha channel decides transparency).
function bmpEntry(size, rgba) {
  const header = Buffer.alloc(40)
  header.writeUInt32LE(40, 0)
  header.writeInt32LE(size, 4)
  header.writeInt32LE(size * 2, 8)
  header.writeUInt16LE(1, 12)
  header.writeUInt16LE(32, 14)
  header.writeUInt32LE(size * size * 4, 20)
  const pixels = Buffer.alloc(size * size * 4)
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const s = (y * size + x) * 4
      const t = ((size - 1 - y) * size + x) * 4
      pixels[t] = rgba[s + 2]
      pixels[t + 1] = rgba[s + 1]
      pixels[t + 2] = rgba[s]
      pixels[t + 3] = rgba[s + 3]
    }
  }
  const maskRow = Math.ceil(size / 32) * 4
  return Buffer.concat([header, pixels, Buffer.alloc(maskRow * size)])
}

function buildIco(frames) {
  const entries = frames.map(f => ({ size: f.size, data: f.size >= 256 ? Buffer.from(f.png, 'base64') : bmpEntry(f.size, f.rgba) }))
  const head = Buffer.alloc(6 + 16 * entries.length)
  head.writeUInt16LE(1, 2)
  head.writeUInt16LE(entries.length, 4)
  let offset = head.length
  entries.forEach((e, i) => {
    const o = 6 + 16 * i
    head[o] = e.size >= 256 ? 0 : e.size
    head[o + 1] = e.size >= 256 ? 0 : e.size
    head.writeUInt16LE(1, o + 4)
    head.writeUInt16LE(32, o + 6)
    head.writeUInt32LE(e.data.length, o + 8)
    head.writeUInt32LE(offset, o + 12)
    offset += e.data.length
  })
  return Buffer.concat([head, ...entries.map(e => e.data)])
}

const browser = await chromium.launch()
try {
  const page = await browser.newPage()
  for (const icon of ICONS) {
    const ico = buildIco(await renderAll(page, icon.source))
    for (const target of icon.targets) {
      fs.writeFileSync(path.join(ROOT, target), ico)
      console.log(`${target}  <-  ${icon.source}  (${SIZES.join(', ')}; ${ico.length} bytes)`)
    }
    if (icon.pngs) {
      const [big] = await renderAll(page, icon.source, [PNG_SIZE])
      for (const target of icon.pngs) {
        fs.writeFileSync(path.join(ROOT, target), Buffer.from(big.png, 'base64'))
        console.log(`${target}  <-  ${icon.source}  (${PNG_SIZE}px PNG)`)
      }
    }
  }
} finally {
  await browser.close()
}
