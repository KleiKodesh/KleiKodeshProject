import { shallowRef } from 'vue'
import { serviceCall } from '@/webview-host/serviceClient'

/**
 * The Otzaria addins installed on this computer, for the Websites & add-ins page, and each
 * addin's own icon (the "icon" of its manifest.json, read by Core's `otzariaAddinIcon` op).
 * The list comes from Otzaria's plugins folder (Core `otzariaInstalledAddins`), so it does not
 * wait for the file index.
 */
export interface OtzariaAddin {
  /** The addin's name from its manifest. */
  name: string
  fileName: string
  /** The entry-point file's full path. */
  path: string
  /** The file index's form of the name ("תוסף אוצריא: {name}") — what the file-open flow keys on. */
  addinName: string
}

const addins = shallowRef<OtzariaAddin[]>([])
let loading: Promise<OtzariaAddin[]> | null = null

export const otzariaAddinsRef = addins

/** Loaded once per session; reactive through `otzariaAddinsRef`. */
export function loadOtzariaAddins(): Promise<OtzariaAddin[]> {
  loading ??= serviceCall<{ name: string; entryPath: string }[] | undefined>('otzariaInstalledAddins')
    .then((reply) =>
      (addins.value = (reply ?? []).map((a) => ({
        name: a.name,
        fileName: a.entryPath.substring(a.entryPath.lastIndexOf('\\') + 1),
        path: a.entryPath,
        addinName: `תוסף אוצריא: ${a.name}`,
      }))),
    )
    .catch(() => {
      loading = null // try again next time the page opens
      return addins.value
    })
  return loading
}

interface AddinIconReply {
  contentType: string
  bytes: Uint8Array
}

const iconSources = new Map<string, Promise<string | null>>()

/** The <img> source for the addin whose entry point is `path`; null when it has no icon (the caller shows the puzzle piece). */
export function otzariaAddinIconSource(path: string): Promise<string | null> {
  const key = path.toLowerCase()
  let source = iconSources.get(key)
  if (!source) {
    source = serviceCall<AddinIconReply | undefined>('otzariaAddinIcon', { path })
      .then((reply) => (reply?.bytes?.length ? URL.createObjectURL(new Blob([reply.bytes as BlobPart], { type: reply.contentType })) : null))
      .catch(() => {
        iconSources.delete(key)
        return null
      })
    iconSources.set(key, source)
  }
  return source
}
