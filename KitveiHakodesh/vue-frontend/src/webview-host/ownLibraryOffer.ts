import { ref } from 'vue'
import { serviceCall } from './serviceClient'

interface OwnLibraryInfo {
  path: string
  exists: boolean
  sizeBytes: number
  inUse: boolean
  declined: boolean
}

/** The open question to the reader, or null. App.vue shows it; `answer` settles the promise. */
export const ownLibraryOffer = ref<{ sizeBytes: number; answer: (freeIt: boolean) => void } | null>(null)

const normalize = (path: string) => path.replace(/\//g, '\\').toLowerCase()

/** Show the question. "Keep" is remembered, so the next launch does not ask again. */
function ask(sizeBytes: number): Promise<boolean> {
  return new Promise<boolean>((resolve) => {
    ownLibraryOffer.value = {
      sizeBytes,
      answer: (freeIt) => {
        ownLibraryOffer.value = null
        if (!freeIt) void serviceCall('declineOwnSeforimLibraryDeletion', {}).catch(() => {})
        resolve(freeIt)
      },
    }
  })
}

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
    return await ask(own.sizeBytes)
  } catch {
    return false
  }
}

/**
 * At launch: the app moved to another library without being told to — Otzaria or Zayit was
 * installed after our copy was downloaded, and it is found first. Same question, once. The
 * service is already on the other library, so the delete can go ahead at once.
 */
export async function offerToFreeOwnLibraryAtStart(): Promise<void> {
  try {
    const own = await serviceCall<OwnLibraryInfo>('ownSeforimLibraryInfo', {})
    if (!own?.exists || own.inUse || own.declined) return
    if (await ask(own.sizeBytes)) await freeOwnLibrary()
  } catch {
    /* no question, no delete */
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
