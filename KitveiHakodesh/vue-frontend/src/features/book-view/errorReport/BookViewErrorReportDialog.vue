<script setup lang="ts">
import { computed, ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useSettingsStore } from '@/stores/settingsStore'
import { showToast } from '@/composables/useToast'
import { buildErrorReportPayload, extractContextAround } from './errorReportPayload'
import { queueErrorReport, sendErrorReport } from './errorReportService'
import { hasErrorReportEndpoint } from './errorReportEndpoint'
import { UNKNOWN_LIBRARY_VERSION, type ErrorReportSource } from './errorReportSource'

/** What a report carries when the database could not be consulted at all. */
const UNRESOLVED_SOURCE: ErrorReportSource = {
  sourceFolder: '',
  libraryVersion: UNKNOWN_LIBRARY_VERSION,
  libraryBuildId: null,
  location: null,
}

/**
 * "דיווח על טעות בספר" — the user names a mistake in the text, and may edit the
 * pre-filled box to say how it should read instead. Describing the mistake is
 * required; proposing a replacement is not.
 *
 * Its own component rather than a ConfirmDialog with slots: it owns three fields, a
 * send that can be in flight, and a correction editor whose state decides what kind of
 * report goes out. The shell (backdrop, Escape, click-outside) deliberately mirrors
 * ConfirmDialog so the two read as the same app.
 */
const props = defineProps<{
  /** What the user selected, or the whole pressed line when nothing was selected. */
  selectedText: string
  /** The whole line the selection sits in, plain text — the correction's anchor. */
  originalLine: string
  /** Where selectedText starts inside originalLine; -1 when it could not be located. */
  selectionOffset: number
  bookTitle: string
  /** Book + TOC path, as the copy menu's מקור builds it. */
  currentRef: string
  /** 0-based index into the book's lines. */
  lineIndex: number
  bookId: number | null
  /**
   * Provenance + verified coordinates, resolved from the database as the dialog opens.
   * null while that read is still in flight.
   */
  source: ErrorReportSource | null
  /** Resolves once `source` has landed — awaited before sending. */
  awaitSource: () => Promise<unknown>
}>()

const emit = defineEmits<{ close: [] }>()

const settingsStore = useSettingsStore()

const errorDetails = ref('')
const senderEmail = ref(settingsStore.errorReportSenderEmail)
const isSending = ref(false)

// The correction editor is always open, seeded with the text as it reads now. What
// makes a proposal a proposal is therefore the user having CHANGED it: leaving the
// box untouched means "I am describing a mistake, not dictating the fix", and sends
// freeText. Emptying it deliberately is a different thing — that proposes a deletion.
// Captured once, and compared against from then on. Reading props.selectedText live
// here would make "has the user changed it?" depend on a prop staying frozen for the
// dialog's lifetime: if the target were ever re-resolved in place, an untouched box
// would start claiming to propose the PREVIOUS line's text.
const seedText = props.selectedText
const proposedText = ref(seedText)

const isProposingCorrection = computed(() => proposedText.value !== seedText)

/** Puts the box back to the text as it reads now, which withdraws the proposal. */
function resetProposedText(): void {
  proposedText.value = seedText
}

/** 1-based, for a human to read. 0 when no line could be identified. */
const lineNumber = computed(() => (props.lineIndex >= 0 ? props.lineIndex + 1 : 0))

const context = computed(() =>
  extractContextAround(props.originalLine, props.selectionOffset, props.selectedText.length),
)

// The description is mandatory. A bare proposal would leave a collector guessing WHY
// the text should change, and the correction editor is pre-filled with the current
// text, so "the user touched it" is far too easy to do by accident to stand alone.
const hasSomethingToReport = computed(() => errorDetails.value.trim() !== '')

function buildPayload() {
  // An untouched box sends null (no proposal). An edited one sends its text verbatim
  // INCLUDING the empty string — that is the user proposing a deletion, not an absence.
  const proposal = isProposingCorrection.value ? proposedText.value : null
  return buildErrorReportPayload({
    senderEmail: senderEmail.value.trim(),
    bookTitle: props.bookTitle,
    currentRef: props.currentRef,
    lineNumber: lineNumber.value,
    lineIndex: props.lineIndex,
    bookId: props.bookId,
    selectedText: props.selectedText,
    errorDetails: errorDetails.value.trim(),
    originalLine: props.originalLine,
    selectionOffset: props.selectionOffset,
    proposedText: proposal,
    source: props.source ?? UNRESOLVED_SOURCE,
  })
}

/**
 * Can a proposed correction actually be applied by whoever receives it?
 *
 * Only when the database confirmed the line is still the one on screen. Said in the
 * dialog rather than discovered after sending, so the user knows their proposal will
 * travel as a description rather than as an applicable change.
 */
const isCorrectionApplicable = computed(() => props.source?.location != null)
const isResolvingSource = computed(() => props.source == null)

/** The email is remembered on every successful action, never on a cancel. */
function rememberSenderEmail(): void {
  settingsStore.errorReportSenderEmail = senderEmail.value.trim()
}

