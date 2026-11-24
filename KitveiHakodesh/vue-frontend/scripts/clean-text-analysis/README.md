# Clean-text rules: how they were decided

Background for the book view's clean-text option (eraser button, Ctrl+D). Read this before
changing any rule in `src/utils/hebrewTextProcessing.ts`, `src/utils/hebrewTextCleaning.ts`
or `src/utils/punctuationModes.ts`, and rerun the scripts here to measure the change.
Analysis done 2026-11-11.

## What the option is for

There are two separate jobs:

1. **Reading preference.** Vowels, cantillation, maqaf to space. These are independent
   checkboxes.
2. **Modern to classic punctuation.** The reader turns modern punctuation into the
   classic rabbinic style (only periods; periods and commas; a colon closing a passage)
   at any level. A level picks every mark at once, and each mark can then be set on its
   own (keep / convert / remove). The user asked for **full control**, so never collapse
   this back into a few fixed options.

Rules that hold at every level:

- **Brackets are not punctuation.** `( )` and `[ ]` are never touched, and neither is
  anything inside them (they hold source citations).
- **Citation colons** (`chapter:verse`, also with geresh, also across tags like
  `<b>9</b>:<b>13</b>`) are never touched. A citation never spans a block tag.
- **The colon closing a passage** (at the end of a line) is its own setting
  (`MARK_PASSAGE_END_COLON`). It stays even at "Periods only", because it is the classic
  colon, not modern punctuation.
- **The line-end period always stays.**

## Levels (punctuationModes.ts, PUNCTUATION_PRESETS)

| Level | ?! | ; | , | colon in line | passage-end colon | period in line | quotes | dash |
|---|---|---|---|---|---|---|---|---|
| Original | keep | keep | keep | keep | keep | keep | keep | keep |
| Classic | to `.` | to `,` | keep | keep | keep | keep | remove | remove |
| Periods and colons | to `.` | remove | remove | keep | keep | keep | remove | remove |
| Periods only | to `.` | remove | remove | to `.` | keep | keep | remove | remove |
| None | remove | remove | remove | remove | keep | remove | remove | remove |

- "Quotes: remove" keeps acronym gershayim, which sit between a word's last two letters.
- "Colon to `.`" turns a colon that ends a sentence into a period. A sentence colon has a
  word, bracket or footnote tag right before it and a space after. A loose artifact colon
  (`word : word`, `word :word`) is dropped.
- The default is vowels, cantillation, the dibbur hamatchil dash and the Classic level.
  The copy paths use the same default (`CLEAN_TEXT_LEGACY`).
- The flags are one number: toggles in bits 0-7, then two bits per mark from bit 8. The
  setting is stored as `text.cleanText.options.v2`; bump the key if the layout changes.

## Dibbur hamatchil dash (the most data-driven rule)

**Bug that started it.** Berakhot 13a in the vocalized Talmud reads
`... if he directed his heart – he fulfilled.` The old rule turned the first dash after
any text on a line into ". ", so a pause dash became a full stop.

**Data.** Otzaria's `seforim.db` (`%AppData%\otzaria\books\seforim.db`, read-only). Line
HTML is in `line_content.content`, zstd-compressed with the dictionary in `zstd_dict`;
Python 3.14's `compression.zstd` reads it.
- Ground truth for heading words: the `line_dh` table (bookId, lineIndex, dhDisplay),
  with 1.27M rows in 2741 books.
- `book.dependenceType = 'commentary'` marks the 4887 commentaries.

**Sample.** Up to 70 random books per top category, 250 random lines each. That gave 9471
lines with a spaced dash; 3139 of them have a known answer.

**Findings.**
- The dash character cannot tell a heading from a pause. Both use the hyphen, en dash and
  em dash.
- Real headings are bold in 97-99% of Bavli and Yerushalmi commentary lines. Vilna Rashi
  and Tosafot write `<b><b></b><i></i>words</b> - `, so empty tags must be ignored.
- Base texts rarely have a bold heading before the dash: Bavli 5%, Tanakh 17%.
- Headings are short: 1-6 words in 68% of cases, 10 or fewer in 90%. Non-headings run
  15+ words in 59% of cases.
- Commentaries with plain (not bold) headings are rare. Examples: Ibn Ezra on Zephaniah
  (Daat edition, book 3613) and book 5818.

