// Runs the real dibbur hamatchil rule on the labelled sample (see README.md).
// Data folder: $CLEAN_TEXT_DATA, else ./data next to this file (gitignored).
import { test } from 'vitest'
import { readFileSync, writeFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { stripNikkudFromHtml, CLEAN_TEXT } from '../../src/utils/hebrewTextProcessing'

const D = (process.env.CLEAN_TEXT_DATA ?? fileURLToPath(new URL('./data', import.meta.url))) + '/'
const DH = CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH
const DH_COMMENTARY = DH | CLEAN_TEXT.PLAIN_DIBBUR_HAMATCHIL

test('run', () => {
  const rows = JSON.parse(readFileSync(D + 'rows2.json', 'utf8'))
  const out = rows.map((r: { pre_html: string; ch: string; post: string }) => {
    const line = r.pre_html + String.fromCharCode(parseInt(r.ch, 16)) + r.post
    // Keys kept from the original run: o8 = main view, o72 = commentary.
    return { o8: stripNikkudFromHtml(line, DH), o72: stripNikkudFromHtml(line, DH_COMMENTARY), line }
  })
  writeFileSync(D + 'vout.json', JSON.stringify(out))
})
