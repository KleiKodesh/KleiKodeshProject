# ספר השרשים לרד"ק — Extraction Plan

## Sample Entry

The entry for שרש **אדם** (book id 6105, line index 63) is used as the reference throughout this document.

Raw content (abbreviated for readability):

```
<h3>אדם</h3>

<b>אָדְמוּ</b> עֶצֶם מִפְּנִינִים <small>(איכה ד, ז)</small>
<b> והפעול מן הדגוש</b> [פֻּעַל] מָגֵן גִּבֹּרֵיהוּ <b>מְאָדָּם</b> <small>(נחום ב, ד)</small>,
וְעֹרֹת אֵילִם <b>מְאָדָּמִים</b> <small>(שמות כה, ה)</small>.
<b> ופועל כבד אחר</b> [הִפְעִיל] אִם <b>יַאְדִּימוּ</b> כַתּוֹלָע <small>(ישעיה א, יח)</small>.
<b> וההתפעל</b> אַל תֵּרֶא יַיִן כִּי <b>יִתְאַדָּם</b> <small>(משלי כג, לא)</small>.
<b>והתאר</b> דּוֹדִי צַח <b>וְאָדוֹם</b> <small>(שיר השירים ה, י)</small>,
וְיִקְחוּ אֵלֶיךָ פָרָה <b>אֲדֻמָּה</b> <small>(במדבר יט, ב)</small>,
<b>אֲדֻמִּים</b> כַּדָּם <small>(מלכים ב' ג, כב)</small>,
<b>והתאר עוד</b> <b>אַדְמוֹנִי</b> עִם יְפֵה עֵינַיִם <small>(שמואל א' טז, יב)</small>.
ובהכפל ה<b>עי"ן</b> וה<b>למ"ד</b> ר"ל בו רוב האדמימות,
אוֹ <b>אֲדַמְדָּם</b> <small>(ויקרא יג, מט)</small>
וכן אמרו רבותינו ז"ל <small>(ספרא פרשת מצורע פרק יד, ב)</small> ירקרק ירוק שבירוקים, אדמדם.... אדום שבאדומים.
נֶגַע לָבָן <b>אֲדַמְדָּם</b> <small>(ויקרא יג, מב)</small>,
לְבָנָה <b>אֲדַמְדֶּמֶת</b> <small>(ויקרא יג, כד)</small>, פתוך ומעורב בשני מראים לובן ואודם.
<b>אֹדֶם</b> פִּטְדָה <small>(שמות כח, יז)</small>, נקראת כן האבן לפי שהיא אדומה וענינם ידוע.
וענין אדם ואדמה גם כן ידוע, ואדם נקרא על שם האדמה אשר לוקח משם...
ואדם יקרא על הפרט, <b>אָדָם</b> כִּי יַקְרִיב מִכֶּם קָרְבָּן <small>(ויקרא א, ב)</small>,
ועל הכלל, <b>מֵאָדָם</b> עַד בְּהֵמָה <small>(בראשית ו, ז)</small>...
גַּם בְּנֵי <b>אָדָם</b> גַּם בְּנֵי אִישׁ <small>(תהלים מט, ג)</small>
<b>בְּנֵי אָדָם</b> הם המון העם, <b>בְּנֵי אִישׁ</b> הם הגדולים...
הַרְחֵק מְאֹד <b>בֵאָדָם [מֵאָדָם]</b> הָעִיר <small>(יהושע ג, טז)</small>, שם עיר.
```

---

## What the Entry Contains

Every entry in ספר השרשים has this anatomy:

1. **Shoresh heading** — the `<h3>` tag. This is the root, unvocalized (e.g. `אדם`).
2. **Word forms** — text inside `<b>...</b>` that is NOT a grammatical label. These are actual Hebrew word forms, usually vocalized with nikud.
3. **Grammatical labels** — also in `<b>...</b>` but are prose labels that introduce a new binyan section. See full mapping below.
4. **Binyan tags** — appear in square brackets `[פֻּעַל]`, `[הִפְעִיל]`, `[נִפְעַל]`, `[הִתְפַּעֵל]`, `[פִּעֵל]` etc. immediately after a grammatical label. These are the modern binyan names written out explicitly by the editor — they confirm the label mapping and are the authoritative source when present.
5. **Biblical citations** — `<small>(ספר פרק, פסוק)</small>` following a word form.
6. **Definition prose** — plain Hebrew text between citations explaining meaning.
7. **Multiple meanings** — signaled by phrases like `וענין שני`, `וענין אחר`, `ענין אחר קרוב לזה`.
8. **Cross-references** — phrases like `כבר כתבנו בשרש X`, `ועוד נכתבנו בשרש X`, `נכתבנו בשרש X`.
9. **Loanword notes** — `בערבי X`, `בלע"ז X`, `ביוני X` giving Arabic/Latin/Greek equivalents.
10. **Rabbinic citations** — `<small>(מסכת דף ע"א/ב)</small>` referencing Talmud/Midrash.

