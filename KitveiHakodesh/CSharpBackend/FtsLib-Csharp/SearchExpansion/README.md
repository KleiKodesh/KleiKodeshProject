# SearchExpansion — related-forms query expansion for FtsLib

`expansion-routed.db` is the artifact that powers the **חיפוש מורחב** option in
full-text search: each plain Hebrew query word is rewritten to
`word | alt1 | alt2 …` (FtsLib's native OR syntax) so a verse matches whether it
uses the typed form, another form of the same lexeme, or a vetted synonym.

This folder holds **one canonical copy** of that artifact plus everything needed
to regenerate it. Both FtsLib projects declare the `.db` as `Content` with
`PreserveNewest`, so it lands in every consumer's output as
`SearchExpansion/expansion-routed.db` and is read at runtime **relative to the
binary** — replace the file here, rebuild, and every consumer picks it up.

```
SearchExpansion/
  expansion-routed.db     <- the shipped artifact (regenerate, commit, rebuild)
  README.md               <- this file
  build/                  <- the generator (Python 3.11+, offline, no packages)
    expansion.py              guard stack + channel assembly (the real logic)
    build_expansion_table.py  per-corpus table builder
    build_expansion_routed.py routed merge -> the shipped artifact
    targum_bridge.csv         mined Hebrew<->Aramaic pairs (input, pre-built)
    tools/
      masked.py               masked stdout (see "Output masking")
      sanitize.py             make a Hebrew-free copy of any file
      -- curated INPUTS (committed; edit to change the artifact) --
      syn_blocklist.txt       bad dictionary synonym links to ignore
      aramaic_fold_manual.tsv human-verified biblical-Aramaic folds (wins over rules)
      aramaic_fold_blocklist.txt surfaces excluded from rule-based folding
      -- generated OUTPUTS (gitignored; nothing reads them back) --
      aramaic_fold_overrides.txt  what the Aramaic rules decided this run,
                                  written for human review (see "Auditing")
      hashmap.tsv                 [H:xxxx] -> word decode map
```

The distinction matters when tuning: the three curated files are **inputs** the
build consults, so editing them changes the next artifact. The overrides file is
a **report** the build emits — regenerated from scratch every run, useful only
to review what the rules did.

## Schema

```sql
fold (surface TEXT PRIMARY KEY, lemma TEXT, source TEXT)   -- typed form -> lexeme
exp  (lemma TEXT, rank INTEGER, form TEXT, channel TEXT, source TEXT,
      PRIMARY KEY (lemma, rank))                            -- ranked alternatives
meta (key TEXT PRIMARY KEY, value TEXT)                     -- design + policy notes
```

`channel` is `infl` (another form of the same lexeme), `syn` (dictionary
synonym), or `bridge` (Targum equivalent). `source` is `tanach` (the validated
register) or `library` (the rest of the corpus).

**Consumer policy — enforced in code, not by the data:** synonym rows are
trusted only where `source='tanach'`; inflection and bridge rows are used from
both. See `SearchExpansionService.cs` (net10 service) and `SearchExpansion.cs`
(net48 hosted twin) — the two are deliberate twins; change both together.

## Prerequisites

The generator reads three databases that are **not** in this repo. All are
read-only and paths are constants at the top of `expansion.py`:

| Input | Default path | Purpose |
| --- | --- | --- |
| seforim corpus | `C:\ProgramData\otzaria\books\seforim.db` | attested surface forms + frequencies |
| lexicon | `C:\Users\Public\Documents\Dictionary\Backup\lexical.db` | surface -> lexeme groups |
| dictionary | `%LOCALAPPDATA%\KleiKodesh\KitveiHakodesh\dictionary\KitveiHakodesh_dictionary.db` | synonym links + gloss texts for the sense gate |

Python 3.11+ with only the standard library (`sqlite3`, `re`, `csv`). No pip
install, no network.

## Regenerating the artifact

Run from inside `build/`. Two commands, ~10 minutes total (the second dominates
— it tokenizes the whole library):

```bash
cd build

# 1. per-corpus tables
python build_expansion_table.py                # -> expansion-tanach.db   (~4.5 MB, seconds)
python build_expansion_table.py --corpus all   # -> expansion-seforim.db  (~63 MB, ~8 min)

# 2. routed merge -> the shipped artifact
python build_expansion_routed.py               # -> expansion-routed.db   (~69 MB)

# 3. publish + rebuild so every consumer picks it up
mv expansion-routed.db ..
```

Then rebuild any consumer (`dotnet build` in `KitveiHakodeshService` or
`KitveiHakodeshLib`) and confirm `SearchExpansion/expansion-routed.db` appears
next to the binary. Commit the new `.db` — it is the shipped artifact.

Expected console summary from step 2 (numbers drift with the input DBs):

```
aramaic-line classifier: 281 Aramaic lines, 380 Hebrew lines in Daniel/Ezra
manual fold entries applied: 31
aramaic register guard: 191 distinctive surfaces, 85 proclitic folds, ...
fold: 39,764 tanach + 1,380,914 library-only
exp rows: {'tanach': ..., 'library': ..., 'merged_forms': ...}
```

## Reproducibility — the artifact tracks its inputs

The build is deterministic **for fixed inputs**, but the inputs are live
databases outside this repo, so regenerating at a later date legitimately
produces slightly different numbers. Verified 2026-08-04: rebuilding from an
untouched checkout reproduced the fold table exactly (1,420,678 rows) and the
inflection/bridge channels within a few dozen rows, while the synonym channel
moved by ~1,900 rows (~1%) because `seforim.db` had been updated in between —
the synonym shape gate depends on corpus frequencies, so a corpus refresh
shifts which alternatives qualify.

Practical consequences:

- A small diff after regeneration is expected, not a defect. Compare channel
  counts (`select channel, count(*) from exp group by channel`) rather than
  file hashes.
- A LARGE diff means an input changed materially (or a knob was edited) — audit
  a sample before committing the new artifact.
- Record which corpus a shipped artifact was built from if that ever matters
  for a bug report; `meta` holds the design/policy notes but not input versions.

## Tuning knobs

Editing these changes the artifact, so re-verify a sample afterwards (see
"Auditing" below).

- `expansion.py` — `MATCH_MAX_RATE` (a form too frequent to be a useful
  alternative; scales with corpus size), `GLOSS_DF_CAP` / `GLOSS_MIN_TOKENS`
  (the synonym sense gate: a synonym is dropped when both glosses are
  substantial yet share no defining word).
- `build_expansion_routed.py` — thin-anchor gate (a lexeme with <=3 validated
  forms does not receive library forms; without it, frequent rabbinic
  homographs flood rare biblical lexemes), the Aramaic proclitic set, and the
  emphatic-state rules.
- `tools/*.txt` / `*.tsv` — curation. The manual fold file wins over every
  rule, so it is the right place to pin a human decision permanently.
- `SearchExpansionService.PerTermLimit` (C#, both twins) — how many
  alternatives a single query word may contribute. This is an expansion-breadth
  knob, **not** a result cap.

## Auditing a regenerated artifact

Word-level quality cannot be judged from counts. Start from
`build/tools/aramaic_fold_overrides.txt`, which the build writes with every
`surface -> lemma` decision its Aramaic rules made — that list is what the
review rounds below were driven from.

The audit method that produced the current artifact: render candidate rows to a local HTML page and have a
Hebrew-literate reviewer judge them (fold correctness per surface, channel
quality per row), reporting by row number. The rule sets are small enough that
every decision can be reviewed rather than sampled — the biblical-Aramaic fold
list was verified exhaustively that way, and the surviving errors were pinned
into `tools/aramaic_fold_manual.tsv`.

Full findings, measured trade-offs, and the rejected alternatives live in
`KitveiHakodesh/research/word-association/FINDINGS.md` (sections 26–27c).

## Output masking

Every script here routes stdout/stderr through `tools/masked.py`, which replaces
Hebrew runs with stable `[H:xxxx]` placeholders and appends the real words to a
local `tools/hashmap.tsv` (git-ignored). This is a **hard requirement**, not a
preference: the network content filter in use scans payloads and the corpus
itself contains blocked vocabulary, so raw corpus words in program output have
already killed a working session. Add `import masked; masked.install()` to any
new script here, and never paste corpus text into a chat, ticket, or commit
message. Full rules: `.kiro/steering/agent-behavior.md`.
