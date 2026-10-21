<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import { useSelfUpdate } from '@/composables/useSelfUpdate'
import {
  updatePromptBody,
  UPDATE_INSTALL_LABEL,
  UPDATE_INSTALL_FAILED,
} from '@/components/updateAvailableTexts'

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
  isAutomaticUpdateTurnedOff,
  loadUpdateStatus,
  checkForUpdateNow,
  installUpdate,
  loadAutomaticUpdateTurnedOff,
  applyAutomaticUpdateTurnedOff,
} = useSelfUpdate()

const isUpdateStatusLoaded = ref(false)

onMounted(async () => {
  await loadAutomaticUpdateTurnedOff()
  await loadUpdateStatus()
  isUpdateStatusLoaded.value = true
})

// ── Update status line ────────────────────────────────────────────────────────

const updateStatusText = computed(() => {
  if (!isUpdateStatusLoaded.value) return 'טוען...'
  if (isUpdateStatusUnavailable.value) return 'לא ניתן לקבל את מצב העדכונים כרגע — שירות הנתונים אינו זמין.'
  if (readyVersion.value) return updatePromptBody(readyVersion.value)
  if (isAutomaticUpdateTurnedOff.value) return 'בדיקת העדכונים האוטומטית כבויה.'
  if (isCheckingForUpdate.value) return 'בודק אם יש גרסה חדשה. אם יש, היא תורד עכשיו — זה עשוי לקחת כמה דקות.'
  if (hasCheckFailed.value) return 'לא ניתן היה לבדוק אם יש עדכון. נא לנסות שוב מאוחר יותר.'
  if (hasCheckedForUpdate.value) return 'הגרסה המותקנת היא העדכנית ביותר.'
  return 'אין עדכון שמוכן להתקנה. התוכנה בודקת אוטומטית מידי פעם אם יש גרסה חדשה ומורידה אותה ברקע.'
})

// Hidden while updates are turned off, and once an update is ready (install is the next step).
const isCheckNowVisible = computed(
  () => isUpdateStatusLoaded.value && !isAutomaticUpdateTurnedOff.value && !readyVersion.value,
)
</script>

<template>
  <!-- ── עדכונים ── -->
  <div data-section="section-updates" data-section-label="עדכונים">
    <div id="section-updates" class="section-label">עדכונים</div>

    <SettingRow label="מצב עדכון">
      <div class="updates-status">
        <span v-if="installedVersion" class="updates-status-text">גרסה מותקנת: {{ installedVersion }}</span>
        <span class="updates-status-text">{{ updateStatusText }}</span>
        <MessageBanner v-if="hasInstallFailed" :message="UPDATE_INSTALL_FAILED" :dismissible="false" />
        <button
          v-if="readyVersion"
          class="settings-btn updates-install-button"
          :disabled="isInstalling"
          @click="installUpdate"
        >
          {{ UPDATE_INSTALL_LABEL }}
        </button>
        <span v-if="isInstalling" class="updates-status-text">אנא המתן כמה רגעים עד לפתיחת המתקין</span>
        <button v-if="isCheckNowVisible" class="settings-btn" :disabled="isCheckingForUpdate" @click="checkForUpdateNow">
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
        :model-value="isAutomaticUpdateTurnedOff"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
        @update:model-value="applyAutomaticUpdateTurnedOff"
      />
    </SettingRow>
  </div>
</template>

<style scoped>
.updates-status {
  width: 100%;
}

.updates-status-text {
  font-size: 12px;
  color: var(--text-secondary);
}

.updates-install-button {
  color: var(--text-on-accent, #fff);
  background: var(--accent-color);
  border-color: var(--accent-color);
}

.updates-install-button:hover:not(:disabled) {
  background: color-mix(in srgb, var(--accent-color) 85%, #000);
}
</style>
