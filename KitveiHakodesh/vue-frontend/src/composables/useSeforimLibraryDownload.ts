import { computed, readonly, ref } from 'vue'
import { serviceCall, serviceStream } from '@/webview-host/serviceClient'
import { onDbPathChanged } from '@/webview-host/seforimDb'
import { t } from '@/i18n'

/** Core's SeforimLibraryDownloadPhase, by value. */
export enum SeforimLibraryDownloadPhase {
  Idle = 0,
  FindingRelease = 1,
  Downloading = 2,
  Verifying = 3,
  Extracting = 4,
  Patching = 5,
  Completed = 6,
  Failed = 7,
}

/** Core's SeforimLibraryDownloadFailure, by value. */
enum SeforimLibraryDownloadFailure {
  None = 0,
  Network = 1,
  NoCompatibleRelease = 2,
  NotEnoughDiskSpace = 3,
  ChecksumMismatch = 4,
  CorruptArchive = 5,
  DiskError = 6,
  Cancelled = 7,
  Unexpected = 8,
}

/** One frame of `seforimLibraryDownloadStream` (Core SeforimLibraryDownloadProgress, camelCased). */
interface SeforimLibraryDownloadFrame {
  phase: SeforimLibraryDownloadPhase
  received: number
  total: number
  releaseTag: string
  failure: SeforimLibraryDownloadFailure
  detail?: string | null
  pending: boolean
  running: boolean
}

const frame = ref<SeforimLibraryDownloadFrame | null>(null)
let isWatching = false

/**
 * The service's library download, PUSHED over one open stream for the session. When the
 * database lands the service restarts to open it, and the page reloads once it is back — the
 * same path as picking a database by hand. ONE app-wide subscription; idempotent.
 */
function watchSeforimLibraryDownload(): void {
  if (isWatching) return
  isWatching = true
  const consume = async () => {
    for (;;) {
      try {
        for await (const f of serviceStream<SeforimLibraryDownloadFrame>('seforimLibraryDownloadStream', {})) {
          frame.value = f
          if (f.phase === SeforimLibraryDownloadPhase.Completed) {
            await onDbPathChanged('', 'downloaded')
            return
          }
        }
      } catch {
        /* service restarting - fall through to the reconnect delay */
      }
      await new Promise((r) => setTimeout(r, 5000))
    }
  }
  void consume()
}

const failureText: Record<SeforimLibraryDownloadFailure, string> = {
  [SeforimLibraryDownloadFailure.None]: '',
  [SeforimLibraryDownloadFailure.Network]: 'composables.libraryDownload.failedNetwork',
  [SeforimLibraryDownloadFailure.NoCompatibleRelease]: 'composables.libraryDownload.failedNoRelease',
  [SeforimLibraryDownloadFailure.NotEnoughDiskSpace]: 'composables.libraryDownload.failedDiskSpace',
  [SeforimLibraryDownloadFailure.ChecksumMismatch]: 'composables.libraryDownload.failedChecksum',
  [SeforimLibraryDownloadFailure.CorruptArchive]: 'composables.libraryDownload.failedCorrupt',
  [SeforimLibraryDownloadFailure.DiskError]: 'composables.libraryDownload.failedDisk',
  [SeforimLibraryDownloadFailure.Cancelled]: 'composables.libraryDownload.cancelled',
  [SeforimLibraryDownloadFailure.Unexpected]: 'composables.libraryDownload.failedUnexpected',
}

const phaseText: Partial<Record<SeforimLibraryDownloadPhase, string>> = {
  [SeforimLibraryDownloadPhase.FindingRelease]: 'composables.libraryDownload.findingRelease',
  [SeforimLibraryDownloadPhase.Downloading]: 'composables.libraryDownload.downloading',
  [SeforimLibraryDownloadPhase.Verifying]: 'composables.libraryDownload.verifying',
  [SeforimLibraryDownloadPhase.Extracting]: 'composables.libraryDownload.extracting',
  [SeforimLibraryDownloadPhase.Patching]: 'composables.libraryDownload.patching',
  [SeforimLibraryDownloadPhase.Completed]: 'composables.libraryDownload.completed',
}

const gigabytes = (bytes: number) => (bytes / 1024 ** 3).toFixed(2)

/**
 * The library download: start / cancel, and one line of status for wherever the download
 * button lives (home card, setup wizard). Empty status = nothing to report yet.
 */
export function useSeforimLibraryDownload() {
  watchSeforimLibraryDownload()

  const isRunning = computed(() => frame.value?.running ?? false)

  const statusText = computed(() => {
    const f = frame.value
    if (!f) return ''
    if (f.phase === SeforimLibraryDownloadPhase.Failed) return t(failureText[f.failure] || 'composables.libraryDownload.failedDisk')
    const key = phaseText[f.phase]
    if (!key) return ''
    const percent = f.total > 0 ? Math.floor((f.received * 100) / f.total) : 0
    return t(key, { percent, received: gigabytes(f.received), total: gigabytes(f.total) })
  })

  async function start(): Promise<void> {
    frame.value = await serviceCall<SeforimLibraryDownloadFrame>('startSeforimLibraryDownload', {})
  }

  async function cancel(): Promise<void> {
    await serviceCall('cancelSeforimLibraryDownload', {})
  }

  return { frame: readonly(frame), isRunning, statusText, start, cancel }
}
