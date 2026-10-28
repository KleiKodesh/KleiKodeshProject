import { computed, ref, watch } from 'vue'
import { useSelfUpdate } from './useSelfUpdate'
import { useUpdateDownloadProgress } from './useUpdateDownloadProgress'

/**
 * How the nav surfaces paint the update: the Windows / Firefox / Chrome hybrid.
 *   ready       - downloaded, waiting for a click (accent)
 *   aging       - ready for 2+ days (orange, like Chrome's menu tint)
 *   overdue     - ready for 7+ days (red)
 *   downloading - the service is fetching it (muted, shows the percent)
 *   installing  - the installer was asked to start
 *   failed      - the installer could not be started; a click retries
 */
export type UpdateNavTone = 'ready' | 'aging' | 'overdue' | 'downloading' | 'installing' | 'failed'

const FIRST_STATUS_READ_DELAY_MS = 30_000
const STATUS_READ_INTERVAL_MS = 6 * 60 * 60 * 1000
const AGE_TICK_MS = 60 * 60 * 1000
const AGING_AFTER_MS = 2 * 24 * 60 * 60 * 1000
const OVERDUE_AFTER_MS = 7 * 24 * 60 * 60 * 1000
/** When this browser first saw the ready version: `{ version, at }`. Per viewer, a convenience only. */
const READY_SEEN_KEY = 'kh-update-ready-seen'

interface ReadySeen {
  version: string
  at: number
}

function readReadySeen(): ReadySeen | null {
  try {
    const raw = localStorage.getItem(READY_SEEN_KEY)
    return raw ? (JSON.parse(raw) as ReadySeen) : null
  } catch {
    return null
  }
}

function writeReadySeen(seen: ReadySeen) {
  try {
    localStorage.setItem(READY_SEEN_KEY, JSON.stringify(seen))
  } catch {
    /* no storage - the row simply never ages */
  }
}

let shared: ReturnType<typeof create> | null = null

function create() {
  // ONE self-update instance for every nav surface (hamburger dot, dropdown row, rail
  // button, overflow flyout), so they never disagree about what is ready or installing.
  const update = useSelfUpdate()
  const { progress } = useUpdateDownloadProgress()

  const readySince = ref<number | null>(null)
  const now = ref(Date.now())

  watch(
    update.readyVersion,
    (version) => {
      if (!version) {
        readySince.value = null
        return
      }
      const seen = readReadySeen()
      const at = seen?.version === version ? seen.at : Date.now()
      if (seen?.version !== version) writeReadySeen({ version, at })
      readySince.value = at
    },
    { immediate: true },
  )

  const isDownloading = computed(
    () => !update.readyVersion.value && progress.value.downloadingVersion !== '',
  )

  const tone = computed<UpdateNavTone | null>(() => {
    if (update.isInstalling.value) return 'installing'
    if (update.hasInstallFailed.value) return 'failed'
    if (update.readyVersion.value) {
      const age = now.value - (readySince.value ?? now.value)
      if (age >= OVERDUE_AFTER_MS) return 'overdue'
      if (age >= AGING_AFTER_MS) return 'aging'
      return 'ready'
    }
    if (isDownloading.value) return 'downloading'
    return null
  })

  // Status reads: the pushed stream covers a download that lands while the app is open;
  // these cover one that landed before it opened, and the odd missed frame.
  setTimeout(() => void update.loadUpdateStatus(), FIRST_STATUS_READ_DELAY_MS)
  setInterval(() => void update.loadUpdateStatus(), STATUS_READ_INTERVAL_MS)
  setInterval(() => (now.value = Date.now()), AGE_TICK_MS)

  return {
    tone,
    readyVersion: update.readyVersion,
    downloadingVersion: computed(() => progress.value.downloadingVersion),
    downloadPercent: computed(() => progress.value.percent),
    installUpdate: update.installUpdate,
    /** The raw refs, writable - for the dev test hook (devTestHook.ts) to stage each tone. */
    devRefs: {
      readyVersion: update.readyVersion,
      readySince,
      isInstalling: update.isInstalling,
      hasInstallFailed: update.hasInstallFailed,
    },
  }
}

/**
 * The update as the nav surfaces show it - app-wide, created on first use. `tone` is null
 * while there is nothing to show (no download, nothing ready), and every surface hides
 * its row or dot then. No dismissal: like Windows' orange dot, the row sits there until
 * it is clicked.
 */
export function useUpdateNavState() {
  if (!shared) shared = create()
  return shared
}