/**
 * The description is what makes a report actionable, and the always-open correction
 * box invites a user to write the fix there and press send. Saying so beats a dead
 * button: the keyboard path (Ctrl+Enter) has no hover affordance to explain itself.
 */
function warnDescriptionRequired(): boolean {
  if (hasSomethingToReport.value) return false
  showToast('נא למלא את תיאור הטעות', { variant: 'error' })
  return true
}

async function onSendNow(): Promise<void> {
  if (isSending.value || warnDescriptionRequired()) return
  isSending.value = true
  try {
    // A send that beats the database reads would stamp the report as unverifiable and
    // silently drop a correction the line could have carried. Wait for them.
    await props.awaitSource()
    const result = await sendErrorReport(buildPayload())
    rememberSenderEmail()
    if (result.outcome === 'sent') showToast('הדיווח נשלח. תודה!', { variant: 'success' })
    else if (result.outcome === 'duplicate') showToast('הטעות כבר דווחה. תודה!', { variant: 'success' })
    else if (result.outcome === 'queued') showToast('אין חיבור — הדיווח נשמר וישלח בהמשך')
    else showToast('שליחת הדיווח נדחתה', { variant: 'error' })
    emit('close')
  } finally {
    isSending.value = false
  }
}

async function onSaveForLater(): Promise<void> {
  if (isSending.value || warnDescriptionRequired()) return
  isSending.value = true
  try {
    // Same reason as onSendNow: a queued report is sent later unchanged, so it has to
    // carry its provenance from the start.
    await props.awaitSource()
    queueErrorReport(buildPayload())
    rememberSenderEmail()
    showToast('הדיווח נשמר וישלח בהמשך')
    emit('close')
  } finally {
    isSending.value = false
  }
}

useEventListener('keydown', (event: KeyboardEvent) => {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
    return
  }
  // Enter sends — but NOT while the caret is in one of the multi-line boxes, where
  // Enter is how the user writes a new line. Ctrl+Enter sends from there instead.
  if (event.key !== 'Enter') return
  const inTextarea = (event.target as HTMLElement | null)?.tagName === 'TEXTAREA'
  if (inTextarea && !event.ctrlKey) return
  event.preventDefault()
  void onSendNow()
})
</script>

<template>
  <Teleport to="body">
    <div class="report-backdrop" @click.self="emit('close')">
      <div class="report-dialog">
        <div class="report-titlebar">
          <p class="report-title">דיווח על טעות בספר</p>
          <button
            class="report-close-btn"
            type="button"
            aria-label="סגירה"
            @click="emit('close')"
          >
            &#10005;
          </button>
        </div>

        <dl class="report-location">
          <dt>ספר</dt>
          <dd>{{ bookTitle }}</dd>
          <dt>מקור</dt>
          <dd>{{ currentRef }}</dd>
          <dt>שורה</dt>
          <dd>{{ lineNumber > 0 ? lineNumber : '—' }}</dd>
        </dl>

        <label class="report-field">
          <span class="report-label">הטקסט שנבחר</span>
          <p class="report-selected-text report-book-text">{{ selectedText }}</p>
        </label>

        <label class="report-field">
          <span class="report-label">מה הטעות? <span class="report-required">שדה חובה</span></span>
          <textarea
            v-model="errorDetails"
            class="report-textarea"
            rows="3"
            placeholder="תיאור הטעות"
          ></textarea>
        </label>

        <div class="report-field">
          <span class="report-label">הצעת נוסח מתוקן</span>
          <textarea
            v-model="proposedText"
            class="report-textarea report-book-text"
            rows="3"
            dir="rtl"
          ></textarea>
          <!-- The untouched words on either side: what the server re-anchors the
               correction by, shown so the user can see the proposal replaces only
               the middle. -->
          <p v-if="context.before || context.after" class="report-context">
            {{ context.before }} <strong>[הנוסח המתוקן]</strong> {{ context.after }}
          </p>
          <!-- An unverified line means the proposal travels as a description only.
               Said here, before sending, rather than silently dropped. -->
          <p v-if="isProposingCorrection && !isResolvingSource && !isCorrectionApplicable" class="report-warning">
            לא ניתן לאמת את השורה מול הספריה, ולכן ההצעה תישלח כתיאור בלבד ולא כתיקון
            שניתן להחיל אוטומטית.
          </p>
          <button
            v-if="isProposingCorrection"
            class="report-link-btn"
            type="button"
            @click="resetProposedText"
          >
            החזרת הנוסח המקורי
          </button>
        </div>

        <label class="report-field">
          <span class="report-label">כתובת דוא״ל (לא חובה)</span>
          <input
            v-model="senderEmail"
            class="report-input"
            type="email"
            dir="ltr"
            placeholder="name@example.com"
          />
        </label>

        <p v-if="!hasErrorReportEndpoint()" class="report-note">
          שרת הדיווחים טרם הוגדר. הדיווח יישמר וישלח אוטומטית כשיוגדר.
        </p>

        <div class="report-actions">
          <button
            class="report-send-btn"
            type="button"
            :disabled="!hasSomethingToReport || isSending"
            @click="onSendNow"
          >
            {{ isSending ? 'שולח…' : 'שלח עכשיו' }}
          </button>
          <button
            class="report-later-btn"
            type="button"
            :disabled="!hasSomethingToReport || isSending"
            @click="onSaveForLater"
          >
            שמור לשליחה מאוחרת
          </button>
          <button class="report-cancel-btn" type="button" @click="emit('close')">ביטול</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.report-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.report-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 460px;
  max-width: calc(100vw - 32px);
  max-height: calc(100vh - 64px);
  overflow-y: auto;
  direction: rtl;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.report-titlebar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.report-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

