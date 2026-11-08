# Commits

Every commit message body ends with one line per change the user will notice:

`User-facing: new|improved|fixed - <Area>: <one plain sentence>`
`User-facing-he: <the same sentence in Hebrew>`

- Area = the name in `.github/scripts/release-glossary.txt`.
- The sentence is what a non-programmer user notices. No jargon, file names or code terms.
- Gemini puts this text into the release notes as written, so write it like a release note.
- The Hebrew line is the author's own wording for the Hebrew release notes; Gemini only
  fixes grammar and glossary terms in it. Write it like a note a yeshiva student reads.
  Commits up to 2026-11-02 carried only the English line; from now on write both.
- Internal-only work (refactors, cleanup, CI, build, tests, docs) gets `User-facing: none`.

Example:

```
Book view: bookmarks anchored to the exact text position

User-facing: new - Book view: save bookmarks at the exact place in the text (Ctrl+Shift+D)
User-facing-he: <Hebrew: save a bookmark at the exact place in the text (Ctrl+Shift+D)>
```

# Dictionary.db and expansion-routed.db

- Committed here; each is the ONE working copy (edit in place, commit here).
- Reproduced by their own repos, cloned as gitignored sibling folders when needed:
  KleiKodesh/KitveiHakodeshDictionary (public) and KleiKodesh/SearchExpansion (private).
  Their builds write straight over the copies here. See the README.md next to each DB.

# Files live beside the app - never anywhere else (VITAL)

There is a portable app, so the app never writes a file outside its own folder: every index,
cache, download, snapshot, log and temp file goes in a subfolder beside the app.

- Never use %LocalAppData%, %AppData%, %TEMP% (`Path.GetTempPath`), Documents, `SpecialFolder`
  or any fixed path for app files. Resolve every location through
  `KitveiHakodesh.Core.Common.AppFileLocator`.
- Before adding any new storage, grep for those calls and check where the file will land in the
  installed app, the portable app and dev.
- Registry settings and file associations are not files and are outside this rule; ask before
  changing them.
