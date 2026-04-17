import { ref, watch, type Ref } from 'vue'
import { isHosted } from '@/host/seforimDb'

/**
 * Fetches synonyms from the Word thesaurus (only available in VSTO environment).
 * Returns groups of synonyms, where each group represents one meaning.
 * Returns empty array when not running inside Word or when no synonyms are found.
 */
export function useWordThesaurus(word: Ref<string>) {
  const groups = ref<string[][]>([])
  const loading = ref(false)

  watch(
    word,
    async (w) => {
      groups.value = []
      if (!w || !isHosted || typeof window.__webviewAction !== 'function') return

      loading.value = true
      try {
        const res = await window.__webviewAction('getWordSynonyms', { word: w })
        if (res && Array.isArray((res as any).groups)) {
          groups.value = (res as any).groups
        }
      } catch {
        // Thesaurus not available — degrade silently
      } finally {
        loading.value = false
      }
    },
    { immediate: true },
  )

  return { groups, loading }
}
