# Commits

Every commit message body ends with one line per change the user will notice:

`User-facing: new|improved|fixed - <Area>: <one plain sentence>`

- Area = the name in `.github/scripts/release-glossary.txt`.
- The sentence is what a non-programmer user notices. No jargon, file names or code terms.
- Gemini puts this text into the release notes as written, so write it like a release note.
- Internal-only work (refactors, cleanup, CI, build, tests, docs) gets `User-facing: none`.

Example:

```
Book view: bookmarks anchored to the exact text position

User-facing: new - Book view: save bookmarks at the exact place in the text (Ctrl+Shift+D)
```
