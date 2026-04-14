# scripts/dictionary/

Scripts for building and maintaining the two offline dictionary databases shipped with the app.

## Databases

| File                       | Size   | Content                                                                   |
| -------------------------- | ------ | ------------------------------------------------------------------------- |
| `public/dictionary.db`     | ~22 MB | Aramaic dictionaries (ToratEmet) + Hebrew abbreviations (ויקיפדיה, קיצור) |
| `public/wikidictionary.db` | ~28 MB | Hebrew Wiktionary (ויקימילון) — all Hebrew word definitions               |

Both DBs share an identical schema so results can be merged directly without column mapping. The only intentional difference is `cache_entry` which exists only in `dictionary.db`.

## Scripts

### `create-dictionary-db.cjs`

Drops and recreates `public/dictionary.db` with a clean schema. Run this before re-importing Aramaic data. Does not import any data.

```
npm run dict:create
```

### `import-aramaic.cjs`

Imports Aramaic entries from `FinalDictionary.txt` (ToratEmet installation) into `public/dictionary.db`. Idempotent — safe to re-run. Requires the ToratEmet installation at the hardcoded path `C:\Users\Admin\Documents\ToratEmetInstall\Dictionaries\FinalDictionary.txt`.

```
npm run dict:import-aramaic
```

### `create-wikidictionary-db.cjs`

Drops and recreates `public/wikidictionary.db` with a clean schema. Called automatically by `import-wiktionary.cjs` — you rarely need to run this directly.

```
npm run wikidict:create
```

### `fetch-wiktionary-api.ps1`

PowerShell script that downloads all Hebrew Wiktionary pages via the MediaWiki API and saves them to `data/hewiktionary-pages.jsonl`. Must be run via PowerShell because Node.js HTTPS is blocked by NetFree on this network. Resumable — re-run after interruption, it picks up from where it left off using `data/hewiktionary-meta.json`.

```
npm run wikidict:fetch
```

This takes several hours for the full ~50k pages. Progress is printed as `N saved, M skipped | last: word`.

### `import-wiktionary.cjs`

Imports Hebrew Wiktionary pages into `public/wikidictionary.db`. Uses `Special:Export` (POST) to fetch wikitext in batches of 20, with an alphabet-iteration fallback when the allpages API is blocked. Recreates the schema fresh on every run. Resumable via `_meta` table inside the DB.

```
npm run wikidict:import
```

Takes ~2 hours for the full import. If interrupted, re-run — it resumes from the saved `apfrom` position.

### `check-wikidict.cjs`

Prints stats and sample data from `public/wikidictionary.db`. Useful for verifying a completed import.

```
node scripts/dictionary/check-wikidict.cjs
```

## Schema (both DBs)

```
source      id, label, lang, url
sense       id, headword, nikud, pos, binyan, shoresh, ktiv_male, etymology,
            cross_ref, period_tag, source_id, sense_order
definition  id, sense_id, text, filter_tag, def_order
example     id, definition_id, text, source
section     id, sense_id, name
section_item id, section_id, text, item_order
translation id, sense_id, lang, word
_meta       key, value  (import resume state)
```

### Key fields for filtering

`definition.filter_tag` — raw layer tag from wikitext (e.g. `גס`, `סלנג`, `המקרא`, `חז"ל`, `עברית חדשה`). NULL = untagged (always shown). Stored at import time so filtering rules can be changed without re-importing.

`sense.period_tag` — derived from `filter_tag` values, bucketed into 4 canonical periods: `מקרא` / `חז"ל` / `ביניים` / `חדשה` / NULL. NULL = modern standard Hebrew. Use this for efficient sense-level period filtering without scanning definitions.

`source.lang` — content language: `ארמית` / `ראשי תיבות` / `עברית`.

`source.url` — source URL. NULL for ToratEmet (physical files).

`sense.cross_ref` — abbreviation that was resolved at import time (Aramaic abbrev entries only). NULL in wikidictionary.db.

## Rebuilding from scratch

### dictionary.db

1. `npm run dict:create` — recreate schema
2. `npm run dict:import-aramaic` — import Aramaic + abbreviations

### wikidictionary.db

The import script handles schema creation internally, so just run:

```
npm run wikidict:import
```

If the network blocks the API (418 errors), the script falls back to alphabet iteration automatically. If it gets stuck, stop and re-run — it resumes.

## Network notes

This machine is behind NetFree which blocks binary file downloads from wikimedia.org and blocks Node.js HTTPS to some endpoints. The workarounds already in place:

- `fetch-wiktionary-api.ps1` uses PowerShell's WinHTTP stack (not blocked)
- `import-wiktionary.cjs` uses `Special:Export` POST + alphabet fallback (not blocked)
- Direct dump downloads (`.xml.bz2`) from dumps.wikimedia.org are blocked for GET but HEAD works
