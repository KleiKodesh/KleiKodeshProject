# Dictionary.db

The dictionary database the app ships (copied to every output as `Dictionary\Dictionary.db`).
This file is the ONE working copy: edit it here, in place, and commit it here.

## Built by

[KleiKodesh/KitveiHakodeshDictionary](https://github.com/KleiKodesh/KitveiHakodeshDictionary) (public).
That repo holds everything needed to reproduce this DB:

- `source/` - the DB as readable TSV files (one senses file per source_kind); the source of truth
- `sources.json` - every source with its origin URL, pin (commit / date), sha256 and license
- `fetch/` - downloads the re-fetchable upstream files and checks their sha256
- `import/` - the import scripts (Otzaria acronyms and Aramaic, Laaz Rashi, Hebrew lexicon,
  seforim headword index, synonym links) and their audit data
- `build.py` / `export.py` / `verify.py` - build the DB from `source/`, write a DB back to
  `source/`, row-diff two DBs
- CI on every push: builds the DB and runs a round-trip check (no releases)

## Updating this DB

```bash
cd KleiKodeshProject                      # the folder that holds KitveiHakodesh/
git clone https://github.com/KleiKodesh/KitveiHakodeshDictionary.git
cd KitveiHakodeshDictionary
python build.py                           # writes straight over this Dictionary.db
```

Cloned there, `build.py`, `export.py` and every import script read and write this file
directly (no copying). To change the content: run an import script (or edit this DB), then
`python export.py` to write the change into `source/`, and commit `source/` in that repo and
this DB here. The `KitveiHakodeshDictionary/` folder is gitignored here.
