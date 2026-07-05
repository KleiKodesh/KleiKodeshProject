import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { useTabStore } from './tabStore'
import type { TabRoute } from './tabStore'
import { disposeLocalFileHost, restoreLocalFile, restoreHbPdf } from '@/webview-host/bridge'
import { onWebviewEvent } from '@/webview-host/seforimDb'
import { useSettingsStore } from './settingsStore'

export const useLocalFileStore = defineStore('localFile', () => {
  const tabStore = useTabStore()

  const virtualUrl = computed(() => tabStore.activeTab.localFileVirtualUrl ?? null)
  const fileName = computed(() => tabStore.activeTab.localFileName ?? null)
  const converting = computed(() => tabStore.activeTab.localFileConverting ?? false)
  const loadingType = computed(() => tabStore.activeTab.localFileLoadingType ?? 'converting')

  // Shown in the HebrewBooks page when a download fails (book not found on server).
  const downloadErrorMessage = ref<string | null>(null)

  // Set of tabIds currently converting — used to ignore results after cancel/navigate/close
  const _converting = new Set<string>()

  // Listen for C# push events
  onWebviewEvent((msg: any) => {
    if (msg.event === 'localFileConversionStarted') {
      startLocalFileConversion(
        msg.fileName as string,
        msg.filePath as string,
        !!(msg.openInNewTab as boolean),
      )
    }
    if (msg.event === 'localFileTxtReady') {
      // .txt files are opened in /txt-view — no virtual URL, just the file path for restore
      const tabFields = {
        route: '/txt-view' as TabRoute,
        title: msg.fileName as string,
        localFileName: msg.fileName as string,
        localFilePath: msg.filePath as string,
        localFileConverting: false,
      }
      let tabId: string
      if (msg.openInNewTab) {
        tabId = tabStore.openTab(tabFields).id
      } else {
        tabId = tabStore.activeTabId
        tabStore.updateActiveTab(tabFields)
      }
      _converting.delete(tabId)
    }
    if (msg.event === 'localFileReady') {
      // HTML and PDF files — served via virtual host URL
      const path = (msg.filePath as string) ?? ''
      const extension = path.substring(path.lastIndexOf('.')).toLowerCase()
      const isHtmlLike = extension === '.htm' || extension === '.html'
      const route: TabRoute = isHtmlLike ? '/html-view' : '/pdf-view'
      const tabFields = {
        route,
        title: msg.fileName as string,
        localFileName: msg.fileName as string,
        localFilePath: msg.filePath as string,
        localFileVirtualUrl: msg.url as string,
        localFileConverting: false,
      }
      let tabId: string
      if (msg.openInNewTab) {
        tabId = tabStore.openTab(tabFields).id
      } else {
        tabId = tabStore.activeTabId
        tabStore.updateActiveTab(tabFields)
      }
      // Ensure the tab is tracked so finishLocalFileConversion no-ops if called again
      _converting.delete(tabId)
    }
    if (msg.event === 'localFileConversionReady') {
      // Find the tab that started this conversion — it's the one still in _converting
      // with pdfLoadingType 'converting'. Only one Word conversion runs at a time.
      const convertingTabId = Array.from(_converting).find((tid) => {
        const t = tabStore.tabs.find((x) => x.id === tid)
        return t?.localFileLoadingType === 'converting'
      })
      if (convertingTabId) {
        finishLocalFileConversion(convertingTabId, {
          url: msg.url as string,
          fileName: msg.fileName as string,
          filePath: msg.filePath as string,
        })
      }
    }
    if (msg.event === 'localFileError') {
      // Conversion failed while a Word file was being opened via "Open With".
      // If the active tab is still showing the converting placeholder, reset it to home.
      const filePath = msg.filePath as string | undefined
      const convertingTabId = filePath
        ? Array.from(_converting).find((tid) => {
            const tab = tabStore.tabs.find((x) => x.id === tid)
            return tab?.localFilePath === filePath
          })
        : undefined
      if (convertingTabId) {
        finishLocalFileConversion(convertingTabId, null)
      }
      window.alert(msg.message as string)
    }
    if (msg.event === 'hbPdfReady') {
      finishHbDownload(
        msg.tabId as string,
        msg.url as string,
        msg.bookTitle as string,
        msg.bookId as string,
      )
    }
    if (msg.event === 'hbPdfCancelled') {
      cancelHbDownload(msg.tabId as string, !!(msg.notFound as boolean), !!(msg.noInternet as boolean))
    }
  })

  // Watch for tabs being closed or navigated away — cancel any in-progress conversion/download.
  // No deep:true needed — the mapped array of primitives already produces a new reference
  // on any tab id/route change, so Vue detects it without deep traversal.
  watch(
    () => tabStore.tabs.map((t) => ({ id: t.id, route: t.route })),
    (current) => {
      const currentIds = new Set(current.map((t) => t.id))
      for (const tabId of Array.from(_converting)) {
        const wasRemoved = !currentIds.has(tabId)
        const route = current.find((t) => t.id === tabId)?.route
        const navigatedAway = route !== '/pdf-view' && route !== '/html-view' && route !== '/txt-view'
        if (wasRemoved || navigatedAway) _converting.delete(tabId)
      }
    },
  )

  /** Navigate the active tab to /pdf-view immediately, showing the converting placeholder. */
  function startLocalFileConversion(fileName: string, filePath: string, openInNewTab = false) {
    const route: TabRoute = '/pdf-view'
    const tabFields = {
      route,
      title: fileName,
      localFileName: fileName,
      localFilePath: filePath,
      localFileConverting: true,
      localFileLoadingType: 'converting' as const,
      localFileVirtualUrl: undefined,
    }
    let tabId: string
    if (openInNewTab) {
      tabId = tabStore.openTab(tabFields).id
    } else {
      tabId = tabStore.activeTabId
      tabStore.updateActiveTab(tabFields)
    }
    _converting.add(tabId)
  }

  /** Called when conversion finishes — ignored if the tab was cancelled/closed/navigated away. */
  function finishLocalFileConversion(
    tabId: string,
    result: { url: string; fileName: string; filePath: string } | null,
  ) {
    const tab = tabStore.tabs.find((t) => t.id === tabId)
    if (!tab) return

    // Bail if already finished (e.g. conversionReady fired before the RPC reply)
    if (!tab.localFileConverting) return

    // For Word conversions, check the cancellation set
    if (!_converting.has(tabId)) return

    _converting.delete(tabId)
    if (result) {
      tabStore.updateTab(tabId, {
        route: '/pdf-view',
        title: result.fileName,
        localFileVirtualUrl: result.url,
        localFileName: result.fileName,
        localFilePath: result.filePath,
        localFileConverting: false,
      })
    } else {
      // Conversion failed — dispose any virtual host that was registered for this tab
      // before navigating away, so the mapping is not left open.
      if (tab.localFilePath) disposeLocalFileHost(tab.localFilePath)
      tabStore.updateTab(tabId, {
        route: '/',
        title: 'בית',
        localFileVirtualUrl: undefined,
        localFileName: undefined,
        localFilePath: undefined,
        localFileConverting: false,
      })
    }
  }

  /** Cancel an in-progress conversion — resets the tab to home. */
  function cancelConversion(tabId: string) {
    _converting.delete(tabId)
    // Dispose the virtual host before clearing localFilePath so the mapping is released.
    const tab = tabStore.tabs.find((t) => t.id === tabId)
    if (tab?.localFilePath) disposeLocalFileHost(tab.localFilePath)
    tabStore.updateTab(tabId, {
      route: '/',
      title: 'בית',
      localFileVirtualUrl: undefined,
      localFileName: undefined,
      localFilePath: undefined,
      localFileConverting: false,
    })
  }

  /** Navigate a tab to /pdf-view placeholder while a HebrewBooks download is in progress. */
    function startHbDownload(bookTitle: string, tabId: string) {
    _converting.add(tabId)
    tabStore.updateTab(tabId, {
      route: '/pdf-view',
      title: bookTitle,
      localFileName: bookTitle,
      localFileConverting: true,
      localFileLoadingType: 'downloading',
      localFileVirtualUrl: undefined,
    })
  }

  /** Called when hbPdfReady fires — ignored if tab was closed/navigated away. */
  function finishHbDownload(tabId: string, url: string, bookTitle: string, bookId: string) {
    if (!_converting.has(tabId)) return
    _converting.delete(tabId)
    tabStore.updateTab(tabId, {
      route: '/pdf-view',
      title: bookTitle,
      localFileVirtualUrl: url,
      localFileName: bookTitle,
      localFileHbBookId: bookId,
      localFileHbBookTitle: bookTitle,
      localFileConverting: false,
    })
  }

  /** Called on hbPdfCancelled — closes the tab. Shows an error if the book was not found. */
  function cancelHbDownload(tabId: string, notFound = false, noInternet = false) {
    _converting.delete(tabId)
    if (noInternet || notFound) {
      // Navigate back to the HebrewBooks page so the user sees the error banner
      // rather than losing the tab entirely.
      tabStore.updateTab(tabId, {
        route: '/hebrewbooks',
        title: 'היברו-בוקס',
        localFileConverting: false,
        localFileVirtualUrl: undefined,
        localFileName: undefined,
      })
      if (noInternet) downloadErrorMessage.value = 'אין חיבור לאינטרנט'
      else downloadErrorMessage.value = 'הספר המבוקש אינו זמין להורדה'
    } else {
      tabStore.closeTab(tabId)
    }
  }

  /** Open a HebrewBooks PDF directly (used by session restore). */
  function openHbBook(url: string, bookTitle: string, bookId: string) {
    tabStore.updateActiveTab({
      route: '/pdf-view',
      title: bookTitle,
      localFileVirtualUrl: url,
      localFileName: bookTitle,
      localFileHbBookId: bookId,
      localFileHbBookTitle: bookTitle,
    })
  }

  /** Called on app init for every restored /pdf-view or /html-view tab. */
  async function restoreTab(tabId: string) {
    const tab = tabStore.tabs.find((t) => t.id === tabId)
    if (!tab || (tab.route !== '/pdf-view' && tab.route !== '/html-view')) return

    if (tab.localFileHbBookId) {
      const localFolder = useSettingsStore().hebrewBooksLocalFolder || undefined
      tabStore.updateTab(tabId, { localFileConverting: true, localFileLoadingType: 'downloading' })
      _converting.add(tabId)
      const res = await restoreHbPdf(tab.localFileHbBookId, tab.localFileHbBookTitle ?? '', tabId, localFolder)
      if (!res) {
        _converting.delete(tabId)
        tabStore.closeTab(tabId)
      } else if ('url' in res) {
        _converting.delete(tabId)
        tabStore.updateTab(tabId, {
          localFileVirtualUrl: res.url,
          localFileConverting: false,
          localFileLoadingType: undefined,
        })
      }
      // redownload: true — stays in _converting, hbPdfReady push event will finish it
    } else if (tab.localFilePath) {
      const ext = tab.localFilePath.substring(tab.localFilePath.lastIndexOf('.')).toLowerCase()
      if (ext === '.txt') {
        // .txt tabs restore directly — TxtViewPage reads the file itself via bridge
        tabStore.updateTab(tabId, { route: '/txt-view' })
      } else {
        const res = await restoreLocalFile(tab.localFilePath)
        if (res) {
          const isHtmlLike = ext === '.htm' || ext === '.html'
          const route = isHtmlLike ? '/html-view' : '/pdf-view'
          tabStore.updateTab(tabId, { localFileVirtualUrl: res.url, route })
        }
      }
    }
  }

  return {
    virtualUrl,
    fileName,
    converting,
    loadingType,
    downloadErrorMessage,
    startLocalFileConversion,
    finishLocalFileConversion,
    cancelConversion,
    startHbDownload,
    finishHbDownload,
    cancelHbDownload,
    openHbBook,
    restoreTab,
  }
})