---

## Distinguishing Word Forms from Grammatical Labels

Both appear in `<b>` tags. The rule:

- A **grammatical label** matches a known set of Hebrew prose phrases:
  `והפעול מן הדגוש`, `ופועל כבד אחר`, `והתאר`, `והתאר עוד`, `והשם`, `והנפעל`,
  `והפעל הכבד`, `וההתפעל`, `והפועל הכבד`, `ושלא נזכר פעלו`, `וכבד אחר`,
  `ופועל כבד`, `והפועל`, `ובהכפל ה`, `ר"ל`, `ז"ל`, `בלשון`, `שם`, `פירוש`.
  These are labels, not word forms — skip them.
- A **word form** is anything else in `<b>` that contains Hebrew letters and is not a label.
  It may include nikud (combining diacritics, Unicode category Mn).
  It may include prefixes like `וְ`, `בְּ`, `לְ`, `כְּ`, `מֵ` etc.
  It may include brackets with a variant: `<b>בֵאָדָם [מֵאָדָם]</b>` — extract both forms.

---

## Extraction Plan per Entry

Given the `<h3>` shoresh and its one content line, produce the following rows:

### Row type 1 — Shoresh entry

One row per shoresh:

| field      | value |
|------------|-------|
| headword   | shoresh stripped of nikud (e.g. `אדם`) |
| nikud      | `null` — the shoresh heading is never vocalized |
| source     | `ספר השרשים לרד"ק` |
| definition | the full plain-text of the entry, HTML stripped, truncated to ~500 chars |

This gives the dictionary a "what is this root" entry that surfaces when a user searches for the bare root.

### Row type 2 — Word form entries

For every `<b>` word form extracted from the content line:

| field      | value |
|------------|-------|
| headword   | the word form stripped of nikud and prefixes (see prefix stripping below) |
| nikud      | the word form as it appears (with nikud), if it has nikud; else `null` |
| source     | `ספר השרשים לרד"ק` |
| definition | `שרש: אדם` — i.e. "this word belongs to root X" |

This lets a user who searches for `אדמה` or `אדום` find the Radak entry.

**Prefix stripping:** remove leading `וְ`, `בְּ`, `לְ`, `כְּ`, `מֵ`, `הָ`, `הַ`, `הֶ`, `שֶׁ`, `כַּ`, `לַ`, `בַּ`, `מִ` before storing the headword. Keep the full prefixed form as the nikud field so it is still searchable.

**Bracket variants:** `<b>בֵאָדָם [מֵאָדָם]</b>` — extract `בֵאָדָם` and `מֵאָדָם` as two separate word form rows, both pointing to the same shoresh.

**Deduplication:** if the same stripped headword already exists in the dictionary from another source, do not insert a duplicate — skip it. If it does not exist, insert.

---

## What to Skip

- Entries whose content line is just a cross-reference with no word forms:
  e.g. `נכתבנו בשרש הה.` — these have no extractable word forms; still insert the shoresh row.
- Entries that are section markers (`נשלמה אות הבי"ת`) — skip entirely.
- The introduction lines before the first `<h3>` (lines 0–25) — skip entirely.
- Grammatical label `<b>` tags — skip as described above.
- `<small>` citation tags — skip.

---

## Radak's Grammatical Terminology — Full Mapping

Radak writes in medieval Hebrew grammar terminology inherited from the Arabic-influenced tradition of Ibn Janah and Hayyuj. His core concept is that verbs are either **קל** (light — simple stem = modern **פָּעַל**) or **כבד** (heavy — intensified stem, because the middle root letter carries a dagesh forte that "weighs" it). Here is every label he uses and what it means in modern Hebrew grammar:

### Verb binyanim

