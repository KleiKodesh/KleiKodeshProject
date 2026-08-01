# Hebrew lexicon expansion (`מילון עברי`)

Adds curated **general / everyday Hebrew vocabulary** (nouns, verbs, adjectives,
nature, science, household, food, animals, plants, …) to the KitveiHakodesh
dictionary, under the existing `source_kind` **`מילון עברי` (source id 6)**.

## Why

The bundled `מילון עברי` source is Torah-centric and thin on plain everyday
words. This fills that gap so a reader can look up ordinary Hebrew (שולחן, נהר,
מהר, כחול, צפרדע …) and get a short gloss + nikud.

## Scope guardrails

* ONLY general / secular vocabulary.
* **No** religious / halachic / theological terms, names of God, mitzvot,
  festivals-as-observances, prayer/Temple terms, or biblical figures.
* **No** vulgar / adult / violent / otherwise inappropriate content.

Curation is the real filter; `import_hebrew_lexicon.py` also has a defensive
whole-word blocklist as a backstop.

## Layout

```
scripts/hebrew_lexicon/
  import_hebrew_lexicon.py     the importer (see below)
  terms/batch_*.json           the curated data — the human-readable record
  baseline_source6/<db>.json   snapshot of source-6 (headword,text) pairs that
                               pre-existed my first import — the "protected" set
  README.md                    this file
```

Each `terms/batch_*.json` is a JSON array of:

```json
{ "hw": "שולחן", "nikud": "שֻׁלְחָן", "defs": ["רהיט בעל משטח ורגליים"] }
```

`nikud` is optional (per **sense** — homographs like מַעֲלָה/מַעְלָה,
שֶׁבַע/שָׂבֵעַ are kept distinct). `defs` is a list of short glosses.

## How it writes

Targets **both** committed DB copies:

* `CSharpBackend/KitveiHakodeshService/Dictionary/Dictionary.db`  (the service)
* `vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db`   (the frontend)

Behaviour:

* **Purely additive + idempotent** — inserts a sense only when no
  `(word_id, text, source_id=6)` row already exists. Re-running never duplicates
  and **never deletes** the pre-existing curated entries.
* **Safe nikud correction** — for a row I own (in the dataset, not in the
  protected baseline) it will UPDATE nikud to match the dataset; baseline rows
  are never touched.
* On first run it snapshots the pre-existing source-6 pairs to
  `baseline_source6/` so a future cleanup can remove exactly my additions.

## Run

```bash
cd scripts/hebrew_lexicon
python import_hebrew_lexicon.py --dry-run      # report only, no writes
python import_hebrew_lexicon.py                # write both DBs
python import_hebrew_lexicon.py --db service   # one target only
```

## Making a running app see the change

The service reads its **bin copy** of `Dictionary.db`; the `.csproj` copies
`Dictionary/Dictionary.db` to the output on build. So after importing, **rebuild
and restart the service** (and rebuild the frontend, which copies `public/` to
`dist/`) for a running dev instance to pick up the new terms.

## Tracking / reverting my additions

* Every added sense is tagged `source_id = 6` (`מילון עברי`).
* The exact set is the union of `terms/batch_*.json`.
* `baseline_source6/<db>.json` records what pre-existed, so
  `myAdditions = (source-6 rows matching the dataset) − baseline`.
