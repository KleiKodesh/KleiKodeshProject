# Chronological ordering

Gives every book a year so search results can be sorted oldest-first. The seforim DB has no
composition-date column — its only year, `pub_date`, is the print-edition year — so the date
is looked up from the tables here.

## Files

| file | keyed by | entries |
|---|---|---|
| `authorYears.ts` | normalized author name | 589 |
| `canonicalWorkYears.ts` | book title | 373 |
| `workStemYears.ts` | category-path stem | 8 dated + 2 deliberately undated |
| `index.ts` | — | the lookup itself (`chronologicalKey`) |

Three tables rather than one because they match on different things, and because the order
they are consulted in matters (see below).

## How a book gets its year

`chronologicalKey()` tries five rungs, most specific first, and the first hit wins:

1. **Author death year** (~73% of the corpus). A death year, not a birth year: we are
   ordering *works*, and an author's output clusters near the end of his life.
2. **Work stem** (~2%). Multi-volume works split one volume per tractate — each volume has a
   different title, so only the category path identifies the work.
3. **Canonical work** (~5%). The anonymous classics, dated per work by **traditional**
   attribution (Torah at Sinai, Mishnah to Rebbi, Bavli to Rav Ashi and Ravina, Targum
   Yonatan to Yonatan ben Uziel per Megillah 3a — not the later dates of critical
   scholarship).
4. **Era category** (~11%). Only `ראשונים` / `אחרונים` / `מחברי זמננו`, the three tree
   categories that genuinely encode an era.
5. **Unknown** (~9%). Sorts last.

Roughly 91% of books get a real date; the range runs from -1312 to 2021.

## Two things that will bite you

**Rung 2 must run before rung 3.** The per-tractate commentary volumes are titled with bare
tractate names, so a volume titled `ברכות` collides with the Bavli tractate `ברכות` in
`canonicalWorkYears.ts`. Checked in the wrong order, a 19th-century commentary gets stamped
500 CE. `WORK_STEM_UNDATED` exists for the same reason: works we deliberately refuse to date
must stop at rung 2 rather than fall through into that collision.

**Keys in `authorYears.ts` must already be normalized.** The lookup normalizes the DB's
author string (strips nikud/te'amim, quote glyphs, directional marks) and matches directly,
so a key left in raw form is silently unreachable — it will not error, the author will just
never resolve and the book will sort last.

## Adding a date

Prefer an author entry: one line dates every book that person wrote. Only use
`canonicalWorkYears.ts` when a work genuinely has no single author, and `workStemYears.ts`
when the volumes have differing titles and no author recorded.

Never infer a year. A century is not a year, a print date is not a composition date, and a
modern editor's or compiler's year is not the work's date — that last one is the specific
error this design exists to remove. If a name or title cannot be resolved to one
identifiable person with a documented year, leave it out: sorting last is the honest answer
and is what the design expects. `authorYears.ts` marks entries `medium` where identification
required judgement or sources disagreed; those are the first place to look if a book turns
up somewhere odd.

`workStemYears.ts` carries one deliberate exception, documented inline: Kikar LaAden is
dated by its publisher (the Chida, d. 1806) rather than its unnamed author, because that
still places ~40 volumes far closer than stranding them at the end.