| Radak's label | Modern binyan | Explanation |
|---|---|---|
| *(no label, opening of entry)* | **פָּעַל** (= קַל) | The default, unmarked stem. Radak calls it קל ("light"); modern Hebrew grammar calls the same binyan פָּעַל. Radak starts every entry with פָּעַל forms without announcing them. |
| `והנפעל` | **נִפְעַל** | "The nif'al." Passive and reflexive of קל. The ן prefix is the marker. |
| `והפעל הכבד` / `והפועל הכבד` | **פִּעֵל** | "The heavy verb." Heavy because the middle root letter (ע"פ) is doubled with dagesh forte. Intensive/factitive. |
| `והפעול מן הדגוש` | **פֻּעַל** | "The passive of the dagesh'd [verb]." The passive of פִּעֵל. Radak calls פִּעֵל "the dagesh'd verb" because of its characteristic dagesh, so its passive is "the passive of the dagesh'd." |
| `ופועל כבד אחר` / `וכבד אחר` | **הִפְעִיל** | "Another heavy verb." After already introducing פִּעֵל or פֻּעַל, Radak signals the *other* heavy binyan — הִפְעִיל (causative). Both are "heavy" in his system; this phrase distinguishes the second one. |
| `ושלא נזכר פעלו מבנין הפעיל` | **הֻפְעַל** | "The [passive] whose active form from הִפְעִיל is not mentioned." The passive of הִפְעִיל. Radak notes it this way when the root has a הֻפְעַל form in Tanakh but no attested הִפְעִיל active for that root. |
| `וההתפעל` | **הִתְפַּעֵל** | "The hitpa'el." Reflexive/reciprocal of פִּעֵל. |

### Non-verb categories

| Radak's label | Modern category | Explanation |
|---|---|---|
| `והתאר` / `והתאר עוד` | **תואר השם** (adjective) | "The descriptor." Words that describe a noun — color terms, quality terms. `עוד` just means "another adjective form." |
| `והשם` | **שם עצם** (noun) | "The noun." A nominal form derived from the root — not a verb conjugation. |
| `ובהכפל העי"ן והלמ"ד` | **שם תואר מוכפל** (reduplicated adjective) | "With the doubling of the ayin and lamed [of the root]." Reduplicated forms like `אֲדַמְדָּם` from `אדם` — a biblical pattern for expressing intensity of a quality (very red, very green). |

### How the system works in practice

Radak's entry structure is a cascade: he works through the binyanim in a roughly fixed order — פָּעַל (קל) → נפעל → פִּעֵל → פֻּעַל → הִפְעִיל → הֻפְעַל → הִתְפַּעֵל — then moves to non-verb categories (תואר, שם). Not every root has forms in every binyan; he only mentions the ones attested in Tanakh.

The bracket tags `[פֻּעַל]`, `[הִפְעִיל]` etc. were added by the modern editor of this digital edition to make the binyan explicit. When a bracket tag is present it is authoritative. When it is absent, the label alone determines the binyan using the table above.

---

## Binyan Tagging (optional, phase 2)

The entry groups word forms by binyan. The pattern is:

```
<b>grammatical label</b> [בִּנְיָן] word1 <small>citation</small>, word2 <small>citation</small>...
```

The binyan in `[...]` applies to all word forms that follow it until the next grammatical label. When no bracket tag is present, derive the binyan from the label using the mapping table above.

For the אדם entry specifically:
- Opening forms with no label → **פָּעַל** (קל)
- `והפעול מן הדגוש` + `[פֻּעַל]` → **פֻּעַל**
- `ופועל כבד אחר` + `[הִפְעִיל]` → **הִפְעִיל**
- `וההתפעל` → **הִתְפַּעֵל**
- `והתאר` → **תואר השם** (adjective, not a verb binyan)

This binyan can be stored in the `sense` table if we want to enrich the dictionary later. For the initial extraction, it is recorded but not required.

---

## Output Summary for אדם

From the אדם entry, extraction produces approximately:

- 1 shoresh row: headword=`אדם`, definition=full prose text
- ~15 word form rows, including:
  `אדמו`, `מאדם`, `מאדמים`, `יאדימו`, `יתאדם`, `אדום`, `אדמה`, `אדמים`,
  `אדמוני`, `אדמדם`, `אדמדמת`, `אדם`, `אדמה`, `אדמת`, `אדם` (person sense), `אדם` (city)

Total across the full book (~2101 entries): estimated 15,000–25,000 word form rows.

---

## Schema Fit

The current `entry` table schema:

```
entry(id, headword TEXT, nikud TEXT, source_id INTEGER, definition TEXT)
```

This fits perfectly:
- Shoresh rows: `headword=אדם`, `nikud=null`, `definition=<prose>`
- Word form rows: `headword=אדום`, `nikud=וְאָדוֹם`, `definition=שרש: אדם`

No schema changes needed for phase 1.

The `sense` table (if binyan tagging is added in phase 2) would hold:
`headword`, `nikud`, `pos` (verb/noun/adj), `shoresh`, `binyan`.
