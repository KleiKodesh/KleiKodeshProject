import { ref, watch, nextTick, onMounted, onUnmounted } from 'vue'

export function usePdfFind(getIframe: () => HTMLIFrameElement | null) {
  // ── State ──────────────────────────────────────────────────────────────────

  const findOpen = ref(false)
  const findQuery = ref('')
  const findMatchCount = ref<number | null>(null)
  const findMatchIndex = ref<number | null>(null)
  const findNotFound = ref(false)
  const findOptionsOpen = ref(false)
  const findInputRef = ref<HTMLInputElement | null>(null)

  // Options
  const findHighlightAll = ref(true)
  const findMatchCase = ref(false)
  const findMatchDiacritics = ref(true)
  const findWholeWord = ref(false)

  // ── PDF.js event bus ───────────────────────────────────────────────────────

  function getEventBus(): any {
    return (getIframe()?.contentWindow as any)?.PDFViewerApplication?.eventBus ?? null
  }

  let attachInterval: ReturnType<typeof setInterval> | null = null

  function attachListeners() {
    const bus = getEventBus()
    if (!bus) return
    bus.on('updatefindmatchescount', (data: any) => {
      findMatchCount.value = data.matchesCount?.total ?? null
      findMatchIndex.value = data.matchesCount?.current ?? null
      findNotFound.value = false
    })
    bus.on('updatefindcontrolstate', (data: any) => {
      if (data.state === 1) {
        findNotFound.value = true
        findMatchCount.value = null
        findMatchIndex.value = null
      } else {
        findNotFound.value = false
      }
    })
  }

  onMounted(() => {
    attachInterval = setInterval(() => {
      if (getEventBus()) {
        attachListeners()
        clearInterval(attachInterval!)
      }
    }, 300)
  })

  onUnmounted(() => { if (attachInterval) clearInterval(attachInterval) })

  // ── Dispatch ───────────────────────────────────────────────────────────────

  function dispatchFind(type: string, findPrevious = false) {
    const bus = getEventBus()
    if (!bus) return
    bus.dispatch('find', {
      source: window,
      type,
      query: findQuery.value,
      caseSensitive: findMatchCase.value,
      entireWord: findWholeWord.value,
      highlightAll: findHighlightAll.value,
      findPrevious,
      matchDiacritics: findMatchDiacritics.value,
    })
  }

  // ── Open / close ───────────────────────────────────────────────────────────

  function openFind() {
    findOpen.value = true
    findNotFound.value = false
    findMatchCount.value = null
    findMatchIndex.value = null
    nextTick(() => findInputRef.value?.focus())
  }

  function closeFind() {
    findOpen.value = false
    findOptionsOpen.value = false
    findQuery.value = ''
    findNotFound.value = false
    findMatchCount.value = null
    findMatchIndex.value = null
    getEventBus()?.dispatch('find', {
      source: window, type: '', query: '',
      caseSensitive: false, entireWord: false,
      highlightAll: false, findPrevious: false, matchDiacritics: false,
    })
  }

  function toggleFind() {
    findOpen.value ? closeFind() : openFind()
  }

  // ── Navigation ─────────────────────────────────────────────────────────────

  function findNext() { dispatchFind('again', false) }
  function findPrevious() { dispatchFind('again', true) }

  function onFindKeydown(event: KeyboardEvent) {
    if (event.key === 'Enter') event.shiftKey ? findPrevious() : findNext()
    else if (event.key === 'Escape') closeFind()
  }

  // ── Watchers ───────────────────────────────────────────────────────────────

  watch(findQuery, () => {
    if (!findOpen.value) return
    if (!findQuery.value) {
      findMatchCount.value = null
      findMatchIndex.value = null
      findNotFound.value = false
    }
    dispatchFind('')
  })

  watch([findHighlightAll, findMatchCase, findMatchDiacritics, findWholeWord], () => {
    if (findOpen.value && findQuery.value) dispatchFind('')
  })

  return {
    findOpen, findQuery, findMatchCount, findMatchIndex, findNotFound,
    findOptionsOpen, findInputRef,
    findHighlightAll, findMatchCase, findMatchDiacritics, findWholeWord,
    openFind, closeFind, toggleFind, findNext, findPrevious, onFindKeydown,
  }
}
