import { ref, readonly } from 'vue'
import { serviceStream } from '@/webview-host/serviceClient'

/** One frame of the `updateProgressStream` op (service UpdateProgressFrame, camelCased). */
interface UpdateProgressFrame {
  downloadingVersion: string
  received: number
  total: number
  readyVersion: string
}

export interface UpdateDownloadProgress {
  /** The release the service is downloading now; '' when none. */
  downloadingVersion: string
  /** 0-100; 0 while the size is still unknown. */
  percent: number
  /** The downloaded update ready to install; '' when none. */
  readyVersion: string
}

const progress = ref<UpdateDownloadProgress>({ downloadingVersion: '', percent: 0, readyVersion: '' })
let isWatching = false

/**
 * The service's self-update download, PUSHED over one open stream (`updateProgressStream`) for
 * the whole session: a frame when a download starts, as it progresses, and the moment it lands
 * (readyVersion set) - so the update prompt appears at once, with no polling. ONE app-wide
 * subscription; idempotent. If the stream drops (service restart), it is reopened.
 */
export function watchUpdateDownloadProgress(): void {
  if (isWatching) return
  isWatching = true
  const consume = async () => {
    for (;;) {
      try {
        for await (const f of serviceStream<UpdateProgressFrame>('updateProgressStream', {})) {
          const total = f.total ?? 0
          progress.value = {
            downloadingVersion: f.downloadingVersion ?? '',
            percent: total > 0 ? Math.min(100, Math.floor(((f.received ?? 0) * 100) / total)) : 0,
            readyVersion: f.readyVersion ?? '',
          }
        }
      } catch {
        /* service restarting - fall through to the reconnect delay */
      }
      // A download cut off by the restart is not running any more.
      progress.value = { ...progress.value, downloadingVersion: '', percent: 0 }
      await new Promise((r) => setTimeout(r, 5000))
    }
  }
  void consume()
}

export function useUpdateDownloadProgress() {
  watchUpdateDownloadProgress()
  return { progress: readonly(progress) }
}
