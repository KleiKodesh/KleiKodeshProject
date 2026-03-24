namespace ZayitVueHost
{
    /// <summary>
    /// JavaScript injected before the Vue app loads.
    /// Exposes three Promise-based functions on window:
    ///   __webviewQuery(sql, params)  — run a SQL query
    ///   __webviewSetDbPath(path)     — set the DB path programmatically
    ///   __webviewPickDbPath()        — open the native file picker
    /// </summary>
    internal static class JsBridge
    {
        public const string Script = @"
(function () {
    const pending = new Map();
    let nextId = 1;

    window.chrome.webview.addEventListener('message', function (e) {
        const msg = e.data;

        // dbPathPicked is a push event, not a reply to a pending call
        if (msg.event === 'dbPathPicked') {
            if (typeof window.__onDbPathPicked === 'function') window.__onDbPathPicked(msg.path);
            return;
        }

        const cb = pending.get(msg.id);
        if (!cb) return;
        pending.delete(msg.id);
        msg.error ? cb.reject(new Error(msg.error)) : cb.resolve(msg);
    });

    window.__onDbPathPicked = null;

    function post(payload) {
        return new Promise(function (resolve, reject) {
            const id = String(nextId++);
            pending.set(id, { resolve: resolve, reject: reject });
            window.chrome.webview.postMessage(Object.assign({ id: id }, payload));
        });
    }

    window.__webviewQuery      = function (sql, params) { return post({ sql: sql, params: params || [] }).then(function (m) { return { rows: m.rows }; }); };
    window.__webviewSetDbPath  = function (path)         { return post({ action: 'setDbPath', path: path }); };
    // pickDbPath is fire-and-forget — result arrives via the dbPathPicked push event
    window.__webviewPickDbPath = function ()             { window.chrome.webview.postMessage({ id: '0', action: 'pickDbPath' }); };
})();";
    }
}
