/**
 * Otzaria addin bridge for HtmlViewPage.
 *
 * When an HTML file is opened and its tab has isOtzariaAddin=true, HtmlViewPage
 * calls `onIframeLoaded()` from this composable after the iframe fires onload.
 * This injects window.OtzariaAddin into the iframe and routes all API calls
 * back to the Vue app via postMessage.
 *
 * The bridge is self-contained: it sets up and tears down its own window.message
 * listener in onMounted/onBeforeUnmount, so HtmlViewPage just calls onIframeLoaded()
 * once and everything else is automatic.
 *
 * API surface implemented — all require the corresponding Otzaria permission to be
 * declared in manifest.json (we don't enforce permissions here since the user
 * explicitly opened the addin from their own disk):
 *
 *   app.*        — getInfo, getTheme, getLocale, getGrantedPermissions, openUrl
 *   library.*    — findBooks, getBookMetadata, getBookToc, getBookContent, getTree
 *   reader.*     — openBook, openBookAtRef, getCurrentState
 *   navigation.* — goTo
 *   storage.*    — get, set, remove, list  (per-addin IDB, keyed by addin manifest id)
 *   settings.*   — get, getMany  (safe read-only allowlist)
 *   ui.*         — showMessage, showSuccess, showError, showWarning
 */

import { onMounted, onBeforeUnmount, type Ref } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { useSettingsStore } from '@/stores/settingsStore'

// ── Bridge stub script ────────────────────────────────────────────────────────
// Injected into the addin iframe to create window.OtzariaAddin.
// Must be plain ES5 — we cannot control the addin's execution environment.

function buildBridgeStubScript(): string {
  return `
(function () {
  if (window.OtzariaAddin) return;
  var _callId = 0, _pending = {}, _handlers = {};
  window.addEventListener('message', function (e) {
    var d = e.data;
    if (!d || typeof d !== 'object') return;
    if (d.type === 'otzaria-reply') {
      var cb = _pending[d.callId]; if (!cb) return;
      delete _pending[d.callId];
      cb(d);
    }
    if (d.type === 'otzaria-event') {
      var hs = _handlers[d.event]; if (!hs) return;
      for (var i = 0; i < hs.length; i++) { try { hs[i](d.payload); } catch(_) {} }
    }
  });
  function call(method, params) {
    return new Promise(function (resolve) {
      var id = String(++_callId);
      _pending[id] = resolve;
      window.parent.postMessage({ type: 'otzaria-call', callId: id, method: method, params: params != null ? params : null }, '*');
    }).then(function (r) {
      if (r.error) return Promise.reject(new Error(r.error));
      return r.result;
    });
  }
  function on(event, handler) {
    if (!_handlers[event]) _handlers[event] = [];
    _handlers[event].push(handler);
  }
  function off(event, handler) {
    if (!_handlers[event]) return;
    if (!handler) { _handlers[event] = []; return; }
    _handlers[event] = _handlers[event].filter(function (h) { return h !== handler; });
  }
  window.OtzariaAddin = { call: call, on: on, off: off };
})();
`
}

// ── Per-addin IDB storage ────────────────────────────────────────────────────

function openAddinDatabase(addinId: string): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(`app-addin-storage-${addinId}`, 1)
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains('data'))
        request.result.createObjectStore('data')
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

async function addinStorageGet(addinId: string, key: string): Promise<unknown> {
  const db = await openAddinDatabase(addinId)
  return new Promise((resolve, reject) => {
    const req = db.transaction('data').objectStore('data').get(key)
    req.onsuccess = () => resolve(req.result ?? null)
    req.onerror = () => reject(req.error)
  })
}

