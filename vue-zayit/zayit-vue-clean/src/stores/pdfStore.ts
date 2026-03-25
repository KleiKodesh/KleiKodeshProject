import { defineStore } from 'pinia'
import { computed, watch } from 'vue'
import { useTabStore } from './tabStore'
import { restoreLocalPdf, restoreHbPdf } from '@/host/bridge'
import { onWebviewEvent } from '@/host/db'

export const usePdfStore = defineStore('pdf', () => {
  const tabStore = useTabStore()

  const virtualUrl    = computed(() => tabStore.activeTab.pdfVirtualUrl ?? null)
  const fileName      = computed(() => tabStore.activeTab.pdfFileName ?? null)
  const converting    = computed(() => tabStore.activeTab.pdfConverting ?? false)
  const loadingType   = computed(() => tabStore.activeTab.pdfLoadingType ?? 'converting')

  // Set of tabIds currently converting — used to ignore results after cancel/navigate/close
  const _converting = new Set<string>()

  // Listen for C# push events
  onWebviewEvent((msg) => {
    if (msg.event === 'conversionStarted') {
      startLocalFileConversion(msg.fileName as string, msg.filePath as string)
    }
    if (msg.event === 'hbPdfReady') {
      finishHbDownload(msg.tabId as string, msg.url as string, msg.bookTitle as string, msg.bookId as string)
    }
    if (msg.event === 'hbPdfCancelled') {
      cancelHbDownload(msg.tabId as string)
    }
  })

  // Watch for tabs being closed or navigated away — cancel any in-progress conversion/download
  watch(() => tabStore.tabs.map(t => ({ id: t.id, route: t.route })), (current, prev) => {
    if (!prev) return
    const currentIds = new Set(current.map(t => t.id))
    for (const tabId of Array.from(_converting)) {
      const wasRemoved = !currentIds.has(tabId)
      const navigatedAway = current.find(t => t.id === tabId)?.route !== '/pdf-view'
      if (wasRemoved || navigatedAway) _converting.delete(tabId)
    }
  }, { deep: true })

  /** Navigate the active tab to /pdf-view immediately, showing the converting placeholder. */
  function startLocalFileConversion(fileName: string, filePath: string) {
    const tabId = tabStore.activeTabId
    _converting.add(tabId)
    tabStore.updateActiveTab({ route: '/pdf-view', title: fileName, pdfFileName: fileName, pdfFilePath: filePath, pdfConverting: true, pdfLoadingType: 'converting', pdfVirtualUrl: undefined })
  }

  /** Called when conversion finishes — ignored if the tab was cancelled/closed/navigated away. */
  function finishLocalFileConversion(tabId: string, result: { url: string; fileName: string; filePath: string } | null) {
    const tab = tabStore.tabs.find(t => t.id === tabId)
    if (!tab) return

    // For Word conversions, check the cancellation set
    if (tab.pdfConverting && !_converting.has(tabId)) return

    _converting.delete(tabId)

    if (result) {
      Object.assign(tab, { route: '/pdf-view', title: result.fileName, pdfVirtualUrl: result.url, pdfFileName: result.fileName, pdfFilePath: result.filePath, pdfConverting: false })
    } else {
      Object.assign(tab, { route: '/', title: 'בית', pdfVirtualUrl: undefined, pdfFileName: undefined, pdfFilePath: undefined, pdfConverting: false })
    }
  }

  /** Cancel an in-progress conversion — resets the tab to home. */
  function cancelConversion(tabId: string) {
    _converting.delete(tabId)
    const tab = tabStore.tabs.find(t => t.id === tabId)
    if (!tab) return
    Object.assign(tab, { route: '/', title: 'בית', pdfVirtualUrl: undefined, pdfFileName: undefined, pdfFilePath: undefined, pdfConverting: false })
  }

  /** Navigate a tab to /pdf-view placeholder while a HebrewBooks download is in progress. */
  function startHbDownload(bookTitle: string, tabId: string) {
    _converting.add(tabId)
    const tab = tabStore.tabs.find(t => t.id === tabId)
    if (tab) Object.assign(tab, { route: '/pdf-view', title: bookTitle, pdfFileName: bookTitle, pdfConverting: true, pdfLoadingType: 'downloading', pdfVirtualUrl: undefined })
  }

  /** Called when hbPdfReady fires — ignored if tab was closed/navigated away. */
  function finishHbDownload(tabId: string, url: string, bookTitle: string, bookId: string) {
    if (!_converting.has(tabId)) return
    _converting.delete(tabId)
    const tab = tabStore.tabs.find(t => t.id === tabId)
    if (!tab) return
    Object.assign(tab, { route: '/pdf-view', title: bookTitle, pdfVirtualUrl: url, pdfFileName: bookTitle, pdfHbBookId: bookId, pdfHbBookTitle: bookTitle, pdfConverting: false })
  }

  /** Called on hbPdfError — closes the tab. */
  function cancelHbDownload(tabId: string) {
    _converting.delete(tabId)
    _closeTab(tabId)
  }

  function _closeTab(tabId: string) {
    tabStore.closeTab(tabId)
  }

  /** Open a HebrewBooks PDF directly (used by session restore). */
  function openHbBook(url: string, bookTitle: string, bookId: string) {
    tabStore.updateActiveTab({ route: '/pdf-view', title: bookTitle, pdfVirtualUrl: url, pdfFileName: bookTitle, pdfHbBookId: bookId, pdfHbBookTitle: bookTitle })
  }

  /** Called on app init for every restored /pdf-view tab. */
  async function restoreTab(tabId: string) {
    const tab = tabStore.tabs.find(t => t.id === tabId)
    if (!tab || tab.route !== '/pdf-view') return

    if (tab.pdfHbBookId) {
      Object.assign(tab, { pdfConverting: true, pdfLoadingType: 'downloading' })
      _converting.add(tabId)
      const res = await restoreHbPdf(tab.pdfHbBookId, tab.pdfHbBookTitle ?? '', tabId)
      if (!res) {
        _converting.delete(tabId)
        tabStore.closeTab(tabId)
      } else if ('url' in res) {
        _converting.delete(tabId)
        Object.assign(tab, { pdfVirtualUrl: res.url, pdfConverting: false, pdfLoadingType: undefined })
      }
      // redownload: true — stays in _converting, hbPdfReady push event will finish it
    } else if (tab.pdfFilePath) {
      const res = await restoreLocalPdf(tab.pdfFilePath)
      if (res) Object.assign(tab, { pdfVirtualUrl: res.url })
    }
  }

  return { virtualUrl, fileName, converting, loadingType, startLocalFileConversion, finishLocalFileConversion, cancelConversion, startHbDownload, finishHbDownload, cancelHbDownload, openHbBook, restoreTab }})
