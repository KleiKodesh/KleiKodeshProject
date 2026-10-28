<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import SettingRow from './SettingRow.vue'
import SettingsPageReportProblemRow from './SettingsPageReportProblemRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import { useSelfUpdate } from '@/composables/useSelfUpdate'
import { useUpdateDownloadProgress } from '@/composables/useUpdateDownloadProgress'
import { useI18n } from 'vue-i18n'
import {
  updatePromptBody,
  UPDATE_INSTALL_LABEL,
  UPDATE_INSTALL_FAILED,
} from '@/components/updateAvailableTexts'

// The KitveiHakodesh service checks for updates (every app start, and weekly) and downloads them
// in the background; it never installs on its own. This section shows the installed version,
// a live progress bar while the service downloads (pushed, useUpdateDownloadProgress), whether
// a downloaded update is ready, runs a check on demand, installs through the same path
// as the nav update row (useSelfUpdate), and holds the "turn off automatic updates" toggle.
// The nav row (appNavUpdateItem) is the quiet, always-there signal; this section is the
// detailed view.

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

const { progress: downloadProgress } = useUpdateDownloadProgress()
const isDownloading = computed(() => !readyVersion.value && downloadProgress.value.downloadingVersion !== '')

const isUpdateStatusLoaded = ref(false)

onMounted(async () => {
  await Promise.all([loadAutomaticUpdateTurnedOff(), loadUpdateStatus()])
  isUpdateStatusLoaded.value = true
})

const { t } = useI18n()

// ── Update status line ────────────────────────────────────────────────────────

const updateStatusText = computed(() => {
  if (!isUpdateStatusLoaded.value) return t('settings.updates.loading')
  if (isUpdateStatusUnavailable.value) return t('settings.updates.unavailable')
  if (readyVersion.value) return updatePromptBody(readyVersion.value)
  if (isDownloading.value)
    return t('settings.updates.downloading', {
      version: downloadProgress.value.downloadingVersion,
      percent: downloadProgress.value.percent,
    })
  if (isAutomaticUpdateTurnedOff.value) return t('settings.updates.off')
  if (isCheckingForUpdate.value) return t('settings.updates.checking')
  if (hasCheckFailed.value) return t('settings.updates.failed')
  if (hasCheckedForUpdate.value) return t('settings.updates.upToDate')
  return t('settings.updates.idle')
})

// Hidden while updates are turned off, and once an update is ready (install is the next step).
const isCheckNowVisible = computed(
  () =>
    isUpdateStatusLoaded.value && !isAutomaticUpdateTurnedOff.value && !readyVersion.value && !isDownloading.value,
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
        <div
          v-if="isDownloading"
          class="updates-progress-track"
          role="progressbar"
          aria-valuemin="0"
          aria-valuemax="100"
          :aria-valuenow="downloadProgress.percent"
        >
          <div class="updates-progress-fill" :style="{ width: downloadProgress.percent + '%' }" />
        </div>
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

    <SettingsPageReportProblemRow />
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

.updates-progress-track {
  height: 4px;
  margin-block: 6px;
  border-radius: 2px;
  background: var(--border-color);
  overflow: hidden;
}

.updates-progress-fill {
  height: 100%;
  background: var(--accent-color);
  transition: width 0.25s ease;
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
