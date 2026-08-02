import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { useTabStore } from './tabStore'
import type { Tab, TabRoute } from './tabStore'
import { useBookViewStore } from './bookViewStore'
import {
  disposeLocalFileHost,
  restoreLocalFile,
  restoreHbPdf,
  getHbDownloadProgress,
  // Aliased: the store's own cancelHbDownload (the hbPdfCancelled handler below) would otherwise
  // SHADOW this import inside the defineStore scope — silently turning the service abort into a
  // no-op (legal JS, no TS error). Keep the distinct name.
  cancelHbDownload as cancelHbDownloadInService,
  cancelLocalFileConversion,
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
   * True when the target pane holds nothing but a home tab — the one the store
   * auto-creates for a session with no saved tabs (see DEFAULT_TAB in tabStore).
   * An "Open With" launch lands exactly here: its push event asks for a new tab,
   * and honouring that would leave the untouched home tab behind as a redundant
   * first tab, so the file takes that tab over instead. Mirrors the same "unless
   * the current tab is home" rule navigateToSingleton uses.
   */
  function targetPaneHasOnlyHomeTab(): boolean {
    const pane = targetPaneId()
    const paneTabs = pane === 2 ? tabStore.pane2Tabs : tabStore.pane1Tabs
    if (paneTabs.length !== 1 || paneTabs[0]!.route !== '/') return false
    // In-place navigation patches the pane's ACTIVE tab — only claim the home tab
    // when that is what it will hit, otherwise the file would land on nothing.
    const activeId = pane === 2 ? tabStore.pane2ActiveTabId : tabStore.activeTabId
    return paneTabs[0]!.id === activeId
  }

  /**
   * Resolve the new-tab intent for a push event: the in-flight user pick's intent
   * (frontend-owned, since the C# events don't carry it), falling back to the
   * event's own flag for "Open With" launches. A user pick's intent is absolute —
   * a Ctrl-click gets its own tab even from the home page — but the "Open With"
   * flag yields to a lone home tab, which the file replaces rather than doubling.
   */
  function resolveOpenInNewTab(eventFlag: unknown): boolean {
    const pickIntent = pendingPickOpenInNewTab()
    if (pickIntent !== null) return pickIntent
    return !!eventFlag && !targetPaneHasOnlyHomeTab()
  }

  const virtualUrl = computed(() => tabStore.activeTab.localFileVirtualUrl ?? null)
  const fileName = computed(() => tabStore.activeTab.localFileName ?? null)
  const converting = computed(() => tabStore.activeTab.localFileConverting ?? false)
  const loadingType = computed(() => tabStore.activeTab.localFileLoadingType ?? 'converting')

  // Shown in the HebrewBooks page when a download fails (book not found on server).
  const downloadErrorMessage = ref<string | null>(null)

  // Set of tabIds currently converting — used to ignore results after cancel/navigate/close
  const _converting = new Set<string>()

  // Active HB download progress pollers, keyed by tabId (dev only — the C# service streams the
  // download and we poll its byte progress to update the loading text under the spinner).
  const _hbProgressTimers = new Map<string, ReturnType<typeof setInterval>>()

  function stopHbProgressPoll(tabId: string) {
    const t = _hbProgressTimers.get(tabId)
    if (t) { clearInterval(t); _hbProgressTimers.delete(tabId) }
  }

  /** Poll the service's live download progress and update the tab's loading text (keeps the
   *  spinner ring; only the sub-text changes). No-op in hosted mode (getHbDownloadProgress → null). */
  function startHbProgressPoll(tabId: string, bookId: string) {
    stopHbProgressPoll(tabId)
    const fmtMb = (b: number) => (b / (1024 * 1024)).toFixed(1)
    const timer = setInterval(async () => {
      // Stop if the tab is no longer downloading (finished / cancelled / navigated away).
      if (!_converting.has(tabId)) { stopHbProgressPoll(tabId); return }
      const p = await getHbDownloadProgress(bookId)
      if (!p || !p.active) return // not started yet, or briefly between poll and completion
      const text = p.total > 0
        ? `${fmtMb(p.received)} / ${fmtMb(p.total)} MB · ${Math.floor((p.received / p.total) * 100)}%`
        : `${fmtMb(p.received)} MB`
      tabStore.updateTab(tabId, { localFileDownloadProgress: text })
    }, 300)
    _hbProgressTimers.set(tabId, timer)
  }

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
        isOtzariaAddin: false,
      }
      const tabId = resolveOpenInNewTab(msg.openInNewTab)
        ? openInTargetPane(tabFields)
        : updateActiveInTargetPane(tabFields)
      _converting.delete(tabId)
    }
    if (msg.event === 'localFileReady') {
      // HTML and PDF files — served via virtual host URL.
      // isOtzariaAddin is set by C#/dev-service when it detects a manifest.json
      // next to the HTML file — HtmlViewPage checks this flag to activate the bridge.
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
        // Written unconditionally: these fields are MERGED onto the tab, so spreading
        // the key in only when true would leave a previous addin's flag on a tab that
        // is now showing an unrelated HTML file — and the bridge would inject into it.
        isOtzariaAddin: isHtmlLike && !!msg.isOtzariaAddin,
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
        if (wasRemoved || navigatedAway) { _converting.delete(tabId); stopHbProgressPoll(tabId) }
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
      // Route by what was actually produced: hosted conversions are always PDF, but the dev
      // service may render Word docs to HTML (Office-free fallback) — the served name says.
      const isHtml = /\.html?$/i.test(result.fileName)
      tabStore.updateTab(tabId, {
        route: isHtml ? '/html-view' : '/pdf-view',
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

  /** Cancel an in-progress conversion or HB download — aborts the work in the service
   *  (real cancellation + cleanup, not just a UI dismiss) and resets the tab to home. */
  function cancelConversion(tabId: string) {
    _converting.delete(tabId)
    const tab = tabStore.tabs.find((t) => t.id === tabId)
    // HebrewBooks download in flight → tell the service to abort it (deletes the partial file).
    if (tab?.localFileHbBookId) {
      stopHbProgressPoll(tabId)
      cancelHbDownloadInService(tab.localFileHbBookId)
    }
    // Word/document conversion in flight → tell the service to abort it (kills Word, deletes partial).
    else if (tab?.localFilePath) {
      cancelLocalFileConversion(tab.localFilePath)
      // Dispose the virtual host before clearing localFilePath so the mapping is released.
      disposeLocalFileHost(tab.localFilePath)
    }
    tabStore.updateTab(tabId, {
      route: '/',
      title: 'בית',
      localFileVirtualUrl: undefined,
      localFileName: undefined,
      localFilePath: undefined,
      localFileHbBookId: undefined,
      localFileConverting: false,
      localFileDownloadProgress: undefined,
    })
  }

  /** Navigate a tab to /pdf-view placeholder while a HebrewBooks download is in progress.
   *  When a bookId is given (dev), start polling the service for live download progress so the
   *  loading text under the spinner shows MB / %. */
  function startHbDownload(bookTitle: string, tabId: string, bookId?: string) {
    _converting.add(tabId)
    tabStore.updateTab(tabId, {
      route: '/pdf-view',
      title: bookTitle,
      localFileName: bookTitle,
      // Stamp the book id NOW (not only on success) so ביטול mid-download can identify this as a
      // HB download and abort it in the service. Without this the cancel branch is skipped and the
      // download runs to completion in the background.
      localFileHbBookId: bookId,
      localFileHbBookTitle: bookTitle,
      localFileConverting: true,
      localFileLoadingType: 'downloading',
      localFileDownloadProgress: undefined,
      localFileVirtualUrl: undefined,
    })
    if (bookId) startHbProgressPoll(tabId, bookId)
  }

  /** Called when hbPdfReady fires — ignored if tab was closed/navigated away. */
  function finishHbDownload(tabId: string, url: string, bookTitle: string, bookId: string) {
    stopHbProgressPoll(tabId)
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
      localFileDownloadProgress: undefined,
    })
  }

  /** Called on hbPdfCancelled — closes the tab. Shows an error if the book was not found. */
  function cancelHbDownload(tabId: string, notFound = false, noInternet = false) {
    stopHbProgressPoll(tabId)
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

  /**
   * Routes a PERSONAL book (user_books.db) whose content is a non-txt FILE into the
   * local-file viewer flow, in place, on the tab that opened as /book-view. PDF and
   * docx personal books cannot render as text — Otzaria keeps their content in the
   * file at book.filePath — so the tab becomes a normal local-file tab (same serving,
   * conversion, persistence and restore as "open file"). txt books never come here
   * (BookView serves them line-by-line from the file).
   */
  async function openUserBookFile(tabId: string, bookId: number, filePath: string): Promise<void> {
    const ext = filePath.substring(filePath.lastIndexOf('.')).toLowerCase()
    const fileName = filePath.substring(Math.max(filePath.lastIndexOf('\\'), filePath.lastIndexOf('/')) + 1)
    // catch: the hosted restoreLocalFile branch can reject (dev's cannot) — a served
    // failure must degrade to "stay in BookView", never an unhandled rejection.
    const res = await restoreLocalFile(filePath).catch(() => null)
    if (!res) {
      console.warn('[localFile] personal-book file could not be served:', filePath)
      return
    }
    // The await above can span a full Word conversion (first-open docx runs for
    // seconds) — the user may have navigated this tab elsewhere or closed it.
    // Patch ONLY if it is still the /book-view tab showing the book that asked;
    // anything else would hijack the tab back to an abandoned book's file.
    const tab = tabStore.tabs.find((t) => t.id === tabId)
    if (!tab || tab.route !== '/book-view' || tab.bookId !== bookId) return
    // Route by what is actually served (dev Word docs may come back as HTML).
    const isHtmlLike = res.kind === 'html' || (!res.kind && (ext === '.htm' || ext === '.html'))
    tabStore.updateTab(tabId, {
      route: isHtmlLike ? '/html-view' : '/pdf-view',
      localFileName: fileName,
      localFilePath: filePath,
      localFileVirtualUrl: res.url,
      localFileConverting: false,
      isOtzariaAddin: false,
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
      // If restore misses and re-downloads, show live progress under the spinner (dev).
      startHbProgressPoll(tabId, tab.localFileHbBookId)
      const res = await restoreHbPdf(tab.localFileHbBookId, tab.localFileHbBookTitle ?? '', tabId, localFolder)
      if (!res) {
        stopHbProgressPoll(tabId)
        _converting.delete(tabId)
        tabStore.closeTab(tabId)
      } else if ('url' in res) {
        stopHbProgressPoll(tabId) // local/cache hit — no download happened
        _converting.delete(tabId)
        tabStore.updateTab(tabId, {
          localFileVirtualUrl: res.url,
          localFileConverting: false,
          localFileLoadingType: undefined,
        })
      }
      // redownload: true — stays in _converting, poller runs, hbPdfReady push event will finish it
    } else if (tab.localFilePath) {
      const ext = tab.localFilePath.substring(tab.localFilePath.lastIndexOf('.')).toLowerCase()
      if (ext === '.txt') {
        // .txt tabs restore directly — TxtViewPage reads the file itself via bridge
        tabStore.updateTab(tabId, { route: '/txt-view' })
      } else {
        const res = await restoreLocalFile(tab.localFilePath)
        if (res) {
          // Route by what is actually served: dev Word docs may come back as HTML
          // (Office-free fallback) — res.kind reports it. Fall back to the extension.
          const isHtmlLike = res.kind === 'html' || (!res.kind && (ext === '.htm' || ext === '.html'))
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
      startHbProgressPoll(tabId, localFileHbBookId)
      const res = await restoreHbPdf(localFileHbBookId, localFileHbBookTitle ?? '', tabId, localFolder)
      if (!res) {
        stopHbProgressPoll(tabId)
        _converting.delete(tabId)
        tabStore.updateTab(tabId, { route: '/', title: 'בית', localFileConverting: false })
      } else if ('url' in res) {
        stopHbProgressPoll(tabId)
        _converting.delete(tabId)
        tabStore.updateTab(tabId, { localFileVirtualUrl: res.url, localFileConverting: false, localFileLoadingType: undefined })
      }
      // redownload: true — poller runs, hbPdfReady push event will finish it
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
        // Dev may serve a Word doc as HTML (Office-free fallback) — follow the served kind.
        const route = res.kind === 'html' ? '/html-view' : res.kind === 'pdf' ? '/pdf-view' : fileRoute
        tabStore.updateTab(tabId, { localFileVirtualUrl: res.url, route })
      }
      return
    }

    // No stored path — an older recents entry saved before local files carried one. Nothing can
    // be re-served, so open the tab empty and let the user re-open the file. Not a dev/hosted
    // distinction: both modes serve real paths now (hosted via its virtual host, dev via the
    // service's capability-gated /khs-file proxy).
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
    openUserBookFile,
  }
})
