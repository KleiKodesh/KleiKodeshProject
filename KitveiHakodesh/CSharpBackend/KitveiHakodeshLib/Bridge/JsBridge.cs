namespace KitveiHakodeshLib.Bridge
{
    /// <summary>
    /// JavaScript injected before the Vue app loads.
    /// Exposes Promise-based functions on window:
    ///   __webviewQuery(sql, params)        — run a SQL query against the main DB
    ///   __webviewDictQuery(sql, params)    — run a SQL query against KitveiHakodesh_dictionary.db
    ///   __webviewSetDbPath(path)           — set the DB path programmatically
    ///   __webviewPickDbPath()              — open the native DB file picker (push event)
    ///   __webviewAction(action, args)      — generic bridge for all other actions
    /// Push events (no pending id) are dispatched via window.__onWebviewEvent(msg).
    /// </summary>
    public static class JsBridge
    {
        /// <summary>
        /// Bridge script injected into the top-level frame on document creation.
        /// Sets up the Promise-based RPC channel between Vue and C#.
        /// </summary>
        public const string Script = @"
(function () {
    const pending = new Map();
    let nextId = 1;

    window.chrome.webview.addEventListener('message', function (e) {
        const msg = e.data;

        // Push events have no pending id — dispatch to the event handler
        if (msg.event || msg.type) {
            if (typeof window.__onWebviewEvent === 'function') window.__onWebviewEvent(msg);
            return;
        }

        const cb = pending.get(msg.id);
        if (!cb) return;
        pending.delete(msg.id);
        msg.error ? cb.reject(new Error(msg.error)) : cb.resolve(msg);
    });

    window.__onWebviewEvent = null;

    function post(payload) {
        return new Promise(function (resolve, reject) {
            const id = String(nextId++);
            pending.set(id, { resolve: resolve, reject: reject });
            window.chrome.webview.postMessage(Object.assign({ id: id }, payload));
        });
    }

    window.__webviewQuery          = function (sql, params) { return post({ sql: sql, params: params || [] }).then(function (m) { return { rows: m.rows }; }); };
    window.__webviewDictQuery      = function (sql, params) { return post({ action: 'dict-sql', sql: sql, params: params || [] }).then(function (m) { return { rows: m.rows }; }); };
    window.__webviewSetDbPath      = function (path)         { return post({ action: 'setDbPath', path: path }); };
    window.__webviewPickDbPath     = function ()             { window.chrome.webview.postMessage({ id: '0', action: 'pickDbPath' }); };
    window.__webviewAction         = function (action, args) { return post(Object.assign({ action: action }, args || {})); };
    window.__webviewUserSettingsQuery   = function (sql, params) { return post({ action: 'userSettingsQuery',   sql: sql, params: params || [] }).then(function (m) { return { rows: m.rows }; }); };
    window.__webviewUserSettingsExecute = function (sql, params) { return post({ action: 'userSettingsExecute', sql: sql, params: params || [] }).then(function (m) { return { lastInsertId: m.lastInsertId }; }); };
})();";

        /// <summary>
        /// Scroll-tracker script injected into every frame (including local file iframes)
        /// via a separate AddScriptToExecuteOnDocumentCreatedAsync call.
        ///
        /// In child frames (local HTML/TXT files served from kitvei-localfile-N):
        ///   - Posts throttled scroll position to window.top as { type: 'htmlViewScroll', scrollTop }
        ///   - Listens for { type: 'htmlViewScrollTo', scrollTop } commands from the parent
        ///     and calls window.scrollTo() to restore the saved position.
        ///   - Forwards Ctrl+key and Ctrl+Shift+key keydown events to window.top as
        ///     { type: 'iframeKeydown', code, ctrlKey, shiftKey, metaKey } so that app-level
        ///     shortcuts (Ctrl+W, Ctrl+N, Ctrl+F, etc.) work when focus is inside the iframe.
        ///
        /// In the top frame (the Vue app) this script is a no-op — the early return fires
        /// immediately.
        /// </summary>
        public const string IframeScrollScript = @"
(function () {
    if (window === window.top) return;

    // Throttled scroll reporter — posts current scrollY to the Vue app parent.
    var scrollTimer = null;
    function reportScroll() {
        scrollTimer = null;
        var top = window.scrollY || document.documentElement.scrollTop || document.body.scrollTop || 0;
        window.top.postMessage({ type: 'htmlViewScroll', scrollTop: top }, '*');
    }
    window.addEventListener('scroll', function () {
        if (scrollTimer) return;
        scrollTimer = setTimeout(reportScroll, 200);
    }, { passive: true });

    // Forward Ctrl+key shortcuts to the parent frame so app-level keyboard
    // shortcuts (Ctrl+W, Ctrl+F, Ctrl+N, etc.) continue to work when focus
    // is inside the iframe.  The parent reconstructs a synthetic KeyboardEvent
    // and dispatches it on its own window.
    // Use capture phase so this fires before any iframe-internal handlers
    // (e.g. PDF.js) that may call preventDefault() on the same event.
    window.addEventListener('keydown', function (e) {
        if (!e.ctrlKey && !e.metaKey) return;
        // Let the browser handle text-editing shortcuts inside the iframe itself
        // (Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+Z, Ctrl+Y) — only forward navigation and
        // app-level shortcuts.
        var editing = ['KeyA','KeyC','KeyV','KeyX','KeyZ','KeyY'];
        if (editing.indexOf(e.code) !== -1 && !e.shiftKey) return;
        window.top.postMessage({
            type: 'iframeKeydown',
            code: e.code,
            ctrlKey: e.ctrlKey,
            shiftKey: e.shiftKey,
            metaKey: e.metaKey,
            altKey: e.altKey
        }, '*');
        e.preventDefault();
    }, true);

    // Listen for scrollTo commands posted by HtmlViewPage.vue on iframe load.
    window.addEventListener('message', function (e) {
        if (!e.data) return;
        if (e.data.type === 'htmlViewScrollTo') {
            window.scrollTo({ top: e.data.scrollTop, behavior: 'instant' });
        }
        if (e.data.type === 'htmlViewTheme') {
            var c = e.data.colors;
            if (!c) return;
            document.documentElement.style.setProperty('--iframe-bg', c.bgPrimary || '');
            document.documentElement.style.setProperty('--iframe-text', c.textPrimary || '');
            document.documentElement.style.setProperty('--iframe-text-secondary', c.textSecondary || '');
            // Apply directly to body so it works regardless of whether a <style> was injected
            if (document.body) {
                document.body.style.background = c.bgPrimary || '';
                document.body.style.color = c.textPrimary || '';
            }
            // Inject scrollbar styling to match the app's thin scrollbar appearance.
            // Uses the resolved color value sent from the parent (textSecondary).
            var styleId = '__kitvei-scrollbar-style';
            var existing = document.getElementById(styleId);
            if (existing) existing.remove();
            var scrollbarStyle = document.createElement('style');
            scrollbarStyle.id = styleId;
            var thumbColor = c.textSecondary
                ? 'color-mix(in srgb, ' + c.textSecondary + ' 30%, transparent)'
                : 'rgba(128,128,128,0.3)';
            var thumbHoverColor = c.textSecondary
                ? 'color-mix(in srgb, ' + c.textSecondary + ' 50%, transparent)'
                : 'rgba(128,128,128,0.5)';
            scrollbarStyle.textContent =
                '* { scrollbar-color: ' + thumbColor + ' transparent; scrollbar-width: thin; }' +
                '*::-webkit-scrollbar { width: 6px; height: 6px; }' +
                '*::-webkit-scrollbar-track { background: transparent; }' +
                '*::-webkit-scrollbar-thumb { background: ' + thumbColor + '; border-radius: 0; }' +
                '*::-webkit-scrollbar-thumb:hover { background: ' + thumbHoverColor + '; }';
            (document.head || document.documentElement).appendChild(scrollbarStyle);
        }
    });

    // Plain-text files are served as text/plain and rendered by the browser as a
    // bare <pre> with no author styles. Strip any HTML tags from the content and
    // inject RTL alignment so Hebrew text reads correctly right-to-left.
    function applyTxtStyles() {
        var ct = document.contentType || '';
        if (ct !== 'text/plain') return;

        // Strip HTML tags — replace every <...> sequence with nothing so that
        // txt files that happen to contain markup are shown as plain text.
        var pre = document.querySelector('pre');
        if (pre) {
            pre.textContent = pre.textContent.replace(/<[^>]*>/g, '');
        }

        var style = document.createElement('style');
        style.textContent =
            'body, pre { direction: rtl; text-align: right; unicode-bidi: plaintext; ' +
            'font-family: ""Segoe UI"", system-ui, sans-serif; font-size: 14px; ' +
            'line-height: 1.7; margin: 16px; white-space: pre-wrap; word-break: break-word; }';
        document.head.appendChild(style);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', applyTxtStyles);
    } else {
        applyTxtStyles();
    }
})();";
    }
}
