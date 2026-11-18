import { shallowRef } from 'vue'
import { fileSystemSearch } from '@/webview-host/bridge'
import { serviceCall } from '@/webview-host/serviceClient'
import { addinDisplayTitle, normalizeAddinQuery } from '@/features/local-file-search/otzariaAddins'

/**
 * The Otzaria addins installed on this computer, for the Websites & add-ins page, and each
 * addin's own icon (the "icon" of its manifest.json, read by Core's `otzariaAddinIcon` op).
 * The list comes from the file index, the same place the file search finds addins.
 */
export interface OtzariaAddin {
  /** The addin's name from its manifest. */
  name: string
  fileName: string
  /** The entry-point file's full path. */
  path: string
  /** The baked index value ("תוסף אוצריא: {name}") — what the file-open flow keys on. */
  addinName: string
}

const addins = shallowRef<OtzariaAddin[]>([])
let loading: Promise<OtzariaAddin[]> | null = null

export const otzariaAddinsRef = addins

/** Loaded once per session; reactive through `otzariaAddinsRef`. */
export function loadOtzariaAddins(): Promise<OtzariaAddin[]> {
  loading ??= fileSystemSearch(normalizeAddinQuery('תוספים'), -1).then((reply) => {
    const seen = new Set<string>()
    const list: OtzariaAddin[] = []
    for (const hit of reply.results ?? []) {
      const path = hit.path ? `${hit.path}\${hit.fileName}` : hit.fileName
      if (!hit.addinName || seen.has(path.toLowerCase())) continue
      seen.add(path.toLowerCase())
      list.push({ name: addinDisplayTitle(hit.addinName), fileName: hit.fileName, path, addinName: hit.addinName })
    }
    if (reply.error) loading = null // try again next time the page opens
    return (addins.value = list)
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