async function addinStorageSet(addinId: string, key: string, value: unknown): Promise<void> {
  const db = await openAddinDatabase(addinId)
  return new Promise((resolve, reject) => {
    const req = db.transaction('data', 'readwrite').objectStore('data').put(value, key)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

async function addinStorageRemove(addinId: string, key: string): Promise<void> {
  const db = await openAddinDatabase(addinId)
  return new Promise((resolve, reject) => {
    const req = db.transaction('data', 'readwrite').objectStore('data').delete(key)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

async function addinStorageListKeys(addinId: string): Promise<string[]> {
  const db = await openAddinDatabase(addinId)
  return new Promise((resolve, reject) => {
    const req = db.transaction('data').objectStore('data').getAllKeys()
    req.onsuccess = () => resolve(req.result as string[])
    req.onerror = () => reject(req.error)
  })
}

// ── Theme helper ─────────────────────────────────────────────────────────────

function getCurrentTheme() {
  const style = document.documentElement.style
  const isDark = document.documentElement.getAttribute('data-theme-preset')?.includes('dark') ?? true
  return {
    isDark,
    colors: {
      background: style.getPropertyValue('--bg-primary-custom').trim() || (isDark ? '#1e1e1e' : '#ffffff'),
      surface: style.getPropertyValue('--bg-secondary-custom').trim() || (isDark ? '#252526' : '#f3f3f3'),
      primary: '#0078d4',
      onBackground: style.getPropertyValue('--text-primary-custom').trim() || (isDark ? '#d4d4d4' : '#616161'),
      onSurface: style.getPropertyValue('--text-primary-custom').trim() || (isDark ? '#d4d4d4' : '#616161'),
      secondary: style.getPropertyValue('--text-secondary-custom').trim() || (isDark ? '#858585' : '#999999'),
      onSecondary: '#ffffff',
    },
  }
}

// ── Main composable ───────────────────────────────────────────────────────────

export function useOtzariaAddinBridge(
  iframeRef: Ref<HTMLIFrameElement | null>,
  /** The manifest id from the addin (used to scope IDB storage). Falls back to filePath. */
  addinIdRef: Ref<string>,
) {
  const tabStore = useTabStore()
  const settingsStore = useSettingsStore()

  function pushEvent(event: string, payload: unknown) {
    iframeRef.value?.contentWindow?.postMessage({ type: 'otzaria-event', event, payload }, '*')
  }

  async function routeCall(method: string, params: unknown): Promise<unknown> {
    const p = (params ?? {}) as Record<string, unknown>
    const addinId = addinIdRef.value

    if (method === 'app.getInfo')
      return { version: '1.0.0', platform: 'windows', locale: 'he', textDirection: 'rtl' }

    if (method === 'app.getTheme')
      return getCurrentTheme()

    if (method === 'app.getLocale')
      return { locale: 'he', textDirection: 'rtl' }

    if (method === 'app.getGrantedPermissions')
      return [] // all permissions are implicitly granted — user picked the file themselves

    if (method === 'app.openUrl') {
      const url = p.url as string
      if (url) window.open(url, '_blank', 'noopener,noreferrer')
      return { ok: true }
    }

    if (method === 'library.findBooks') {
      const rows = await query<{ id: number; title: string; heShortDesc: string | null }>(
        SQL.ADDIN_SEARCH_BOOKS, [`%${(p.query as string) ?? ''}%`, Number(p.limit ?? 50)],
      )
      return { books: rows }
    }

    if (method === 'library.getBookMetadata') {
      const rows = await query<{ id: number; title: string; heShortDesc: string | null; totalLines: number }>(
        SQL.ADDIN_GET_BOOK_METADATA, [Number(p.bookId)],
      )
      return rows[0] ?? null
    }

    if (method === 'library.getBookToc') {
      const rows = await query<{ id: number; parentId: number | null; text: string; level: number; lineId: number | null; isLastChild: number; hasChildren: number }>(
        SQL.ADDIN_GET_BOOK_TOC, [Number(p.bookId)],
      )
      return { entries: rows }
    }

    if (method === 'library.getBookContent') {
      const rows = await query<{ lineIndex: number; content: string }>(
        SQL.ADDIN_GET_BOOK_CONTENT, [Number(p.bookId), Number(p.startLineIndex ?? 0), Number(p.maxLines ?? 100)],
      )
      return { lines: rows }
    }

    if (method === 'library.getTree') {
      const rows = await query<{ id: number; parentId: number | null; title: string; level: number }>(
        SQL.ADDIN_GET_CATEGORY_TREE, [],
      )
      return { categories: rows }
    }

    if (method === 'reader.openBook') {
      const bookId = Number(p.bookId)
      if (!bookId) throw new Error('bookId נדרש')
      tabStore.updateActiveTab({ route: '/book-view', title: (p.title as string) || '', bookId })
      return { ok: true }
    }

    if (method === 'reader.openBookAtRef') {
      const bookId = Number(p.bookId)
      if (!bookId) throw new Error('bookId נדרש')
      tabStore.updateActiveTab({
        route: '/book-view',
        title: (p.title as string) || '',
        bookId,
        ...(p.lineIndex !== undefined ? { openTocLineIndex: Number(p.lineIndex) } : {}),
        ...(p.tocEntryId !== undefined ? { openTocEntryId: Number(p.tocEntryId) } : {}),
      })
      return { ok: true }
    }

    if (method === 'reader.getCurrentState') {
      const tab = tabStore.activeTab
      if (tab.route !== '/book-view') return null
      return { bookId: tab.bookId ?? null }
    }

    if (method === 'navigation.goTo') {
      const routeMap: Record<string, string> = { library: '/books', settings: '/settings' }
      const mapped = routeMap[p.target as string]
      if (mapped) tabStore.navigateToDestination(mapped as '/books' | '/settings')
      return { ok: true }
    }

    if (method === 'storage.get')
      return { value: await addinStorageGet(addinId, p.key as string) }

    if (method === 'storage.set') {
      await addinStorageSet(addinId, p.key as string, p.value)
      return { ok: true }
    }

    if (method === 'storage.remove') {
      await addinStorageRemove(addinId, p.key as string)
      return { ok: true }
    }

    if (method === 'storage.list')
      return { keys: await addinStorageListKeys(addinId) }

    if (method === 'settings.get')
      return { value: readSafeSetting(p.key as string) }

    if (method === 'settings.getMany') {
      const result: Record<string, unknown> = {}
      for (const key of (p.keys as string[]) ?? []) result[key] = readSafeSetting(key)
      return result
    }

    if (method === 'ui.showMessage' || method === 'ui.showSuccess' ||
        method === 'ui.showError' || method === 'ui.showWarning') {
      const kind = method.replace('ui.show', '').toLowerCase()
      pushEvent('ui.message', { kind, text: p.text ?? '' })
      return { ok: true }
    }

    throw new Error(`שיטה לא מוכרת: ${method}`)
  }

  const SAFE_SETTINGS: Record<string, () => unknown> = {
    'reading.fontSize': () => settingsStore.fontSize,
    'reading.lineSpacing': () => settingsStore.linePadding,
    'app.isDark': () => document.documentElement.getAttribute('data-theme-preset')?.includes('dark') ?? true,
  }

  function readSafeSetting(key: string): unknown {
    return SAFE_SETTINGS[key]?.() ?? null
  }

  // ── Message handler ──────────────────────────────────────────────────────

  async function handleMessage(event: MessageEvent) {
    if (!event.data || event.data.type !== 'otzaria-call') return
    const iframe = iframeRef.value
    if (!iframe?.contentWindow || event.source !== iframe.contentWindow) return

    const { callId, method, params } = event.data
    let result: unknown
    let error: string | undefined
    try {
      result = await routeCall(method, params)
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    }

    iframe.contentWindow.postMessage(
      { type: 'otzaria-reply', callId, ...(error !== undefined ? { error } : { result }) },
      '*',
    )
  }

  // ── Theme observer ────────────────────────────────────────────────────────

  let themeObserver: MutationObserver | null = null

  function startThemeObserver() {
    themeObserver?.disconnect()
    themeObserver = new MutationObserver(() => pushEvent('theme.changed', getCurrentTheme()))
    themeObserver.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme-preset', 'style'],
    })
  }

  // ── Lifecycle ─────────────────────────────────────────────────────────────

  onMounted(() => window.addEventListener('message', handleMessage))

  onBeforeUnmount(() => {
    window.removeEventListener('message', handleMessage)
    themeObserver?.disconnect()
    themeObserver = null
  })

  return {
    /** Call once after the iframe's onload fires to inject the stub and fire plugin.boot. */
    onIframeLoaded() {
      const iframe = iframeRef.value
      if (!iframe?.contentWindow || !iframe?.contentDocument) return

      // Inject the bridge stub as a <script> tag directly into the iframe document.
      // This works because the virtual host serves the addin on the same origin as the
      // Vue app (same-origin /khs-file proxy in dev; ms-local-stream in hosted mode also
      // allows same-document script injection via contentDocument).
      try {
        const script = iframe.contentDocument.createElement('script')
        script.textContent = buildBridgeStubScript()
        iframe.contentDocument.head?.appendChild(script) ?? iframe.contentDocument.body?.appendChild(script)
      } catch {
        // Cross-origin fallback — should not happen with virtual host URLs, but be defensive.
        return
      }

      // Wait one tick for the stub to be evaluated, then fire plugin.boot.
      setTimeout(() => {
        startThemeObserver()
        pushEvent('plugin.boot', {
          plugin: { id: addinIdRef.value, version: '1.0.0' },
          app: { version: '1.0.0', platform: 'windows', runMode: 'foreground', locale: 'he', textDirection: 'rtl' },
          theme: getCurrentTheme(),
          permissions: [],
        })
      }, 50)
    },
  }
}
