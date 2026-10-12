<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { serviceCall } from '@/webview-host/serviceClient'
import {
  UPDATE_PROMPT_TITLE,
  updatePromptBody,
  UPDATE_INSTALL_LABEL,
  UPDATE_LATER_LABEL,
  UPDATE_INSTALL_FAILED,
} from './updateAvailableTexts'

// Asks the user before an update is installed. The KitveiHakodesh service checks for updates
// once a week and downloads them in the background, but never launches the installer on its
// own: this prompt asks the service whether a downloaded update is ready (getUpdateStatus) and,
// only when the user agrees, asks it to start the installer (installUpdate) - which then asks
// to close Word and the app. "Later" hides the prompt until the app is opened again.
// Mount ONCE at the app root.

const FIRST_CHECK_DELAY_MS = 30_000
const CHECK_INTERVAL_MS = 6 * 60 * 60 * 1000

const readyVersion = ref<string | null>(null)
const isInstalling = ref(false)
const failed = ref(false)
let dismissedVersion: string | null = null
let firstTimer: ReturnType<typeof setTimeout> | null = null
let intervalTimer: ReturnType<typeof setInterval> | null = null

async function check() {
  try {
    const status = await serviceCall<{ ready?: boolean; version?: string }>('getUpdateStatus')
    const version = status?.ready ? status.version ?? '' : null
    readyVersion.value = version && version !== dismissedVersion ? version : null
  } catch {
    /* the service banner covers an unreachable service */
  }
}

async function install() {
  if (isInstalling.value) return
  isInstalling.value = true
  failed.value = false
  try {
    const reply = await serviceCall<{ ok?: boolean }>('installUpdate')
    if (reply?.ok) readyVersion.value = null
    else failed.value = true
  } catch {
    failed.value = true
  } finally {
    isInstalling.value = false
  }
}

function later() {
  dismissedVersion = readyVersion.value
  readyVersion.value = null
}

onMounted(() => {
  firstTimer = setTimeout(check, FIRST_CHECK_DELAY_MS)
  intervalTimer = setInterval(check, CHECK_INTERVAL_MS)
})

onBeforeUnmount(() => {
  if (firstTimer) clearTimeout(firstTimer)
  if (intervalTimer) clearInterval(intervalTimer)
})
</script>

<template>
  <Teleport to="body">
    <Transition name="update-prompt">
      <div v-if="readyVersion" class="update-prompt" role="dialog" aria-live="polite">
        <div class="update-prompt-text">
          <strong>{{ UPDATE_PROMPT_TITLE }}</strong>
          <span>{{ updatePromptBody(readyVersion) }}</span>
          <span v-if="failed" class="update-prompt-error">{{ UPDATE_INSTALL_FAILED }}</span>
        </div>
        <div class="update-prompt-actions">
          <button class="update-prompt-install" :disabled="isInstalling" @click="install">
            {{ UPDATE_INSTALL_LABEL }}
          </button>
          <button class="update-prompt-later" :disabled="isInstalling" @click="later">
            {{ UPDATE_LATER_LABEL }}
          </button>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.update-prompt {
  /* Same placement rule as ServiceUnavailableBanner: pinned from both edges so a narrow
     Word task pane gets the full width, never half of it. Bottom corner, like the toast. */
  position: fixed;
  bottom: 20px;
  left: 16px;
  right: 16px;
  margin-inline: auto;
  width: fit-content;
  max-width: 480px;
  z-index: 10001;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px 14px;
  padding: 14px 16px;
  direction: rtl;
  font-family: var(--header-font);
  font-size: 13px;
  line-height: 1.45;
  color: var(--text-primary);
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 8px 16px rgba(0, 0, 0, 0.14), 0 0 2px rgba(0, 0, 0, 0.12);
}

.update-prompt-text {
  flex: 1 1 16em;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.update-prompt-error {
  color: var(--status-danger);
}

.update-prompt-actions {
  display: flex;
  gap: 8px;
  margin-inline-start: auto;
}

.update-prompt-actions button {
  padding: 6px 14px;
  font: inherit;
  border-radius: 4px;
  cursor: pointer;
}
.update-prompt-actions button:disabled {
  opacity: 0.6;
  cursor: default;
}

.update-prompt-install {
  color: var(--text-on-accent, #fff);
  background: var(--accent-color);
  border: none;
}

.update-prompt-later {
  color: var(--text-primary);
  background: transparent;
  border: 1px solid var(--border-color);
}

.update-prompt-enter-active,
.update-prompt-leave-active {
  transition: opacity 250ms ease, transform 250ms ease;
}
.update-prompt-enter-from,
.update-prompt-leave-to {
  opacity: 0;
  transform: translateY(12px);
}
</style>
