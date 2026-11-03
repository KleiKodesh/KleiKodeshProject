import { ref } from 'vue'
import { serviceCall } from './serviceClient'

interface OwnLibraryInfo {
  path: string
  exists: boolean
  sizeBytes: number
}

/** The open question to the reader, or null. App.vue shows it; `answer` settles the promise. */
export const ownLibraryOffer = ref<{ sizeBytes: number; answer: (freeIt: boolean) => void } | null>(null)

const normalize = (path: string) => path.replace(/\//g, '\\').toLowerCase()

/**
 * The reader is leaving a library for another one. When the one being left is the copy this
 * app downloaded itself, ask whether to delete it to free the disk space.
 *
 * True = delete it (the caller does that once the service has let go of the file).
 * Anything unexpected answers false: a library is never deleted on a guess.
 */
export async function askToFreeOwnLibrary(previousPath: string, newPath: string): Promise<boolean> {
  try {
    const own = await serviceCall<OwnLibraryInfo>('ownSeforimLibraryInfo', {})
    if (!own?.exists) return false
    if (normalize(previousPath) !== normalize(own.path)) return false
    if (normalize(newPath) === normalize(own.path)) return false
    return await new Promise<boolean>((resolve) => {
      ownLibraryOffer.value = {
        sizeBytes: own.sizeBytes,
        answer: (freeIt) => {
          ownLibraryOffer.value = null
          resolve(freeIt)
        },
      }
    })
  } catch {
    return false
  }
}

/** Delete our downloaded copy. Call only after the service restarted onto the other library. */
export async function freeOwnLibrary(): Promise<void> {
  try {
    await serviceCall('deleteOwnSeforimLibrary', {})
  } catch {
    /* the space stays used; nothing the reader needs depends on it */
  }
}
