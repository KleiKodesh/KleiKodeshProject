# Hebrew Search Text Normalization

How book-view and txt-view search normalize Hebrew text before matching, and the
character-position invariant that the highlight walkers depend on.

Everything here lives in `vue-frontend/src/utils/hebrewTextProcessing.ts`.

## The maqaf is a separator, not a diacritic

U+05BE MAQAF is the Hebrew hyphen. It joins two words into one accented unit and
sits *inside* the U+0591–U+05C7 Hebrew mark block, so a naive "strip the whole
block" pass deletes it — fusing the words it joins into a single token.

That was a real bug: a two-word query typed with a space could not match a
maqaf-joined phrase in a pointed text, because the text had been collapsed to one
unbroken word while the query still had its space.

Search therefore treats the maqaf as **a space**, on both sides of the match:

- `removeDiacriticsForSearch(text)` — strips the block *minus* the maqaf, then
  replaces the maqaf with a space. Applied to the QUERY.
- `stripHtmlForSearch(html)` — same rule while walking HTML. Applied to the TEXT.

Because both sides normalize identically, a phrase matches whether the reader
types a space or a maqaf.

## The position-parity invariant

`stripHtmlForSearch` output is not just for `indexOf` — its character positions
are mapped back into the original HTML by the mark-injection walkers, which
re-walk the raw HTML and count the characters they did NOT skip. **A character
must occupy the same number of positions in both.**

The maqaf becomes one space (one position), so the walkers must count it as one
character rather than skipping it. That is why there are two predicates:

| predicate                | skips the maqaf? | used by                                     |
| ------------------------ | ---------------- | ------------------------------------------- |
| `isSearchIgnoredMark`    | no (counts it)   | search walkers — positions from `stripHtmlForSearch` |
| `isDiacriticChar`        | yes (skips it)   | annotation walkers — highlights and notes   |

They are NOT interchangeable. Getting this wrong shifts marks by one character
per maqaf in the line, which looks like an off-by-one highlight bug far from its
cause.

### Why annotations keep the old reading

User highlight and note offsets were recorded against DOM text stripped with the
full `[\u0591-\u05C7]` range — maqaf included. Those offsets are **persisted in
`user_settings.db`**, so their walkers must keep skipping exactly what that strip
skipped. Changing them would silently move every existing annotation in a
maqaf-bearing line. Search positions are recomputed every scan and carry no such
history, which is why only search moved.

Walkers using `isSearchIgnoredMark` (search):
- `highlightMatches` and the snippet-term highlighter in `lines/useBookViewLineRenderer.ts`
- the search walker in `commentary/useCommentaryRender.ts`

Walkers using `isDiacriticChar` (annotations):
- `applyUserHighlights` and `applyUserNoteMarkers` in `lines/useBookViewLineRenderer.ts`

## Unaffected on purpose

- `wordLinkAnchors.ts` counts diacritics as visible characters to match upstream's
  `countVisibleChars`. It uses neither predicate and must not be aligned to them.
- `useBookViewAnnotations.ts` / `useCommentaryCopy.ts` strip the full range against
  live DOM text and compare against quotes stripped the same way, so they stay
  self-consistent.
- The hebrew-calendar helpers already excluded the maqaf (`[\u0591-\u05BD\u05BF-\u05C7]`),
  which is the same reading search now uses.
