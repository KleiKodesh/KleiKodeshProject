import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { RegexFindOptions, RegexFindState, SearchResult, SearchMode } from '@/types/regex-find'

export const useRegexFindStore = defineStore('regexFind', () => {
  const state = ref<RegexFindState>({
    options: {
      text: '',
      bold: false,
      italic: false,
      underline: false,
      superscript: false,
      subscript: false,
      style: '',
      font: '',
      fontSize: undefined,
      mode: 'All' as SearchMode,
      slop: 0,
      useWildcards: false,
      replace: {
        text: '',
        bold: false,
        italic: false,
        underline: false,
        superscript: false,
        subscript: false,
        style: '',
        font: '',
        fontSize: undefined
      }
    },
    results: [],
    currentIndex: -1,
    isSearching: false
  })

  const hasResults = computed(() => state.value.results.length > 0)
  const currentResult = computed(() => 
    state.value.currentIndex >= 0 && state.value.currentIndex < state.value.results.length
      ? state.value.results[state.value.currentIndex]
      : null
  )

  function updateOptions(options: Partial<RegexFindOptions>) {
    state.value.options = { ...state.value.options, ...options }
  }

  function setResults(results: SearchResult[]) {
    state.value.results = results
    state.value.currentIndex = results.length > 0 ? 0 : -1
  }

  function selectNext() {
    if (hasResults.value) {
      state.value.currentIndex = (state.value.currentIndex + 1) % state.value.results.length
    }
  }

  function selectPrevious() {
    if (hasResults.value) {
      state.value.currentIndex = state.value.currentIndex <= 0 
        ? state.value.results.length - 1 
        : state.value.currentIndex - 1
    }
  }

  function selectResult(index: number) {
    if (index >= 0 && index < state.value.results.length) {
      state.value.currentIndex = index
    }
  }

  function setSearching(searching: boolean) {
    state.value.isSearching = searching
  }

  return {
    state,
    hasResults,
    currentResult,
    updateOptions,
    setResults,
    selectNext,
    selectPrevious,
    selectResult,
    setSearching
  }
})