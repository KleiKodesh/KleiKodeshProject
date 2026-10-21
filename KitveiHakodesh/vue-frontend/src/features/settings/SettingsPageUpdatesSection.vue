<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import { useSelfUpdate } from '@/composables/useSelfUpdate'
import { useI18n } from 'vue-i18n'
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

const { t } = useI18n()

// ── Update status line ────────────────────────────────────────────────────────

const updateStatusText = computed(() => {
  if (!isUpdateStatusLoaded.value) return t('settings.updates.loading')
  if (isUpdateStatusUnavailable.value) return t('settings.updates.unavailable')
  if (readyVersion.value) return updatePromptBody(readyVersion.value)
  if (isAutomaticUpdateTurnedOff.value) return t('settings.updates.off')
  if (isCheckingForUpdate.value) return t('settings.updates.checking')
  if (hasCheckFailed.value) return t('settings.updates.failed')
  if (hasCheckedForUpdate.value) return t('settings.updates.upToDate')
  return t('settings.updates.idle')
})

// Hidden while updates are turned off, and once an update is ready (install is the next step).
const isCheckNowVisible = computed(
  () => isUpdateStatusLoaded.value && !isAutomaticUpdateTurnedOff.value && !readyVersion.value,
)
</script>

<template>
  <!-- ── עדכונים ── -->
  <div data-section="section-updates" :data-section-label="$t('settings.updates.section')">
    <div id="section-updates" class="section-label">{{ $t('settings.updates.section') }}</div>

    <SettingRow :label="$t('settings.updates.status')">
      <div class="updates-status">
        <span v-if="installedVersion" class="updates-status-text">{{ $t('settings.updates.installed', { version: installedVersion }) }}</span>
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
        <span v-if="isInstalling" class="updates-status-text">{{ $t('settings.updates.waitInstaller') }}</span>
        <button v-if="isCheckNowVisible" class="settings-btn" :disabled="isCheckingForUpdate" @click="checkForUpdateNow">
          {{ isCheckingForUpdate ? $t('settings.updates.checkingShort') : $t('settings.updates.checkNow') }}
        </button>
      </div>
    </SettingRow>

    <SettingRow
      id="nav-turn-off-updates"
      :data-nav-label="$t('settings.updates.disableNav')"
      :label="$t('settings.updates.disable')"
      :hint="$t('settings.updates.disableHint')"
    >
      <ToggleGroup
        :model-value="isAutomaticUpdateTurnedOff"
        :options="[
          { label: $t('settings.yes'), value: true },
          { label: $t('settings.no'), value: false },
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
