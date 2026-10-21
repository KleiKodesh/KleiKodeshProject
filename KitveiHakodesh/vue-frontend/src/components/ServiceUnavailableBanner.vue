<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import { getDbPathInfo, pickDbPath } from '@/webview-host/bridge'
import { onDbPathChanged } from '@/webview-host/seforimDb'
import { useSettingsStore } from '@/stores/settingsStore'
import {
  serviceUnavailableReason,
  isRetryingService,
  retryService,
} from '@/webview-host/serviceAvailability'
import {
  SERVICE_UNAVAILABLE_TEXTS,
  SERVICE_ACTION_LABELS,
  SERVICE_RETRYING_LABEL,
  SEFORIM_DB_UPDATE_INTERRUPTED_TEXTS,
  SEFORIM_DB_MISSING_TEXTS,
  SEFORIM_DB_UNREADABLE_TEXTS,
  SEFORIM_DB_PICK_LABEL,
} from './serviceUnavailableTexts'

// The one place the app says the KitveiHakodesh service cannot be reached, and why. Every data
// op goes through the service, so instead of each feature failing on its own, serviceClient
// records the failure (serviceAvailability.ts) and this banner explains it with a
// button that asks before it acts: only a click on it may raise the Windows admin prompt
// (install / repair when needed) - no data request ever does. Mount ONCE at the app root.

// The service is up but the seforim DB itself is the problem, one of three ways:
//  - 'interrupted': Otzaria's update stopped half-way; the hot journal beside the file makes
//    every read-only query fail. Once Otzaria finishes, the page reloads so every view
//    fetches its data again. No button - never touches the files.
//  - 'missing': no file at the configured path. Only after setup (the wizard handles the
//    first pick). Button opens the native picker.
//  - 'unreadable': the file exists but cannot be opened (not SQLite, damaged, locked).
//    Button opens the native picker.
// Checked every few seconds (cheap file tests in the service).
type DbProblem = 'interrupted' | 'missing' | 'unreadable' | null
const settingsStore = useSettingsStore()
const dbProblem = ref<DbProblem>(null)
const dbPath = ref('')
async function checkDbProblem() {
  if (serviceUnavailableReason.value) return
  const info = await getDbPathInfo()
  if (!info) return // a failed poll is not "cleared" — keep the current state
  dbPath.value = info.path
  const problem: DbProblem = info.updateInterrupted
    ? 'interrupted'
    : !info.exists
      ? (settingsStore.setupDone ? 'missing' : null)
      : info.readable === false ? 'unreadable' : null
  if (dbProblem.value && !problem) location.reload()
  dbProblem.value = problem
}
useIntervalFn(checkDbProblem, 5000, { immediateCallback: true })

const texts = computed(() => {
  if (serviceUnavailableReason.value) return SERVICE_UNAVAILABLE_TEXTS[serviceUnavailableReason.value]
  switch (dbProblem.value) {
    case 'interrupted': return SEFORIM_DB_UPDATE_INTERRUPTED_TEXTS
    case 'missing': return SEFORIM_DB_MISSING_TEXTS
    case 'unreadable': return SEFORIM_DB_UNREADABLE_TEXTS
    default: return null
  }
})

const canPickLibrary = computed(
  () => !serviceUnavailableReason.value && (dbProblem.value === 'missing' || dbProblem.value === 'unreadable'),
)
const isPickingLibrary = ref(false)
async function pickLibrary() {
  isPickingLibrary.value = true
  try {
    const previous = dbPath.value
    const picked = await pickDbPath()
    if (!picked) return
    await onDbPathChanged(previous, picked)
    location.reload()
  } finally {
    isPickingLibrary.value = false
  }
}

// The user may close the banner; a DIFFERENT message (or the same one after it cleared) shows again.
const dismissed = ref(false)
watch(texts, () => { dismissed.value = false })
</script>

<template>
  <Teleport to="body">
    <Transition name="service-banner">
      <div v-if="texts && !dismissed" class="service-banner" role="alert">
        <span class="service-banner-icon" aria-hidden="true">
          <svg viewBox="0 0 20 20" width="18" height="18" fill="currentColor">
            <path d="M10 2a8 8 0 1 1 0 16 8 8 0 0 1 0-16Zm0 10.5a1 1 0 1 0 0 2 1 1 0 0 0 0-2Zm0-7a.75.75 0 0 0-.74.65l-.01.1v4.5a.75.75 0 0 0 1.49.1l.01-.1v-4.5A.75.75 0 0 0 10 5.5Z"/>
          </svg>
        </span>
        <div class="service-banner-text">
          <strong>{{ texts.title }}</strong>
          <span>{{ texts.body }}</span>
        </div>
        <button
          v-if="serviceUnavailableReason"
          class="service-banner-retry"
          :disabled="isRetryingService"
          @click="retryService"
        >
          {{ isRetryingService ? SERVICE_RETRYING_LABEL : SERVICE_ACTION_LABELS[serviceUnavailableReason!] }}
        </button>
        <button
          v-else-if="canPickLibrary"
          class="service-banner-retry"
          :disabled="isPickingLibrary"
          @click="pickLibrary"
        >
          {{ SEFORIM_DB_PICK_LABEL }}
        </button>
        <button class="service-banner-close" :aria-label="$t('components.close')" @click="dismissed = true">
          <svg viewBox="0 0 12 12" width="12" height="12" stroke="currentColor" stroke-width="1.5" aria-hidden="true">
            <path d="M2 2l8 8M10 2l-8 8"/>
          </svg>
        </button>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.service-banner {
  /* Pinned 16px from both edges and centred by auto margins - NOT left:50% + translate,
     which caps a shrink-to-fit box at HALF the viewport and, in a narrow Word task pane,
     broke the text after every word. */
  position: fixed;
  top: 12px;
  left: 16px;
  right: 16px;
  margin-inline: auto;
  width: fit-content;
  max-width: 640px;
  z-index: 10002;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  padding: 12px 16px;
  direction: rtl;
  font-family: var(--header-font);
  font-size: 13px;
  line-height: 1.45;
  color: var(--text-primary);
  background: var(--bg-primary);
  border: 1px solid var(--status-danger);
  border-radius: 8px;
  box-shadow: 0 8px 16px rgba(0, 0, 0, 0.14), 0 0 2px rgba(0, 0, 0, 0.12);
}

.service-banner-icon {
  flex-shrink: 0;
  display: inline-flex;
  color: var(--status-danger);
}

.service-banner-text {
  /* At least ~16 em of text before the button wraps onto its own line. */
  flex: 1 1 16em;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.service-banner-retry {
  flex-shrink: 0;
  margin-inline-start: auto;
  padding: 6px 14px;
  font: inherit;
  color: var(--text-on-accent, #fff);
  background: var(--accent-color);
  border: none;
  border-radius: 4px;
  cursor: pointer;
}
.service-banner-retry:disabled {
  opacity: 0.6;
  cursor: default;
}

.service-banner-close {
  flex-shrink: 0;
  display: inline-flex;
  padding: 4px;
  color: var(--text-secondary);
  background: none;
  border: none;
  border-radius: 4px;
  cursor: pointer;
}
.service-banner-close:hover {
  color: var(--text-primary);
}

.service-banner-enter-active,
.service-banner-leave-active {
  transition: opacity 250ms ease, transform 250ms ease;
}
.service-banner-enter-from,
.service-banner-leave-to {
  opacity: 0;
  transform: translateY(-12px);
}
</style>
