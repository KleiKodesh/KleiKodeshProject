# expansion-routed.db

The search-expansion DB the app ships (FtsLib copies it next to every consumer as
`SearchExpansion/expansion-routed.db`). This file is the ONE working copy: edit it here,
in place, and commit it here.

## Slim version

This is the SLIM build (`build_meta.variant = slim`): only what the app's one reader,
`KitveiHakodesh.Core/SeforimDbFullTextSearch/SeforimDbFtsRelatedFormExpander.cs`, can use.
The FULL build (every row and table, for audits and analysis) is NOT here: it is an asset of
a `full-<date>-<commit>` release of KleiKodesh/SearchExpansion, recorded in that repo's
`full_db.json` (`python build/tools/fetch_full.py` downloads it). `build_meta.full_sha256`
names the FULL file this one was cut from.

The slim rule (`build/tools/make_slim.py` there), each step proven not to change any
expansion (`build/tools/slim_equivalence.py`: Python port of the reader, 0 differences):

- no `meta`, `bhsa_homograph`, `sqlite_stat1` tables and no `fold.source` (never read);
- no `exp` rows the reader always skips: unknown channel, `syn` rows from a source outside
  its trust policy, forms failing its drop-guard;
- no `fold` row where surface = lemma and it is the surface's only reading: a missing row
  already means "its own lemma, weight 1", and a single reading ignores its weight;
- no `fold` rows of surfaces that can never expand (no `exp` rows for the surface or any
  of its lemmas).

If the reader changes (new channel, trust rule, table), update make_slim.py first.

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

The builder repo is NOT kept on this machine: nothing for reproducing this DB lives here
except the DB itself. Clone it only when you need to update the DB, and delete the clone
when you are done (everything worth keeping must be pushed there first).

```bash
cd KleiKodeshProject                      # the folder that holds KitveiHakodesh/
git clone https://github.com/KleiKodesh/SearchExpansion.git
cd SearchExpansion
python build_all.py                       # writes the SLIM DB straight over this file
```

Then commit this DB here. The `SearchExpansion/` folder is gitignored here.

Large files that are not committed there (the full fastText model, the full Sefaria lexicon
export) are assets of its `local-archive-2026-11-03` release; fetch them with
`python build/tools/fetch_archive_assets.py <source id> <out dir>` (ids in `sources.json`).
