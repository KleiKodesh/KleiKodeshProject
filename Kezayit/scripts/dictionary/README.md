# Dictionary Scripts

Scripts for building and maintaining the two dictionary databases used by the app.

## Databases

Both databases live in `public/dicts/`:

| File | Size | Description |
|------|------|-------------|
| `kezayit_dictionary.db` | ~2.4MB | Aramaic dictionaries + Hebrew abbreviations |
| `wikidictionary.db` | ~19.3MB | Hebrew Wiktionary (filtered for Orthodox Torah use) |

Original unfiltered backup of wikidictionary is in `data/dictionaries/`.

---

## kezayit_dictionary.db

Built from multiple sources:
- Aramaic entries from ToratEmet installation (`FinalDictionary.txt`)
- Hebrew abbreviations from wiki.jewishbooks.org.il
- Additional sources via `create-dictionary-db.cjs`

Schema: `source → sense → definition` (no examples, sections, or translations)

### Rebuild scripts
- `create-dictionary-db.cjs` — drops and recreates schema from scratch
- `import-aramaic.cjs` — imports Aramaic entries from FinalDictionary.txt
- `import-jewishbooks-abbrev.cjs` — imports Hebrew abbreviations

---

## wikidictionary.db

Built from Hebrew Wiktionary (he.wiktionary.org) via Special:Export API.

### Build pipeline
1. `create-wikidictionary-db.cjs` — creates empty schema
2. `import-wiktionary.cjs` — fetches and imports all Hebrew Wiktionary entries
3. `fetch-wiktionary-api.ps1` — PowerShell helper for fetching
4. `rebuild-wiki.cjs` — **main rebuild script** — filters + compacts from backup

### Filtering applied (in `rebuild-wiki.cjs`)
The rebuild script applies all filtering in one pass from the original backup:

**Senses excluded:**
- `netfree_blocked = 1` — words blocked by NetFree filter (checked via milog.co.il)
- Senses with blocked `filter_tag`: סלנג, עממי, כינוי גנאי, משלב חסר, שפת הדיבור, לשון מדוברת, דיבור, עגה צה"לית, עגה ירושלמית, צה"ל, צבא, נצרות, מיתולוגיה, אידאולוגיות
- Senses tagged `period_tag = 'חדשה'` with no Torah sense for the same headword

**Columns dropped from final DB:**
- `sense`: `netfree_blocked`, `heuristic_blocked`, `etymology`, `cross_ref`, `period_tag`
- `definition`: `filter_tag`
- `translation` table: dropped entirely (English/Arabic translations not needed)

**Post-build cleaning scripts** (run once after rebuild, results baked in):
- `clean-ktiv-male.cjs` — nulls out `ktiv_male` where identical to headword; strips wiki markup
- `strip-refs-from-db.cjs` — strips `<ref>` tags, URLs, wiki markup from all text fields
- `remove-modern-examples.cjs` — removes examples with non-Torah citation sources
- `remove-remaining-modern.cjs` — second pass removing remaining modern sources
- `cleanup-inappropriate.cjs` — removes/cleans inappropriate sexual/explicit content
- `cleanup2.cjs` — removes modern ideology, contraception, drugs, smoking terms
- `cleanup3.cjs` — removes modern politics, entertainment, media, sport terms
- `cleanup4.cjs` — removes remaining modern anatomy, media, sport, drug terms

### Content policy
The database is filtered for **Orthodox Jewish Torah study**:
- Keeps: Biblical Hebrew, Talmudic/Rabbinic Hebrew, Medieval Hebrew, standard modern Hebrew
- Keeps: Torah/halacha terms (even if explicit, with clean definitions)
- Removes: Sexual/inappropriate content, modern entertainment, sports, fashion, technology, secular ideology, drugs, smoking, gambling
- Removes: Modern political terms (democracy, communism, Zionism, etc.)
- Removes: Non-Torah citation examples (modern literature, songs, etc.)

### Maintenance
To rebuild from scratch:
```
node scripts/dictionary/rebuild-wiki.cjs
node scripts/dictionary/clean-ktiv-male.cjs
node scripts/dictionary/strip-refs-from-db.cjs
node scripts/dictionary/remove-modern-examples.cjs
node scripts/dictionary/remove-remaining-modern.cjs
node scripts/dictionary/cleanup-inappropriate.cjs
node scripts/dictionary/cleanup2.cjs
node scripts/dictionary/cleanup3.cjs
node scripts/dictionary/cleanup4.cjs
```

### Verification
```
node scripts/dictionary/validate-queries.cjs   # verify all SQL queries work
node scripts/dictionary/verify-dbs.cjs         # verify DB health and row counts
```

---

## npm scripts

| Script | Description |
|--------|-------------|
| `dict:create` | Recreate kezayit_dictionary.db schema |
| `dict:import-aramaic` | Import Aramaic entries |
| `dict:import-jewishbooks` | Import Hebrew abbreviations |
| `wikidict:create` | Recreate wikidictionary.db schema |
| `wikidict:fetch` | Fetch Wiktionary data (PowerShell) |
| `wikidict:import` | Import Wiktionary data |
| `wikidict:filter` | Create filtered wikidictionary from backup |
| `wikidict:orthodox` | Create orthodox-filtered wikidictionary |
| `wikidict:netfree` | Tag words blocked by NetFree (milog.co.il) |
| `wikidict:netfree-xml` | Tag words via Wiktionary XML export |
| `wikidict:tag-heuristic` | Tag words by Torah heuristic |

---

## C# deployment

Copy both DB files to `CSharpBackend/KezayitLib/bin/{Config}/kezayit/dicts/`:
- `kezayit_dictionary.db`
- `wikidictionary.db`

The C# host opens both read-only at startup via `AppViewer._dictDb` and `AppViewer._wikiDictDb`.
