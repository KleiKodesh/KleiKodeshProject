# Agent Behavior Guidelines

## Content filter safety (network proxy scans EVERY payload)

**Rule:** Nothing that the network content filter might reject may enter the
conversation — in either direction. The filter scans the **entire API payload**:
user text, assistant replies, tool inputs, tool results (script stdout, file
reads), and compaction summaries. A blocked request kills the session.

**Proven failure (2026-08-03, session f54b8339):** an evaluation script printed
raw Hebrew corpus words (query probes + their neighbor lists) to stdout; that
tool result entered the next request and the proxy 418-blocked it. The corpus
itself contains vocabulary on the blocklist. The earlier guidance "illustrate
with corpus terms" was therefore WRONG and is revoked.

**Hard rules when working with corpus/lexicon text (Hebrew or Aramaic):**
1. **Raw corpus tokens never enter the conversation.** Not in replies, not in
   script output, not in file reads. Assume any surface form may be blocked.
2. **All corpus-touching scripts print masked output**: replace every
   Hebrew-script run with a stable placeholder `[H:xxxx]` (first 4 hex of md5).
   Full raw results go to files on disk; only masked text and ASCII metrics
   (counts, scores, timings) go to stdout. A shared helper for this lives at
   `KitveiHakodesh/research/word-association/tools/masked.py` — import it, do
   not re-roll it. It appends `hash → word` rows to a local decode map
   (`tools/hashmap.tsv`) so a human can decode placeholders on the machine.
3. **Never Read a file that contains Hebrew-range text.** Check first with a
   Grep for `[\x{0590}-\x{05FF}]` in `files_with_matches` mode (returns paths
   only). If it matches, produce a sanitized copy (same masking) and Read that.
4. **Delegating does not help** — subagents call the same API through the same
   proxy. The only safe channel for raw corpus text is local scripts + disk.
5. Data **on disk** stays verbatim — never alter corpus files, DBs, or code
   string literals to satisfy this rule. Masking applies to what is
   *transmitted*, never to what is *stored*.

**English wording in replies:** avoid animal names, body parts, personal names,
and informal or off-topic English examples — the filter reacts to vocabulary,
not intent. Illustrate co-occurrence/similarity/ranking with neutral
placeholders (`term A`, `word₁`) or masked hashes. Keep replies terse.

**Recovery when a session is blocked anyway:** the transcript survives at
`~/.claude/projects/<encoded-project>/<sessionId>.jsonl`. From a fresh session,
extract a **sanitized digest** (mask Hebrew runs → `[H:xxxx]`, strip non-ASCII,
truncate tool blobs) and read that — never the raw jsonl. A working extractor
pattern is kept at
`KitveiHakodesh/research/word-association/tools/extract_transcript.py`.

## Markdown File Creation

**Rule:** Do NOT create markdown (.md) files unless explicitly requested by the user.

**Rationale:**
- Markdown files should be created only when the user specifically asks for documentation
- Default behavior is to implement features and make code changes without generating documentation files
- This keeps the repository focused on code rather than auto-generated docs

**When to Create .md Files:**
- User explicitly says "create a document", "write a guide", "document this", etc.
- User asks for a README or specific documentation file
- User requests a specification or design document

**When NOT to Create .md Files:**
- Implementing features
- Fixing bugs
- Refactoring code
- Analyzing code
- Making code changes

**Exception:** README files that are part of project structure (e.g., updating existing README.md) may be modified if necessary for the implementation.
