# TTS (Text-to-Speech) Feature Plan

## Overview

Add a Hebrew text-to-speech button to the book view toolbar. Uses the Web Speech API via VueUse's `useSpeechSynthesis`. Reads from the selected line onward, highlights the current word as speech advances, scrolls to keep the active line in view, and guides the user through installing a Hebrew voice if none is found.

---

## Files to Create

### `src/components/book-view/useSpeech.ts`

Thin composable wrapping `useSpeechSynthesis` from `@vueuse/core` — do not hand-roll the Web Speech API.

- Accepts a reactive `text` ref (the current line's raw content)
- Picks the Hebrew voice first: uses `window.speechSynthesis.getVoices()`, waiting for `onvoiceschanged` if the list is empty (Chrome async quirk), then selects the first voice whose `lang` starts with `he`
- Passes the selected voice + `lang: 'he-IL'` into `useSpeechSynthesis(text, { voice, lang, onBoundary })`
- The `onBoundary` option (supported by VueUse since the `onBoundary` callback was added) updates `currentWordRange` on each word event
- Exposes:
  - `speak()` — calls the `speak()` returned by `useSpeechSynthesis`
  - `stop()` — calls the `stop()` returned by `useSpeechSynthesis`
  - `isSpeaking` — mapped from VueUse's `isPlaying`
  - `hasHebrewVoice` — reactive boolean, true once a Hebrew voice is detected
  - `currentWordRange` — reactive `{ charIndex: number; charLength: number } | null`, updated via `onBoundary`, reset to null on end
  - `onEnd` — callback setter so `BookViewPage` can hook into utterance completion to advance to the next line

### `src/components/book-view/SpeechInstallDialog.vue`

Modal shown when the TTS button is clicked and no Hebrew voice is available.

- Overlay with a centered card, same pattern as `ConfirmDialog.vue`
- Hebrew content only:
  - Title: "אין קול עברי מותקן"
  - Body: step-by-step instructions — הגדרות Windows ← זמן ושפה ← דיבור ← הוסף קולות ← חפש "עברית"
  - Primary button: "פתח הגדרות דיבור" — triggers `<a href="ms-settings:speech">` navigation
  - Secondary button: "סגור"
- Emits `close`
- No external dependencies beyond existing app patterns

---

## Files to Modify

### `src/components/book-view/BookViewLinesContent.vue`

Two changes:

1. **Allow line selection when commentary is collapsed** — `onLineClick` currently only emits `line-selected` when `bottomVisible` is true. Remove that guard so clicking always emits `line-selected`. The visual `selected` highlight class can remain gated on `bottomVisible` (cosmetic only).

2. **Word highlight for TTS** — add a new prop `ttsWordRange: { charIndex: number; charLength: number } | null` (default null). In the `lineContent()` render function, when `ttsWordRange` is non-null and the line being rendered is the active TTS line (new prop `ttsLineId: number | null`), inject a `<mark class="tts-word">` span around the character range — same HTML-aware walk already used by `highlightMatches`. Add CSS: `.tts-word { background: var(--accent-color); opacity: 0.35; border-radius: 2px; }`. The render cache key must include `ttsLineId` and `ttsWordRange` so it invalidates correctly.

### `src/components/book-view/BookViewToolbar.vue`

- Add props: `isSpeaking: boolean`, `hasHebrewVoice: boolean`, `selectedLineAvailable: boolean`
- Add emit: `toggleSpeech`
- Add TTS button before the first separator:
  - Always visible
  - Icon: `IconSpeaker2_20Filled` when `isSpeaking`, `IconSpeaker2_20Regular` otherwise
  - `@click="$emit('toggleSpeech')"`
  - Title: "קרא בקול" when idle, "עצור קריאה" when speaking
  - No disabled state — clicking when no voice is available triggers the install dialog (handled in page)

### `src/components/book-view/BookViewPage.vue`

This is where all TTS orchestration lives.

**State additions:**

- `ttsActive` ref — true while TTS is running
- `ttsLineIndex` ref — the `lineIndex` of the line currently being spoken (not the ID, since we need to advance sequentially)
- `showSpeechInstallDialog` ref — controls `SpeechInstallDialog` visibility

**`useSpeech` wiring:**

- `speechText` computed — raw content of the line at `ttsLineIndex` (strip HTML tags for clean speech input); falls back to `lines.value[0]?.content` if `ttsLineIndex` is null
- Pass `speechText` into `useSpeech`

**Default line selection:**

- When `selectedLineId` is null after IDB restore (i.e. no persisted selection), default to the first loaded line: watch `lines` until the first real line (id > 0) arrives, then set `selectedLineId` to it without opening the commentary panel

**TTS button click handler (`onToggleSpeech`):**

- If `isSpeaking` → call `stop()`, set `ttsActive = false`
- Else if `!hasHebrewVoice` → set `showSpeechInstallDialog = true`
- Else → set `ttsLineIndex` to the `lineIndex` of the current `selectedLineId` (or 0 if none), call `speak()`

**Line advance logic:**

- Watch `useSpeech`'s internal utterance `end` event (exposed as an `onEnd` callback from `useSpeech`) — when a line finishes speaking, increment `ttsLineIndex` by 1, update `selectedLineId` to the new line's id, call `speak()` with the next line's text
- Stop automatically when `ttsLineIndex` exceeds `lines.value.length - 1`
- On each line advance, call `linesContentRef.value?.scrollToLineId(newLineId)` so the active line stays visible

**Word highlight wiring:**

- Pass `ttsWordRange` from `useSpeech` and `ttsLineId` (the id of the line at `ttsLineIndex`) down to `BookViewLinesContent` as props

**Toolbar props:**

- Pass `isSpeaking`, `hasHebrewVoice`, `selectedLineAvailable` (true when `selectedLineId != null`) to all four `BookViewToolbar` instances
- Add `@toggle-speech="onToggleSpeech"` to all four

**Template additions:**

- Add `<SpeechInstallDialog v-if="showSpeechInstallDialog" @close="showSpeechInstallDialog = false" />` inside `.book-view`

---

## Data Flow Summary

```
BookViewPage
  ├── useSpeech(speechText)
  │     ├── currentWordRange  ──→ BookViewLinesContent (ttsWordRange prop)
  │     ├── isSpeaking        ──→ BookViewToolbar (isSpeaking prop)
  │     ├── hasHebrewVoice    ──→ BookViewToolbar + onToggleSpeech guard
  │     └── onEnd callback    ──→ advance ttsLineIndex, call speak() again
  │
  ├── ttsLineIndex            ──→ derive ttsLineId from lines[]
  │     └── ttsLineId         ──→ BookViewLinesContent (ttsLineId prop)
  │                                 scrollToLineId() on advance
  │
  └── showSpeechInstallDialog ──→ SpeechInstallDialog (v-if)
```

---

## Constraints & Notes

- Raw line content (before HTML processing) is used as speech input — strip any residual HTML tags with a simple regex before passing to `useSpeechSynthesis`
- Divine name censoring (`censorDivineNames`) should NOT be applied to TTS text — speak the actual text
- The `onBoundary` event is not fired by all voices/browsers; `currentWordRange` may stay null — the feature degrades gracefully (line-level highlight still works, word highlight just doesn't appear)
- WebView2 limitation: natural/neural voices may not appear in `getVoices()` even if installed; standard SAPI voices will. The install dialog guides users to add the standard Hebrew voice.
- `ttsWordRange` changes on every word boundary — the render cache key must include it to avoid stale highlights. Since this fires frequently, keep the cache invalidation cheap (the key is a string comparison).
- When TTS is stopped mid-line, reset `ttsWordRange` to null and clear the highlight immediately.
- Do not persist TTS state to IDB — it is session-only.
- All user-facing strings in Hebrew only.
- `SpeechInstallDialog` must be extracted as its own component — do not inline the markup in `BookViewPage`.
- After implementing, update `src/components/book-view/README.md` to document the new files and the TTS data flow.
