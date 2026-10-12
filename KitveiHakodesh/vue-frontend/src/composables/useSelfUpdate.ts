import { ref } from 'vue'
import {
  getUpdateStatus,
  checkForUpdateNow as runUpdateCheckNow,
  installReadyUpdate,
  type UpdateStatus,
} from '@/webview-host/bridge'

/**
 * The app's self-update, shared by UpdateAvailablePrompt (app root) and the settings page's
 * updates section: read the status, run a check now, and install a downloaded update. Each
 * caller gets its own state.
 *
 * readyVersion — the version of the downloaded installer waiting to run, or null.
 * installedVersion — the installed copy's version ('' when unknown / not an installed copy).
 * isUpdateStatusUnavailable — the last status read failed (service unreachable).
 * hasCheckedForUpdate — a check-now has finished in this view (with or without a result).
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

  // Keeps the last known answer when the service cannot reply; a ready version does not
  // vanish because one read failed.
  function applyUpdateStatus(status: UpdateStatus | null) {
    isUpdateStatusUnavailable.value = status === null
    if (status === null) return
    readyVersion.value = status.readyVersion || null
    installedVersion.value = status.installedVersion
  }

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

  return {
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
  }
}
