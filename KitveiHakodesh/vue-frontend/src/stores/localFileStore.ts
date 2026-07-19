import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { useTabStore } from './tabStore'
import type { Tab, TabRoute } from './tabStore'
import { useBookViewStore } from './bookViewStore'
import {
  disposeLocalFileHost,
  restoreLocalFile,
  restoreHbPdf,
  pendingPickOpenInNewTab,
} from '@/webview-host/bridge'
import { onWebviewEvent } from '@/webview-host/seforimDb'
import { useSettingsStore } from './settingsStore'

function stripFileExtension(fileName: string): string {
  const lastDot = fileName.lastIndexOf('.')
  return lastDot > 0 ? fileName.substring(0, lastDot) : fileName
}

export const useLocalFileStore = defineStore('localFile', () => {
  const tabStore = useTabStore()

  // ── Pane-aware navigation for C# push events ──────────────────────────────
  // File-open push events must land in the pane that initiated the pick, not
  // always pane 1. When split view is off there is only pane 1; when it's on we
  // target the focused pane (the one the user opened the file from).
  function targetPaneId(): 1 | 2 {
    const bookViewStore = useBookViewStore()
    return bookViewStore.splitViewEnabled ? bookViewStore.focusedPaneId : 1
  }
  /** Open a new tab in the target pane; returns its id. */
  function openInTargetPane(fields: Omit<Tab, 'id' | 'pane'>): string {
    return targetPaneId() === 2
      ? tabStore.openPane2Tab(fields).id
      : tabStore.openTab(fields).id
  }
  /** Navigate the target pane's active tab in place; returns its id. */
  function updateActiveInTargetPane(fields: Partial<Omit<Tab, 'id'>>): string {
    if (targetPaneId() === 2) {
      tabStore.updatePane2ActiveTab(fields)
      return tabStore.pane2ActiveTabId
    }
    tabStore.updateActiveTab(fields)
    return tabStore.activeTabId
  }
  /**
   * Resolve the new-tab intent for a push event: the in-flight user pick's intent
   * (frontend-owned, since the C# events don't carry it), falling back to the
   * event's own flag for "Open With" launches.
   */
  function resolveOpenInNewTab(eventFlag: unknown): boolean {
    return pendingPickOpenInNewTab() ?? !!eventFlag
  }

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
        resolveOpenInNewTab(msg.openInNewTab),
      )
    }
    if (msg.event === 'localFileTxtReady') {
      // .txt files are opened in /txt-view — no virtual URL, just the file path for restore
      const tabFields = {
        route: '/txt-view' as TabRoute,
        title: stripFileExtension(msg.fileName as string),
        localFileName: msg.fileName as string,
        localFilePath: msg.filePath as string,
        localFileConverting: false,
      }
      const tabId = resolveOpenInNewTab(msg.openInNewTab)
        ? openInTargetPane(tabFields)
        : updateActiveInTargetPane(tabFields)
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
        title: stripFileExtension(msg.fileName as string),
        localFileName: msg.fileName as string,
        localFilePath: msg.filePath as string,
        localFileVirtualUrl: msg.url as string,
        localFileConverting: false,
      }
      const tabId = resolveOpenInNewTab(msg.openInNewTab)
        ? openInTargetPane(tabFields)
        : updateActiveInTargetPane(tabFields)
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
      title: stripFileExtension(fileName),
      localFileName: fileName,
      localFilePath: filePath,
      localFileConverting: true,
      localFileLoadingType: 'converting' as const,
      localFileVirtualUrl: undefined,
    }
    const tabId = openInNewTab
      ? openInTargetPane(tabFields)
      : updateActiveInTargetPane(tabFields)
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
        title: stripFileExtension(result.fileName),
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

  /**
   * Finalize a Word conversion that was started by a user-initiated pick, using the
   * pickFile RPC reply. Cached conversions produce no localFileConversionReady push
   * (no FileSystemWatcher is set up when the cache file already exists), so without this
   * the placeholder tab would stay converting forever. Idempotent: a no-op when the
   * conversionReady push already finalized the tab, or when the pick was a plain
   * PDF/HTML/TXT file (no tab is left in the converting state).
   */
  function finalizeConvertingFromReply(result: { url: string; fileName: string; filePath: string }) {
    const convertingTabId = Array.from(_converting).find((tid) => {
      const t = tabStore.tabs.find((x) => x.id === tid)
      return t?.localFileLoadingType === 'converting'
    })
    if (convertingTabId) finishLocalFileConversion(convertingTabId, result)
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

  /**
   * Opens a document from the recently opened history.
   * Re-registers the virtual host for PDF/HTML files (the previous session's URL is gone),
   * re-downloads HebrewBooks PDFs if needed, and navigates the FOCUSED pane's active tab
   * to the result — recents picked from pane 2 (address bar, home tiles) must not land
   * in pane 1. Follow-up patches after awaits go by tabId, not "whatever is active now".
   */
  async function openFromHistory(
    entry: import('@/stores/recentlyOpenedStore').RecentlyOpenedEntry,
    openInNewTab = false,
  ): Promise<void> {
    const { route, title, localFilePath, localFileHbBookId, localFileHbBookTitle } = entry

    // For a Ctrl/⌘-click, open a fresh tab in the target pane; otherwise navigate
    // the active tab in place. Both return the id so post-await patches target it.
    const placeTab = (fields: Omit<Tab, 'id' | 'pane'>): string =>
      openInNewTab ? openInTargetPane(fields) : updateActiveInTargetPane(fields)

    if (localFileHbBookId) {
      const localFolder = useSettingsStore().hebrewBooksLocalFolder || undefined
      const tabId = placeTab({
        route: '/pdf-view',
        title,
        localFileName: title,
        localFileHbBookId,
        localFileHbBookTitle,
        localFileConverting: true,
        localFileLoadingType: 'downloading',
        localFileVirtualUrl: undefined,
      })
      _converting.add(tabId)
      const res = await restoreHbPdf(localFileHbBookId, localFileHbBookTitle ?? '', tabId, localFolder)
      if (!res) {
        _converting.delete(tabId)
        tabStore.updateTab(tabId, { route: '/', title: 'בית', localFileConverting: false })
      } else if ('url' in res) {
        _converting.delete(tabId)
        tabStore.updateTab(tabId, { localFileVirtualUrl: res.url, localFileConverting: false, localFileLoadingType: undefined })
      }
      // redownload: true — hbPdfReady push event will finish it
      return
    }

    if (localFilePath) {
      const ext = localFilePath.substring(localFilePath.lastIndexOf('.')).toLowerCase()
      if (ext === '.txt') {
        placeTab({ route: '/txt-view', title, localFileName: entry.localFileName ?? title, localFilePath })
        return
      }
      const isHtmlLike = ext === '.htm' || ext === '.html'
      const fileRoute: import('@/stores/tabStore').TabRoute = isHtmlLike ? '/html-view' : '/pdf-view'
      const tabId = placeTab({ route: fileRoute, title, localFileName: entry.localFileName ?? title, localFilePath, localFileVirtualUrl: undefined, localFileConverting: false })
      const res = await restoreLocalFile(localFilePath)
      if (res) {
        tabStore.updateTab(tabId, { localFileVirtualUrl: res.url })
      }
      return
    }

    // Dev mode: no real path, navigate directly (blob URL is gone — file must be re-opened)
    placeTab({ route, title, localFileName: entry.localFileName ?? title })
  }

  return {
    virtualUrl,
    fileName,
    converting,
    loadingType,
    downloadErrorMessage,
    startLocalFileConversion,
    finishLocalFileConversion,
    finalizeConvertingFromReply,
    cancelConversion,
    startHbDownload,
    finishHbDownload,
    cancelHbDownload,
    openHbBook,
    restoreTab,
    openFromHistory,
  }
})