/* Matches the dialog's other dismissals (Escape, backdrop click) — a plain affordance
   in the corner, not an action button competing with the three at the bottom. */
.report-close-btn {
  flex: none;
  width: 24px;
  height: 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  border: none;
  border-radius: 4px;
  background: transparent;
  color: var(--text-secondary);
  font-size: 13px;
  line-height: 1;
  cursor: pointer;
}

.report-close-btn:hover {
  background: var(--bg-hover, rgba(127, 127, 127, 0.16));
  color: var(--text-primary);
}

/* Sits inside the label, so it stays attached to the field it qualifies. */
.report-required {
  font-weight: 400;
  color: var(--text-secondary);
}

/* The location block is reference data, not something to fill in — a definition list
   keeps each label bound to its value for a screen reader too. */
.report-location {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 2px 10px;
  margin: 0;
  font-size: 12px;
}
.report-location dt {
  color: var(--text-secondary);
}
.report-location dd {
  margin: 0;
  color: var(--text-primary);
  overflow-wrap: anywhere;
}

.report-field {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.report-label {
  font-size: 12px;
  color: var(--text-secondary);
}

/* The two fields that hold BOOK TEXT — the quoted line and the proposed replacement
   — are pinned to one fixed face rather than var(--text-font). The reader's chosen
   book font is a display preference that can be any installed family, and a report is
   a comparison between what the library says and what the user typed: both halves have
   to be set in the same metrics for that comparison to mean anything. Frank Ruehl
   Libre ships with the app (main.css), so this never waits on a network fetch. */
.report-book-text {
  font-family: 'Frank Ruehl Libre', var(--text-font), serif;
  /* Matched to the quoted line above, not to the surrounding form controls: the user
     is reading the two against each other. */
  font-size: 14px;
  line-height: 1.7;
}

.report-selected-text {
  margin: 0;
  max-height: 120px;
  overflow-y: auto;
  padding: 8px 10px;
  font-size: 14px;
  line-height: 1.7;
  color: var(--text-primary);
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  white-space: pre-wrap;
}

.report-textarea,
.report-input {
  width: 100%;
  box-sizing: border-box;
  padding: 7px 9px;
  font-family: inherit;
  font-size: 13px;
  color: var(--text-primary);
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  resize: vertical;
}
.report-textarea:focus,
.report-input:focus {
  outline: 1px solid var(--accent-color, #3478f6);
}

.report-context {
  margin: 0;
  font-size: 12px;
  line-height: 1.6;
  color: var(--text-secondary);
}

.report-note {
  margin: 0;
  font-size: 12px;
  line-height: 1.6;
  color: var(--text-secondary);
}

/* A consequence the user should see before sending, not a failure — warning weight
   rather than error weight. */
.report-warning {
  margin: 0;
  font-size: 12px;
  line-height: 1.6;
  color: var(--text-warning, var(--text-secondary));
}

/* A text-only action inside the form body — it opens or abandons the proposal, it is
   not one of the dialog's three outcomes. */
.report-link-btn {
  align-self: flex-start;
  padding: 0;
  font-size: 12px;
  color: var(--accent-color, #3478f6);
  background: none;
  border: none;
  cursor: pointer;
}
.report-link-btn:hover {
  text-decoration: underline;
}

.report-actions {
  display: flex;
  flex-direction: row-reverse;
  justify-content: flex-start;
  gap: 8px;
  padding-top: 10px;
  margin-top: 2px;
  border-top: 1px solid var(--border-color);
}

.report-actions button {
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
  border-radius: 4px;
}
.report-actions button:disabled {
  opacity: 0.5;
  cursor: default;
}

.report-send-btn {
  color: var(--accent-color, #3478f6);
  border: 1px solid color-mix(in srgb, var(--accent-color, #3478f6) 40%, transparent);
  background: color-mix(in srgb, var(--accent-color, #3478f6) 8%, transparent);
}
.report-send-btn:hover:not(:disabled) {
  background: color-mix(in srgb, var(--accent-color, #3478f6) 16%, transparent);
}

.report-later-btn,
.report-cancel-btn {
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  background: var(--control-bg);
}
.report-later-btn:hover:not(:disabled),
.report-cancel-btn:hover {
  background: var(--control-bg-hover);
}
</style>
