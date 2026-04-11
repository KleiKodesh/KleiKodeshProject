# TOC Search Heuristics

## Core Data Structure — `SearchableTree`

Each node is indexed as an array of segments (one per ancestor + self), where each segment is an array of lowercase tokens. `בראשית / פרק א / פסוק ד` → `[["בראשית"], ["פרק", "א"], ["פסוק", "ד"]]`. Segment boundaries are preserved, never flattened into a single string.

## Pass 1 — Scoring

Query words are matched as an ordered subsequence across segments. Score = sum of costs between consecutive matched word pairs:

- Same segment: cost = token distance between the two matches (tight = cheap)
- Different segments: cost = number of segment boundaries crossed × 10

The heavy crossing penalty ensures `פרק ד` in one segment (score 1) beats `פרק ד` spread across two segments (score 10+), so the best result is always the tightest structural match.

## Pass 2 — Bond Detection

From the top-scoring result, any consecutive query word pair that landed in the same segment is marked "bonded". All results where a bonded pair crosses a segment boundary are filtered out. One occurrence is enough to establish a bond — no consistency threshold needed. This prevents `פרק ד` from matching `פרק א / פסוק ד` once a tighter match like `פרק ד` exists.

## Pass 3 — Ancestry Deduplication

If a node matched, all its descendants are suppressed. If `פרק ד` matched, `פרק ד / פסוק א`, `פרק ד / פסוק ב` etc. are dropped. Idea: the user asked for a chapter, not every verse in it.

## Book-FS Book Matching

All words except the last must match a path token exactly. The last word uses contains matching (loose, for mid-word typing feel). This prevents `טור` from matching `טורי זהב` while still letting `טו` match `טור` as the user types.
