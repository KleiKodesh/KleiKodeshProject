# How to add senses to the KitveiHakodesh dictionary

This folder holds **`Dictionary.db`** — the dictionary the service serves for
word look-ups. This note explains how to add more words/senses to it correctly.

> **There are TWO copies of this database — keep them identical:**
> - `CSharpBackend/KitveiHakodeshService/Dictionary/Dictionary.db`  ← this one (service)
> - `vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db`   (frontend)
>
> Every change must be applied to **both**.

---

## 1. Schema

```
word(id, headword)                                  -- headword, unvocalized, UNIQUE
sense(id, word_id, nikud, text, source_id)          -- one meaning of a word
source_kind(id, name)                               -- which dictionary a sense came from
link(word_id, target_id, kind_id), link_kind(...)   -- related-word graph (rarely touched)
```

- `word.headword` — the word **without** nikud (e.g. `שולחן`).
- `sense.nikud`   — the **vocalized** form of the headword for THIS sense (e.g. `שֻׁלְחָן`). May be NULL.
- `sense.text`    — a short Hebrew gloss / definition (median ~15 chars).
- `sense.source_id` — which source the sense belongs to (see below).

### Sources (`source_kind`)
| id | name | what it is |
|----|------|------------|
| 1 | מילון ארמי עברי | Aramaic–Hebrew (Talmudic) |
| 2–4, 9 | …ראשי תיבות | acronym/abbreviation lists |
| 5 | המכלול | encyclopedic |
| 6 | **מילון עברי** | **general Hebrew — add new general vocabulary here** |
| 7 | ספר השרשים לרד"ק | roots |
| 8 | לעזי רש"י | Rashi's Old-French glosses |

**General Hebrew words go under `source_id = 6` (`מילון עברי`).**

---

## 2. THE INCLUSION BAR — what belongs in the dictionary

A dictionary is for words a reader **does not already know**. Before adding a word,
ask: *would an educated adult reader of Hebrew texts need to look this up?*

**DO add** — rare / literary / archaic / technical words:
> משטמה, פורענות, תעצומה, יגון, עדנה, מרגוע, נשגב, נקלה, זניח, זיקק, מיגר, נכסף,
> אכסדרה, גזוזטרה, מרזב, מגדלור, ברקת, בדולח, ברדלס, דוכיפת, ראם, אזוב, לענה, שקמה

**Do NOT add** — obvious everyday vocabulary any adult knows:
> ❌ concrete nouns: כיסא, שולחן, מטבח, מעיל, מכונית, רופא
> ❌ common verbs: הלך, אכל, חייך, קנה, בישל, התחיל, סייע
> ❌ common adjectives: גדול, מהיר, יפה, חשוב, מרהיב, פתוח
> ❌ colors, numbers, basic body parts, toddler words

Also skip "formal-but-known" words (מרהיב, התלהב, מסובך) — a synonym an adult
already knows is not worth an entry. When in doubt, **leave it out.**

### Appropriateness (this is a Torah-study app)
- No vulgar / adult / immodest / intimate terms.
- Don't invent halachic/religious rulings or add religiously-specific items that
  need halachic precision. Neutral general vocabulary only.
- **Never** delete or rewrite the pre-existing entries (the "baseline") — they
  include curated religious/technical content (Ark, Shema, שבעת המינים, alphabet
  letters, nikud names, …). The tooling below protects them automatically.

---

## 3. Recommended way to add senses — the importer

Local tooling lives in **`scripts/hebrew_lexicon/`** (this is gitignored — it's a
build tool; the committed artifact is `Dictionary.db` itself).

1. **Edit the word list:** `scripts/hebrew_lexicon/terms/batch_lexicon.json`
   Each entry:
   ```json
   { "hw": "משטמה", "nikud": "מַשְׂטֵמָה", "defs": ["שנאה עמוקה"] }
   ```
   - `hw` — headword, no nikud. `nikud` — vocalized form (or `null` if unsure).
   - `defs` — list of short glosses (usually one).
   - You may add extra `.json` files named `batch_*.json` in `terms/`.

2. **Dry-run** to preview, then apply to **both** DBs:
   ```
   cd scripts/hebrew_lexicon
   python import_hebrew_lexicon.py --dry-run     # preview counts, no writes
   python import_hebrew_lexicon.py               # write both DB copies
   ```

3. To **remove** words later: delete them from the batch file (or add them to
   `exclude.json`) and run with reconcile:
   ```
   python import_hebrew_lexicon.py --prune
   ```
   `--prune` makes the DBs match the batch files exactly, deleting only words the
   tool itself added (the baseline is never touched).

The importer is **safe and idempotent**: it stores nikud **per sense** (so
homographs like שֶׁבַע / שָׂבֵעַ stay distinct), never duplicates, never deletes
pre-existing content, and writes both DB copies. See its docstring and
`scripts/hebrew_lexicon/README.md` for details.

---

## 4. Manual alternative (raw SQL)

If you add a sense by hand (no `sqlite3` CLI on this box — use Python's `sqlite3`),
do it in **both** DB files:

```python
import sqlite3
db = sqlite3.connect(r"Dictionary.db")
cur = db.cursor()
hw, nikud, text = "משטמה", "מַשְׂטֵמָה", "שנאה עמוקה"
cur.execute("SELECT id FROM word WHERE headword=?", (hw,))
row = cur.fetchone()
word_id = row[0] if row else cur.execute(
    "INSERT INTO word(headword) VALUES(?)", (hw,)).lastrowid
# avoid duplicates:
if not cur.execute("SELECT 1 FROM sense WHERE word_id=? AND text=? AND source_id=6",
                   (word_id, text)).fetchone():
    cur.execute("INSERT INTO sense(word_id,nikud,text,source_id) VALUES(?,?,?,6)",
                (word_id, nikud, text))
db.commit()
db.execute("PRAGMA wal_checkpoint(TRUNCATE)")   # important before committing to git
db.close()
```

---

## 5. Checklist before committing

- [ ] Applied to **both** DB copies (service + frontend).
- [ ] `PRAGMA integrity_check` returns `ok`.
- [ ] `PRAGMA wal_checkpoint(TRUNCATE)` run — otherwise new senses sit in a
      separate `-wal` file and the committed `.db` would be incomplete.
- [ ] Only the two `.db` files are staged (don't commit unrelated changes).
- [ ] The console is cp1252 here — Hebrew prints as `?`. Write query output to a
      UTF-8 file and open it to verify Hebrew, don't trust the terminal.

For a running dev service to pick up changes, rebuild + restart it (the service
reads its bin copy, which the `.csproj` refreshes from this file on build).
