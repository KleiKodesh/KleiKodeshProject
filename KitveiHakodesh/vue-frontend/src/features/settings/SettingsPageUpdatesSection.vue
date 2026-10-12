<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import { useSelfUpdate } from '@/composables/useSelfUpdate'
import {
  updatePromptBody,
  UPDATE_INSTALL_LABEL,
  UPDATE_INSTALL_FAILED,
} from '@/components/updateAvailableTexts'
import { getTurnOffUpdates, setTurnOffUpdates } from '@/webview-host/bridge'

// The KitveiHakodesh service checks for updates once a week and downloads them in the
// background; it never installs on its own. This section shows the installed version and
// whether a downloaded update is ready, runs a check on demand, installs through the same path
// as UpdateAvailablePrompt (useSelfUpdate), and holds the "turn off automatic updates" toggle.

const {
  readyVersion,
  installedVersion,
  isUpdateStatusUnavailable,
  isCheckingForUpdate,
  hasCheckedForUpdate,
  hasCheckFailed,
  isInstalling,
  hasInstallFailed,
  loadUpdateStatus,
  checkForUpdateNow,
  installUpdate,
} = useSelfUpdate()

const isUpdateStatusLoaded = ref(false)

// ── Automatic updates ─────────────────────────────────────────────────────────
// Backed by the shared VSTO registry key (KleiKodesh\UpdateChecker\TurnOffUpdates),
// read/written directly via the host bridge — not the settings store (which persists
// to localStorage). true = the automatic update check is turned OFF.

const turnOffUpdates = ref(false)

onMounted(async () => {
  const value = await getTurnOffUpdates()
  if (value !== null) turnOffUpdates.value = value
  await loadUpdateStatus()
  isUpdateStatusLoaded.value = true
})

// The service reports no ready update while updates are turned off, so the status is
// read again after every change.
async function applyTurnOffUpdates(value: boolean) {
  turnOffUpdates.value = value
  await setTurnOffUpdates(value)
  await loadUpdateStatus()
}

// ── Update status line ────────────────────────────────────────────────────────

const updateStatusText = computed(() => {
  if (!isUpdateStatusLoaded.value) return 'טוען...'
  if (isUpdateStatusUnavailable.value) return 'לא ניתן לקבל את מצב העדכונים כרגע — שירות הנתונים אינו זמין.'
  if (readyVersion.value) return updatePromptBody(readyVersion.value)
  if (turnOffUpdates.value) return 'בדיקת העדכונים האוטומטית כבויה.'
  if (isCheckingForUpdate.value) return 'בודק אם יש גרסה חדשה. אם יש, היא תורד עכשיו — זה עשוי לקחת כמה דקות.'
  if (hasCheckFailed.value) return 'לא ניתן היה לבדוק אם יש עדכון. נא לנסות שוב מאוחר יותר.'
  if (hasCheckedForUpdate.value) return 'הגרסה המותקנת היא העדכנית ביותר.'
  return 'אין עדכון שמוכן להתקנה. התוכנה בודקת אוטומטית פעם בשבוע אם יש גרסה חדשה ומורידה אותה ברקע.'
})

// Hidden while updates are turned off, and once an update is ready (install is the next step).
const isCheckNowVisible = computed(
  () => isUpdateStatusLoaded.value && !turnOffUpdates.value && !readyVersion.value,
)
</script>

<template>
  <!-- ── עדכונים ── -->
  <div data-section="section-updates" data-section-label="עדכונים">
    <div id="section-updates" class="section-label">עדכונים</div>

    <SettingRow v-if="installedVersion" label="גרסה מותקנת">
      <span class="updates-status-text">{{ installedVersion }}</span>
    </SettingRow>

    <SettingRow label="מצב עדכון">
      <div class="updates-status">
        <span class="updates-status-text">{{ updateStatusText }}</span>
        <span v-if="hasInstallFailed" class="updates-status-error">{{ UPDATE_INSTALL_FAILED }}</span>
        <button
          v-if="readyVersion"
          class="updates-install-button"
          :disabled="isInstalling"
          @click="installUpdate"
        >
          {{ UPDATE_INSTALL_LABEL }}
        </button>
        <button
          v-if="isCheckNowVisible"
          class="updates-check-button"
          :disabled="isCheckingForUpdate"
          @click="checkForUpdateNow"
        >
          {{ isCheckingForUpdate ? 'בודק...' : 'בדוק עכשיו' }}
        </button>
      </div>
    </SettingRow>

    <SettingRow
      id="nav-turn-off-updates"
      data-nav-label="כבה בדיקת עדכונים"
      label="כבה בדיקת עדכונים אוטומטית"
      hint="בבחירת 'כן' התוכנה לא תבדוק אם יש גרסה חדשה ולא תוריד אותה. ההגדרה משותפת עם התוסף לוורד."
    >
      <ToggleGroup
        :model-value="turnOffUpdates"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
        @update:model-value="applyTurnOffUpdates"
      />
    </SettingRow>
  </div>
</template>

<style scoped>
.updates-status {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 8px;
  width: 100%;
}

.updates-status-text {
  font-size: 12px;
  color: var(--text-secondary);
}

.updates-status-error {
  font-size: 12px;
  color: var(--status-danger);
}

.updates-install-button {
  height: 28px;
  padding: 0 14px;
  font-size: 12px;
  color: var(--text-on-accent, #fff);
  background: var(--accent-color);
  border: none;
  border-radius: 4px;
}

.updates-check-button {
  height: 28px;
  padding: 0 14px;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
}

.updates-check-button:hover:not(:disabled) {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.updates-install-button:disabled,
.updates-check-button:disabled {
  opacity: 0.6;
  cursor: default;
}
</style>
