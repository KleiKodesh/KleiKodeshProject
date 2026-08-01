# Scripts

## UpdateHebrewBooksDb.py

Scrapes Hebrew books directly from beta.hebrewbooks.org and updates the SQLite database.

**Run:** `python UpdateHebrewBooksDb.py`

---

## AddLaazRashiToDictionary.py

Reads all לעזי רש"י entries from the seforim database (book "אוצר לעזי רש"י", id 5747)
and imports them into `KitveiHakodesh_dictionary.db` as a new source kind.

Each entry's headword is the לעז word (a Hebrew-script word with gershayim before
the last letter, e.g. פריי"ר, גלצ"א). The sense text is the bolded Hebrew translation
that follows the Latin transliteration in the entry.

The script handles both layout variants found in the source book:
- Standard: LAAZ / latin-transliteration / **Hebrew-translation**
- Inverted: LAAZ Hebrew-description / **latin-transliteration**

The import is idempotent — re-running it removes previously imported entries before
inserting fresh ones, so running it twice produces the same result as running it once.

**Modes:**

```
python AddLaazRashiToDictionary.py            # parse + import
python AddLaazRashiToDictionary.py --dry-run  # parse + show counts, no write
python AddLaazRashiToDictionary.py --test     # parse only, no dictionary access
```

**Source DB:** `C:\ProgramData\otzaria\books\seforim.db`  
**Target DB:** `vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db`
---

## AddOtzariaAcronymsToDictionary.py

Reads the Otzaria app's ראשי תיבות asset (`Acronyms.json`, ~13,100 abbreviations /
~29,000 expansions) and imports it into `KitveiHakodesh_dictionary.db` as the
source kind `אוצריא - ראשי תיבות`.

Keys carrying nikud (e.g. הַגְּרָ"א) are stripped and merged with their plain form.
Because the dictionary already holds three ראשי תיבות sources that overlap with the
Otzaria list, expansions whose normalized text already exists as a sense of the same
headword (from any source) are skipped — only genuinely new expansions are inserted.
Text comparison is spelling-tolerant (nikud stripped, י/ו ignored), so מלא/חסר
variants of the same expansion count as duplicates.

**Prefix-redundancy filter:** the book-view abbreviation tooltip strips attached
prefix letters (ו, ה, ב, כ, ל, מ, ש, ד) before lookup, so expansions that are just a
prefix letter glued onto an expansion of the stripped base entry are NOT imported
(בתוה"ק → "בתורתינו הקדושה" is dropped; selecting בתוה"ק resolves via תוה"ק).
Entries whose expansion carries content the base entry lacks are kept even when the
headword looks prefixed (בא"ש → "באר שבע", באה"ל → "ביאור הלכה").

The import is idempotent — re-running it removes previously imported entries before
inserting fresh ones.

**Modes:**

```
python AddOtzariaAcronymsToDictionary.py            # analyze + import
python AddOtzariaAcronymsToDictionary.py --dry-run  # analyze + show counts, no write
python AddOtzariaAcronymsToDictionary.py --test     # analyze only (includes overlap stats)
```

**Source:** `C:\Program Files\אוצריא\data\flutter_assets\assets\Acronyms.json`  
**Target DB:** `vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db`