**Scores on the labelled lines** (precision = share of full stops that are right; recall =
share of real headings caught):

| Rule | Precision | Recall |
|---|---|---|
| Old: any text before the dash | 0.57 | 1.00 |
| One bold span only | 0.93 | 0.86 |
| Bold span, or 6 or fewer plain words without inner punctuation in a commentary (chosen) | 0.93 | 0.98 |

- The implementation was checked by running the real TypeScript function on the same
  lines: precision 0.932, recall 0.981.
- In commentaries alone, precision is 0.994.
- Almost all remaining wrong full stops are in one Tanakh headword book (8174, written
  `<b>word</b> - explanation`), where a full stop is arguably right.

**Implemented rule** (`separateDibburHamatchil`):
- Only the first spaced dash of a line is judged. It becomes ". " when the text before it
  is one bold span. Numbering may come before the span, and its own `.` or `:` after it.
- In commentary only (`PLAIN_DIBBUR_HAMATCHIL`), a short plain heading also counts, and so
  does a bold span still open at the dash.
- "Commentary" means the commentary pane, or a book whose `dependenceType` is commentary.
  The latter comes from `BookInfo.isCommentary`, which `GetBookById` returns.
- A heading that already ends in `.` or `:` keeps its mark, and the dash is just removed.
- A dash with nothing before it (a list or table cell) stays, and the next dash is judged.
- Later dashes are punctuation and belong to `MARK_DASH`.

**Known misses** (about 25 lines in the sample): book 3613, whose headings contain a comma,
and book 3376, which puts the bold word and a `<br>` before the text.

## Audit of the other rules (about 21k lines, 9 categories)

Bugs found and fixed:
- **Sentence colons dropped.** A colon ending a sentence in mid-line was deleted: 27% of
  all colons, including 532 after a footnote `</sup>`.
- **Citation colons next to a geresh lost.** 277 cases.
- **Acronym quotes lost** when vowel points or a U+200E direction mark sat next to them.
  About 450 cases.
- **Typed maqaf.** An ASCII hyphen joining two Hebrew words (437 cases) now also becomes a
  space under "maqaf to space". Digit ranges like 615-567 stay.
- **Paseq.** Removing U+05C0 left a double space.
- **Square brackets** did not protect the marks inside them, as round ones did. 246 cases.
- **Glued long dash.** A long dash between two words with no spaces fused them when
  removed. It now leaves a space.
- **Period before an inline tag.** A period before `</b>` in mid-line counted as line end.
  Now only a block tag or the end of the string does.

Checked and correct:
- Vowel and cantillation removal, including meteg, holam haser, and upper and lower dots.
- U+05C3 sof pasuq is kept as punctuation.

Not changed (low priority):
- Single quotes (`'`, U+05F3) are never removed as quotation marks.
- `&nbsp;` is not merged with neighbouring spaces.

## Rerunning

The scripts are the ones used for the numbers above.
- They read the DB read-only, and each finishes in a few minutes.
- Python: `python -I sample_dash_lines.py [path/to/seforim.db]`. The default is Otzaria's
  `%AppData%/otzaria/books/seforim.db`. Data files go to `./data` (gitignored), or to
  `$CLEAN_TEXT_DATA`.
- The `.rig.ts` files are vitest files that run the real TypeScript functions. The
  normal test run (`src/**/*.test.ts`) skips them. To run one, copy it to `x.test.ts` here
  and run `npx vitest run --root scripts/clean-text-analysis x.test.ts` from
  `vue-frontend`, then delete the copy.
- sample_dash_lines.py writes rows.json; the labelled rows2.json the scorers read came
  from a second pass in the original session that is not kept. Check the field names
  before you rerun.

| Script | Does |
|---|---|
| sample_dash_lines.py | Samples lines with a spaced dash and labels them from `line_dh` and `dependenceType` (writes rows2.json) |
| score_dash_heuristics.py | Scores candidate heading rules on rows2.json |
| run_dash_implementation.rig.ts | Runs the real `stripNikkudFromHtml` on rows2.json (writes vout.json) |
| score_dash_implementation.py | Scores vout.json |
| sample_audit_lines.py | Samples lines across categories for the other rules (writes au.json) |
| run_audit.rig.ts | Runs the real `cleanHebrewText` per option (writes auout.json) |
| count_audit_changes.py | Counts which marks each option changed, and in what context |
