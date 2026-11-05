import { defineStore } from 'pinia'
import { ref } from 'vue'
import { displayLocalPaths } from '@/webview-host/bridge'

/**
 * What to SHOW for a recent file or folder under Torat Emet (Hebrew title, Hebrew path with the root names), asked of Core in
 * one batch (LocalPathDisplay). Display only: nothing here is persisted and the stored history entries are never rewritten.
 * Until the answer arrives (or when the service is down) callers show the stored values.
 */
export const useLocalPathDisplayStore = defineStore('localPathDisplay', () => {
  const shown = ref<Record<string, { title: string; path: string }>>({})
  const asked = new Set<string>()

  const keyOf = (path: string, isFolder: boolean) => `${isFolder ? 'd' : 'f'}|${path.toLowerCase()}`
  /** Only paths with ToratEmet in them can be translated: the rest never cost a round trip. */
  const maybeTranslated = (path: string) => path.toLowerCase().includes('toratemet')

  async function request(items: { path?: string; isFolder: boolean }[]): Promise<void> {
    const fresh = new Map<string, { path: string; isFolder: boolean }>()
    for (const item of items) {
      if (!item.path || !maybeTranslated(item.path)) continue
      const key = keyOf(item.path, item.isFolder)
      if (!asked.has(key)) fresh.set(key, { path: item.path, isFolder: item.isFolder })
    }
    if (!fresh.size) return
    const keys = [...fresh.keys()]
    keys.forEach((k) => asked.add(k))
    try {
      const answers = await displayLocalPaths([...fresh.values()])
      const next = { ...shown.value }
      keys.forEach((k, i) => { next[k] = answers[i]! })
      shown.value = next
    } catch {
      keys.forEach((k) => asked.delete(k))   // service not ready: asked again next time
    }
  }

  function titleOf(path: string | undefined, isFolder: boolean): string {
    return path ? shown.value[keyOf(path, isFolder)]?.title ?? '' : ''
  }
  function pathOf(path: string | undefined, isFolder: boolean): string {
    return path ? shown.value[keyOf(path, isFolder)]?.path ?? '' : ''
  }

  return { request, titleOf, pathOf }
})
