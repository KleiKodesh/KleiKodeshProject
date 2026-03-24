namespace ZayitVueHost.viewer
{
    /// <summary>
    /// JavaScript injected before the Vue app loads.
    /// Registers window.__webviewQuery, __webviewPickDbPath, and __webviewSetDbPath
    /// as Promise-based wrappers over the WebView2 postMessage channel.
    /// </summary>
    internal static class WebViewBridge
    {
        public const string Script = @"
(function () {
    const pending = new Map();
    let nextId = 1;

    window.chrome.webview.addEventListener('message', function (e) {
        const msg = e.data;
        if (msg.event === 'dbPathPicked') {
            if (typeof window.__onDbPathPicked === 'function') window.__onDbPathPicked(msg.path);
            return;
        }
        const callbacks = pending.get(msg.id);
        if (!callbacks) return;
        pending.delete(msg.id);
        msg.error ? callbacks.reject(new Error(msg.error))
                  : callbacks.resolve(msg);
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
    window.__webviewPickDbPath = function ()             { return post({ action: 'pickDbPath' }); };
    window.__webviewSetDbPath  = function (path)         { return post({ action: 'setDbPath', path: path }); };
})();";
    }
}
