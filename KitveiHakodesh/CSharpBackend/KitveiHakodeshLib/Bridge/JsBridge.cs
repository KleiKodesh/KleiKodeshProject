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
        public const string Script = @"
(function () {
    const pending = new Map();
    let nextId = 1;

    window.chrome.webview.addEventListener('message', function (e) {
        const msg = e.data;

        // Push events have no pending id — dispatch to the event handler
        if (msg.event) {
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
})();";
    }
}
