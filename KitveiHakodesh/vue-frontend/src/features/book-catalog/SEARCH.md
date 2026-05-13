# Book Catalog Search Design

The search is built around an in-memory inverted index and a two-phase query strategy. The design is self-contained and can be copied to any feature that needs fast, Hebrew-aware search over a fixed in-memory dataset.

## Files and responsibilities

The search stack has four layers, each with a single responsibility:

`bookCatalogSearchNormalizer.ts` owns all text transformation. It expands abbreviations (שו"ע → שלחן ערוך), normalizes spelling variants (שולחן → שלחן), decomposes Hebrew words into a consonantal skeleton plus a vowel-letter set for חסר/מלא matching, and strips the definite article ה so הרמבן and רמבן resolve to the same token. Every rule here is applied symmetrically to both sides — indexed tokens at build time and query words at search time. Adding a new normalization rule means editing only this file.

`bookCatalogSearchMatcher.ts` owns the score constants and the per-token scoring function. Three tiers: EXACT (3) when a token equals the query word or is a חסר/מלא spelling variant, PREFIX (2) when a token starts with the query word, NONE (0) when there is no match. This file has no knowledge of the index or the catalog — it scores one word against one token list.

`bookCatalogSearch.ts` owns the inverted index and all query execution. It tokenizes books at index-build time (title + full category path + authors, all normalized), stores tokens in a sorted array with a parallel array of book-index sets for O(log n) binary-search lookup, and maintains a separate skeleton map for חסר/מלא variant matching. The public API is two functions: `buildSearchIndex` (call once after the catalog loads) and `filterBooksByWords` (call on every search).

`useBookCatalogSearch.ts` owns the two-phase search orchestration and Vue reactivity. It calls `filterBooksByWords` and decides when to fall back to the TOC heuristics pipeline.

## The inverted index

At catalog load time, every book is tokenized into a list of normalized strings (title words, category path words, author words). These tokens are stored in a single sorted array. A parallel array holds a `Uint16Array` of book indices for each token. Lookup is a binary search to the first token that starts with the query word, then a linear scan forward while the prefix holds — this gives all exact and prefix matches in one pass with no hash collisions and good cache locality.

A second structure, the skeleton map, maps each token's consonantal skeleton to the list of books that contain it. This handles חסר/מלא variants: נידה and נדה share skeleton נדה and are treated as exact matches. שבועות and שביעית share skeleton שבעת but have incompatible vowel sets, so they do not match.

## The catalog-best rule

For each query word, the scoring pass first finds the highest tier achieved by any book in the entire catalog. That tier becomes the required minimum for that word. If any book achieves EXACT for a word, then PREFIX-only books are dropped for that word. This keeps results tight: typing רמבם returns only books that exactly match רמבם, not every book whose title merely starts with ר.

## The mid-typing fallback

When the normal search (catalog-best on all words) returns nothing, `filterBooksByWords` previously retried with the last word's required tier capped at PREFIX. This fallback has been removed — every word is now treated uniformly. The catalog-best rule applies to all words equally.

## Result ranking

When multiple books pass the catalog-best filter, they are sorted by four tiers in order:

1. Total score — sum of per-word match tiers across all query words. Higher is better. A book that scores EXACT on every word ranks above one that scores PREFIX on any word.

2. Title match count — how many query words appear in the book's title tokens (exact or prefix). More title hits means the query is describing the book itself rather than something in its category path. This puts "תלמוד ירושלמי פסחים" above commentaries on it whose titles are "שיירי קרבן על תלמוד ירושלמי פסחים" — the query words appear in the base book's title directly, not just in the path.

3. Title token count — within the same title-match tier, fewer title tokens means the query fills a larger fraction of the title. "תלמוד ירושלמי פסחים" (3 title tokens) ranks above "תלמוד ירושלמי פסחים חלק א" (4 title tokens) when both have the same title match count.

4. Catalog tree order — final tiebreak. Whatever appears first in the catalog tree wins.

## The title-startsWith promotion

When no word in the query reached EXACT anywhere in the catalog, results are re-sorted so that books whose title starts with the full raw query string appear first. This handles single-word prefix searches where the user has typed the beginning of a title: typing מסיל puts מסילת ישרים before other books that merely contain מסיל somewhere in their category path.

## The two-phase query strategy

Phase 1 runs on every keystroke, synchronously. It calls `filterBooksByWords` against the in-memory catalog and shows results immediately with no loading state. If Phase 1 finds anything, Phase 2 never runs.

Phase 2 runs only when Phase 1 finds nothing, debounced at 300ms. It interprets the query as a combination of book words and TOC words — for example, בראשית פרק ד splits into book=בראשית and toc=פרק ד. It finds the longest prefix of the query that matches at least one book, fetches the TOC entries for those books from the database, and scores the remaining words against the TOC text. Results are capped at 50 candidate books to prevent runaway DB fetches on broad prefixes. A generation counter ensures that if the user types again while Phase 2 is in flight, the stale results are discarded.

## TOC search — Talmud page references

The TOC tokenizer in `segmentSearchTree.ts` keeps `.` and `:` attached to a preceding letter or digit so Talmud page references survive as single tokens: `דף י.` tokenizes to `["דף", "י."]` and `דף י:` to `["דף", "י:"]`. Without special handling, the exact-last-word strategy (which prevents `פרק ל` from surfacing `פרק לא`) would suppress `דף ד.` and `דף ד:` when the user types `דף ד`, because neither `ד.` nor `ד:` equals `ד` exactly.

The fix: in `_score`, a token is treated as an exact match for a query word when the token equals the word with a single `.` or `:` appended. So `ד.` and `ד:` are exact matches for `ד`. Both forms appear in results when the exact-last-word attempt succeeds, and the `פרק ל` / `פרק לא` disambiguation is fully preserved because `לא` is not `ל` plus a single suffix character.

## Adapting this design to another feature

Replace `bookCatalogSearchNormalizer.ts` with a normalizer specific to the new domain. Keep the same symmetric rule: whatever transformation you apply to indexed tokens, apply identically to query words.

Replace `bookCatalogSearch.ts` with a new index file. The tokenization function (`_tokenizeBook`) is the only domain-specific part — it decides what text from each item goes into the index. The index structure, binary search, skeleton map, catalog-best rule, and mid-typing fallback are all generic and can be copied verbatim.

Keep `bookCatalogSearchMatcher.ts` as-is or copy the score constants. The tier values (3/2/0) and the catalog-best rule are not specific to books.

If the feature has a secondary data source (like TOC entries here), add a heuristics file following the same four-stage pipeline pattern. Keep each stage as a named exported function so the pipeline is readable and independently testable.

Wire everything together in a composable that mirrors `useBookCatalogSearch.ts`: normalize the query, call `filterBooksByWords`, fall back to the secondary source when the primary returns nothing, use a generation counter for cancellation, and debounce only the async phase.
