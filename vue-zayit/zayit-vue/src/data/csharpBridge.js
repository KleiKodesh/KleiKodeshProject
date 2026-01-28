// /**
//  * C# WebView2 Bridge (function-based, class-equivalent)
//  */

// let bridgeInstance = null;
// export const getCSharpBridge = () => bridgeInstance || (bridgeInstance = createBridge());
// const webViewAvailable = () => !!window.chrome?.webview;

// function createBridge() {
//     if (!webViewAvailable()) return null;
//     const pendingRequests = new Map();

//     window.chrome.webview.addEventListener("message", (event) => {
//         const msg = event.data;
//         if (!msg || !msg.requestId) {
//             console.warn("[CSharpBridge] Received invalid message:", msg);
//             return;
//         }

//         // reject if data is missing
//         if (!("data" in msg)) {
//             console.warn("[CSharpBridge] Received message without data:", msg);

//             if (msg && msg.requestId) {
//                 const pending = pendingRequests.get(msg.requestId);
//                 if (pending) {
//                     pending.reject(new Error("No data returned"));
//                     pendingRequests.delete(msg.requestId);
//                 }
//             }
//             return;
//         }

//         const { requestId, data } = msg;
//         const pending = pendingRequests.get(requestId);
//         if (pending) {
//             pending.resolve(data);
//             pendingRequests.delete(requestId);
//         }
//     });

//     function sendRequest(requestId, args = []) {
//         if (!webViewAvailable) {
//             return Promise.reject(new Error("WebView not available"));
//         }

//         const promise = new Promise((resolve, reject) => {
//             pendingRequests.set(requestId, { resolve, reject });
//         });

//         window.chrome.webview.postMessage({ requestId, args });

//         return promise;
//     }
// C# WebView2 Bridge - runtime JS implementation

let bridgeInstance = null

export const getCSharpBridge = () => bridgeInstance || (bridgeInstance = createBridge())

function createBridge() {
    const pendingRequests = new Map()
    let initialized = false

    function isAvailable() {
        return typeof window !== 'undefined' && window.chrome?.webview !== undefined
    }

    function setupGlobalHandlers() {
        if (initialized || typeof window === 'undefined') return
        initialized = true

        const win = window

        win.receiveTreeData = (data) => {
            const r = pendingRequests.get('GetTree')
            if (r) {
                r.resolve(data)
                pendingRequests.delete('GetTree')
            }
        }

        win.receiveTocData = (bookId, data) => {
            const key = `GetToc:${bookId}`
            const r = pendingRequests.get(key)
            if (r) {
                r.resolve(data)
                pendingRequests.delete(key)
            }
        }

        win.receiveLinks = (tabId, bookId, links) => {
            const key = `GetLinks:${tabId}:${bookId}`
            const r = pendingRequests.get(key)
            if (r) {
                r.resolve(links)
                pendingRequests.delete(key)
            }
        }

        win.receiveTotalLines = (bookId, totalLines) => {
            const key = `GetTotalLines:${bookId}`
            const r = pendingRequests.get(key)
            if (r) {
                r.resolve(totalLines)
                pendingRequests.delete(key)
            }
        }

        win.receiveLineContent = (bookId, lineIndex, content) => {
            const key = `GetLineContent:${bookId}:${lineIndex}`
            const r = pendingRequests.get(key)
            if (r) {
                r.resolve(content)
                pendingRequests.delete(key)
            }
        }

        win.receivePdfFilePath = (virtualUrl, fileName, originalPath) => {
            const r = pendingRequests.get('OpenPdfFilePicker')
            if (r) {
                r.resolve({ fileName, dataUrl: virtualUrl, originalPath })
                pendingRequests.delete('OpenPdfFilePicker')
            }
        }

        win.receivePdfVirtualUrl = (requestPath, virtualUrl) => {
            const key = `RecreateVirtualUrlFromPath:${requestPath}`
            const r = pendingRequests.get(key)
            if (r) {
                r.resolve(virtualUrl)
                pendingRequests.delete(key)
            }
        }

        win.receiveSearchResults = (bookId, searchTerm, results) => {
            const key = `SearchLines:${bookId}:${searchTerm}`
            const r = pendingRequests.get(key)
            if (r) {
                r.resolve(results)
                pendingRequests.delete(key)
            }
        }

        win.receiveHebrewBookDownloadReady = (bookId, action) => {
            const key = `PrepareHebrewBookDownload:${bookId}:${action}`
            const r = pendingRequests.get(key)
            if (r) {
                r.resolve({ success: true })
                pendingRequests.delete(key)
            }
        }

        win.receiveHebrewBookDownloadComplete = (bookId, result) => {
            const s = `HebrewBookDownloadComplete:${bookId}`
            const r = pendingRequests.get(s) || pendingRequests.get(`PrepareHebrewBookDownload:${bookId}:download`)
            if (r) {
                r.resolve(result)
                pendingRequests.delete(s)
                pendingRequests.delete(`PrepareHebrewBookDownload:${bookId}:download`)
            }
        }
    }

    function send(command, args) {
        setupGlobalHandlers()

        if (!isAvailable()) return

        window.chrome.webview.postMessage({ command, args })
    }

    function createRequest(requestId) {
        setupGlobalHandlers()

        return new Promise((resolve, reject) => {
            pendingRequests.set(requestId, { resolve, reject })
        })
    }

    return {
        send,
        createRequest,
        isAvailable
    }
}

