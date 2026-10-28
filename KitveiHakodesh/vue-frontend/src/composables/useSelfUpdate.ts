import { ref, watch } from 'vue'
import { useUpdateDownloadProgress } from './useUpdateDownloadProgress'
import {
  getUpdateStatus,
  checkForUpdateNow as runUpdateCheckNow,
  installReadyUpdate,
  getTurnOffUpdates,
  setTurnOffUpdates,
  type UpdateStatus,
} from '@/webview-host/bridge'

/**
 * The app's self-update, used by useUpdateNavState (the nav surfaces' shared instance) and the
 * settings page's updates section: read the status, run a check now, install a downloaded update, and read or
 * change the turn-off-automatic-updates flag. Each caller gets its own state.
 *
 * readyVersion — the version of the downloaded installer waiting to run, or null. Also set the
 *   moment the service finishes a download (pushed, useUpdateDownloadProgress).
 * installedVersion — the installed copy's version ('' when unknown / not an installed copy).
 * isUpdateStatusUnavailable — the last status read failed (service unreachable).
 * hasCheckedForUpdate — a check-now has finished in this view (with or without a result).
 * isAutomaticUpdateTurnedOff — the TurnOffUpdates registry value shared with the Word add-in,
 *   read and written through the service, never the settings store. Only callers that show
 *   the toggle load it (loadAutomaticUpdateTurnedOff).
 */
export function useSelfUpdate() {
  const readyVersion = ref<string | null>(null)
  const installedVersion = ref('')
  const isUpdateStatusUnavailable = ref(false)
  const isCheckingForUpdate = ref(false)
  const hasCheckedForUpdate = ref(false)
  const hasCheckFailed = ref(false)
  const isInstalling = ref(false)
  const hasInstallFailed = ref(false)
  const isAutomaticUpdateTurnedOff = ref(false)

  // Keeps the last known answer when the service cannot reply; a ready version does not
  // vanish because one read failed.
  function applyUpdateStatus(status: UpdateStatus | null) {
    isUpdateStatusUnavailable.value = status === null
    if (status === null) return
    readyVersion.value = status.readyVersion || null
    installedVersion.value = status.installedVersion
  }

  // The service pushes a finished download at once; take it without waiting for a status read.
  const { progress } = useUpdateDownloadProgress()
  watch(
    () => progress.value.readyVersion,
    (version) => {
      if (version) readyVersion.value = version
    },
  )

  async function loadUpdateStatus() {
    applyUpdateStatus(await getUpdateStatus())
  }

  // Can take minutes: a newer release is downloaded before the reply comes back.
  async function checkForUpdateNow() {
    if (isCheckingForUpdate.value) return
    isCheckingForUpdate.value = true
    hasCheckFailed.value = false
    const status = await runUpdateCheckNow()
    applyUpdateStatus(status)
    hasCheckFailed.value = status === null || status.checkError !== ''
    hasCheckedForUpdate.value = true
    isCheckingForUpdate.value = false
  }

  async function installUpdate() {
    if (isInstalling.value) return
    isInstalling.value = true
    hasInstallFailed.value = false
    const isStarted = await installReadyUpdate()
    if (isStarted) readyVersion.value = null
    else hasInstallFailed.value = true
    isInstalling.value = false
  }

  // Left unchanged when the service cannot answer.
  async function loadAutomaticUpdateTurnedOff() {
    const value = await getTurnOffUpdates()
    if (value !== null) isAutomaticUpdateTurnedOff.value = value
  }

  // The service reports no ready update while updates are turned off, so the status is
  // read again after every change.
  async function applyAutomaticUpdateTurnedOff(value: boolean) {
    isAutomaticUpdateTurnedOff.value = value
    await setTurnOffUpdates(value)
    await loadUpdateStatus()
  }

  return {
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
  }
}
