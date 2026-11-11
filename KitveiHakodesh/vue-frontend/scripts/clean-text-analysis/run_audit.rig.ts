// Runs the real cleanHebrewText per option on the audit sample (see README.md).
// Data folder: $CLEAN_TEXT_DATA, else ./data next to this file (gitignored).
import { test } from 'vitest'
import { readFileSync, writeFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { cleanHebrewText } from '../../src/utils/hebrewTextCleaning'
import { CLEAN_TEXT } from '../../src/utils/hebrewTextProcessing'
import { PUNCTUATION_PRESETS } from '../../src/utils/punctuationModes'

const D = (process.env.CLEAN_TEXT_DATA ?? fileURLToPath(new URL('./data', import.meta.url))) + '/'
const level = (key: string) => PUNCTUATION_PRESETS.find((p) => p.key === key)!.value
const MARKS = CLEAN_TEXT.TEAMIM | CLEAN_TEXT.NIKKUD

test('run', () => {
  const rows = JSON.parse(readFileSync(D + 'au.json', 'utf8'))
  // Keys kept from the original run (before per-mark modes): o16 was "normalize
  // punctuation" (now the Classic level), o32 was "remove punctuation" (now None).
  writeFileSync(D + 'auout.json', JSON.stringify(rows.map((r: { line: string }) => ({
    ...r,
    o1: cleanHebrewText(r.line, CLEAN_TEXT.TEAMIM),
    o2: cleanHebrewText(r.line, CLEAN_TEXT.NIKKUD),
    o4: cleanHebrewText(r.line, CLEAN_TEXT.MAQAF),
    o16: cleanHebrewText(r.line, level('classic')),
    o18: cleanHebrewText(r.line, MARKS | level('classic')),
    o32: cleanHebrewText(r.line, MARKS | level('none')),
  }))))
})
