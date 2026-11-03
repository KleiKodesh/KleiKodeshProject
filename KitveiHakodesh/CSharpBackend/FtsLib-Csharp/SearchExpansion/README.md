# expansion-routed.db

The search-expansion DB the app ships (FtsLib copies it next to every consumer as
`SearchExpansion/expansion-routed.db`). This file is the ONE working copy: edit it here,
in place, and commit it here.

## Built by

[KleiKodesh/SearchExpansion](https://github.com/KleiKodesh/SearchExpansion) (private).
That repo holds everything needed to reproduce this DB:

- `inputs/` - every data input (corpus stats, lexicons, Sefaria, BHSA, Targum bridge, audits)
- `sources.json` - every input with its origin, pin, sha256 and license
- `build/` - the generator and its tools; `build_all.py` runs the whole build
- `analysis/`, `research/`, `RESEARCH.md` - audits and evaluations
- CI on every push: checks the inputs against `sources.json` and builds (no releases)

It reads the dictionary from `KitveiHakodesh.Core/Dictionary/Dictionary.db` (see the README
there), or, outside KleiKodeshProject, from a sibling clone of KitveiHakodeshDictionary.

## Updating this DB

```bash
cd KleiKodeshProject                      # the folder that holds KitveiHakodesh/
git clone https://github.com/KleiKodesh/SearchExpansion.git
cd SearchExpansion
python build_all.py                       # writes straight over this expansion-routed.db
```

Then commit this DB here. The `SearchExpansion/` folder is gitignored here.
