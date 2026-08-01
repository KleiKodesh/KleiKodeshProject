# Aramaic/Hebrew spelling-redirect vetting spec

You are vetting candidate spelling-redirect groups for a **Talmudic-study Hebrew/Aramaic dictionary**.

## What a candidate is
Each input group is: `BASE (n): form1 form2 ...` — an existing dictionary headword (BASE) and inflected/variant surface forms that lexical.db rolled up to it. A "redirect" makes typing `form` jump to BASE's entry. So every kept form MUST genuinely be a spelling/inflection of the SAME WORD as BASE.

## KEEP a base (and its clean forms) when:
- BASE is a real Hebrew common noun / verb / adjective, and the forms are its inflections (prefixes ו/ה/ש/כ/ל/ב/מ + suffixes).
- BASE is a single Aramaic word (e.g. דוכתא=place, אבהתא=fathers, שמעתתא=teaching) — **KEEP Aramaic**, this is a Talmud app. Aramaic morphology differs: ד prefix ("of"), א prefix (aphel), suffixes ייהו/יה/ין/נן/הו. These are exactly the variant spellings we want.
- Same-word homographs (one word, multiple senses / cross-strata): KEEP. Examples: יובל jubilee/stream, ירא fear/God-fearing, רוח spirit/wind, כבש conquer, בגד garment.

## DROP a whole base when:
- It is a **proper name** (person/place) whose only forms are proclitic+name (ל/ו/מ/ב+name): drop. (Names add clutter, no real spelling variants.)
- It is a **pure grammatical particle / pronoun** with no meaningful dict entry: drop. E.g. Aramaic דידיה(his), דילמא(perhaps), האי(this), ההוא(that-one), הכא(here), הני(these), אין(no/there-is), Hebrew גם(also), אז(then).
- It is a **TRUE different-word homograph**: forms belong to 2+ genuinely different words that merely share the unpointed spelling. DROP the whole base. Examples proven so far: דין(law / Aramaic "this" / Midian), בר(son / grain / "create"), אל(God / "to" / "these"), שאול(Saul / Sheol / borrowed), שם(name / there), שכל(intellect שׂ / bereave שׁ), כבש(sheep כבשׂ / conquer כבשׁ), באר(well / explain), גמל(camel / repay-verb), גבה(collect-tax / be-tall).

## DROP individual forms (keep the rest of the base) when:
- The form is a merged/garbage token (letters mashed: בתוהנערה, אשראבא, וקרןישעי, doubled גגג).
- The form collides with a common OTHER verb: e.g. הרים(mountains vs "he raised" רום), האשים(fires vs "he accused" אשם), הבריח(bolts vs "he smuggled" ברח), התאים(chambers vs "he matched" תאם), הדגים(fish vs "he demonstrated" דגם). When a form is a real hiphil/piel of a DIFFERENT root, DROP it.
- An Aramaic "throw/cast" cluster mixed into a Hebrew noun (e.g. שדה field got Aramaic שדי forms) — drop the alien cluster, keep the noun forms.

## Output format (STRICT)
Return ONLY a JSON object with this shape (no prose outside it):
```
{
  "keep": [ {"base": "דוכתא", "variants": ["בדוכתא","מדוכתא", ...]}, ... ],
  "dropped_bases": [ {"base":"דין","reason":"homograph: law/this/Midian"}, ... ],
  "dropped_forms": [ {"base":"הר","form":"הרים","reason":"='he raised' רום"}, ... ]
}
```
- Include in `keep` every base you keep, with the exact surviving variant strings (copy them verbatim from input).
- Be decisive. When unsure whether a base is a true homograph, prefer KEEP if forms clearly share one meaning, DROP if they clearly split. Note borderline calls in dropped_bases reason.
- Do NOT invent variants. Only use forms present in the input.
- Do NOT touch any database, file, or git. Analysis only.
