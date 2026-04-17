'use strict'
/**
 * build-nikkud-db.cjs
 *
 * Builds public/dicts/nikkud.db from the seforim DB.
 *
 * For every line in the seforim DB:
 *   1. Split raw HTML content by whitespace → raw tokens
 *   2. Per token: strip HTML tags + strip all non-Hebrew-word chars → nikud form
 *      (Hebrew word chars = Hebrew letters U+05D0-U+05EA + nikud U+05B0-U+05C7
 *       + cantillation U+0591-U+05AF + U+05C8-U+05CF + maqaf U+05BE + geresh U+05F3 + gershayim U+05F4)
 *   3. If nikud form is empty → skip
 *   4. stripped form = nikud form with all diacritic/cantillation chars removed
 *      (keep only U+05D0-U+05EA Hebrew letters)
 *   5. If stripped form is empty → skip
 *   6. INSERT OR IGNORE (stripped, nikkud) into nikkud.db
 *
 * Schema: word(stripped TEXT, nikkud TEXT, PRIMARY KEY (stripped, nikkud))
 */

const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

// ── Config ────────────────────────────────────────────────────────────────────

const SEFORIM_DB_PATH =
  process.env.SEFORIM_DB_PATH ||
  'C:/Users/Admin/AppData/Roaming/io.github.kdroidfilter.seforimapp/databases/seforim.db'

const OUT_PATH = path.resolve(__dirname, '../../public/dicts/nikkud.db')
const BATCH_SIZE = 50_000   // lines per transaction
const LOG_EVERY  = 500_000  // log progress every N lines

// ── Unicode ranges ────────────────────────────────────────────────────────────

// Characters to KEEP in the nikud form (Hebrew letters + all diacritics)
// Hebrew letters:      U+05D0–U+05EA
// Cantillation:        U+0591–U+05AF
// Nikud:               U+05B0–U+05C7
// Extra nikud/marks:   U+05C8–U+05CF
// Maqaf:               U+05BE
// Geresh/Gershayim:    U+05F3–U+05F4
const HEBREW_WORD_CHAR_RE = /[^\u05D0-\u05EA\u0591-\u05CF\u05BE\u05F3\u05F4]/g

// Characters that are diacritics (to strip when building the stripped form)
// Everything except the base Hebrew letters
const DIACRITIC_RE = /[^\u05D0-\u05EA]/g

// HTML tag stripper
const HTML_TAG_RE = /<[^>]*>/g

// ── Main ──────────────────────────────────────────────────────────────────────

function main() {
  if (!fs.existsSync(SEFORIM_DB_PATH)) {
    console.error('Seforim DB not found at:', SEFORIM_DB_PATH)
    console.error('Set SEFORIM_DB_PATH env var to override.')
    process.exit(1)
  }

  console.log('Opening seforim DB:', SEFORIM_DB_PATH)
  const seforim = new Database(SEFORIM_DB_PATH, { readonly: true })

  // Remove existing output and recreate
  if (fs.existsSync(OUT_PATH)) {
    fs.unlinkSync(OUT_PATH)
    console.log('Removed existing nikkud.db')
  }

  console.log('Creating nikkud.db at:', OUT_PATH)
  const nikkud = new Database(OUT_PATH)

  nikkud.exec(`
    CREATE TABLE word (
      stripped TEXT NOT NULL,
      nikkud   TEXT NOT NULL,
      PRIMARY KEY (stripped, nikkud)
    );
    CREATE INDEX idx_word_stripped ON word(stripped);
  `)

  const insert = nikkud.prepare('INSERT OR IGNORE INTO word (stripped, nikkud) VALUES (?, ?)')

  const totalLines = seforim.prepare('SELECT COUNT(*) AS cnt FROM line').get().cnt
  console.log(`Total lines to process: ${totalLines.toLocaleString()}`)

  // Stream lines in batches using LIMIT/OFFSET
  let offset = 0
  let totalInserted = 0
  let totalTokens = 0

  const runBatch = nikkud.transaction((rows) => {
    let inserted = 0
    for (const row of rows) {
      const content = row.content
      if (!content) continue

      // Split by whitespace
      const tokens = content.split(/\s+/)

      for (const rawToken of tokens) {
        if (!rawToken) continue

        // Step 1: strip HTML tags from token
        const noHtml = rawToken.replace(HTML_TAG_RE, '')

        // Step 2: strip all non-Hebrew-word chars → nikud form
        const nikudForm = noHtml.replace(HEBREW_WORD_CHAR_RE, '')

        // Step 3: skip if empty
        if (!nikudForm) continue

        // Step 4: strip diacritics → stripped form (Hebrew letters only)
        const strippedForm = nikudForm.replace(DIACRITIC_RE, '')

        // Step 5: skip if stripped form is empty
        if (!strippedForm) continue

        totalTokens++
        const info = insert.run(strippedForm, nikudForm)
        inserted += info.changes
      }
    }
    return inserted
  })

  while (offset < totalLines) {
    const rows = seforim
      .prepare('SELECT content FROM line LIMIT ? OFFSET ?')
      .all(BATCH_SIZE, offset)

    const inserted = runBatch(rows)
    totalInserted += inserted
    offset += rows.length

    if (offset % LOG_EVERY < BATCH_SIZE || offset >= totalLines) {
      console.log(
        `  Processed ${offset.toLocaleString()} / ${totalLines.toLocaleString()} lines` +
        ` | tokens: ${totalTokens.toLocaleString()}` +
        ` | new pairs: ${totalInserted.toLocaleString()}`,
      )
    }

    if (rows.length < BATCH_SIZE) break
  }

  // Final stats
  const wordCount = nikkud.prepare('SELECT COUNT(*) AS cnt FROM word').get().cnt
  const strippedCount = nikkud.prepare('SELECT COUNT(DISTINCT stripped) AS cnt FROM word').get().cnt

  console.log('\nDone.')
  console.log(`  Distinct (stripped, nikkud) pairs: ${wordCount.toLocaleString()}`)
  console.log(`  Distinct stripped forms:           ${strippedCount.toLocaleString()}`)

  seforim.close()
  nikkud.close()
}

main()
