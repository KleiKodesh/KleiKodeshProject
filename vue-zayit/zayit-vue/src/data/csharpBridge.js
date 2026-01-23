/**
 * C# WebView2 Bridge (function-based, class-equivalent)
 */

let bridgeInstance = null;
export const getCSharpBridge = () => bridgeInstance || (bridgeInstance = createBridge());
const webViewAvailable = () => !!window.chrome?.webview;

function createBridge() {
    if (!webViewAvailable()) return null;
    const pendingRequests = new Map();

    window.chrome.webview.addEventListener("message", (event) => {
        const msg = event.data;
        if (!msg || !msg.requestId) {
            console.warn("[CSharpBridge] Received invalid message:", msg);
            return;
        }

        // reject if data is missing
        if (!("data" in msg)) {
            console.warn("[CSharpBridge] Received message without data:", msg);

            if (msg && msg.requestId) {
                const pending = pendingRequests.get(msg.requestId);
                if (pending) {
                    pending.reject(new Error("No data returned"));
                    pendingRequests.delete(msg.requestId);
                }
            }
            return;
        }

        const { requestId, data } = msg;
        const pending = pendingRequests.get(requestId);
        if (pending) {
            pending.resolve(data);
            pendingRequests.delete(requestId);
        }
    });

    function sendRequest(requestId, args = []) {
        if (!webViewAvailable) {
            return Promise.reject(new Error("WebView not available"));
        }

        const promise = new Promise((resolve, reject) => {
            pendingRequests.set(requestId, { resolve, reject });
        });

        window.chrome.webview.postMessage({ requestId, args });

        return promise;
    }

    return { createRequest };
}


// function createBridge() {
//     if (!webViewAvailable) return;
//     const pendingRequests = new Map<string, PendingRequest>();

//     function setupGlobalHandlers(): void {


//         const win = window as any;

//         win.receiveTreeData = (data: any) => {
//             const r = pendingRequests.get("GetTree");
//             if (r) {
//                 r.resolve(data);
//                 pendingRequests.delete("GetTree");
//             }
//         };

//         win.receiveTocData = (bookId: number, data: any) => {
//             const key = `GetToc:${bookId}`;
//             const r = pendingRequests.get(key);
//             if (r) {
//                 r.resolve(data);
//                 pendingRequests.delete(key);
//             }
//         };

//         win.receiveLinks = (tabId: string, bookId: number, links: any) => {
//             const key = `GetLinks:${tabId}:${bookId}`;
//             const r = pendingRequests.get(key);
//             if (r) {
//                 r.resolve(links);
//                 pendingRequests.delete(key);
//             }
//         };

//         win.receiveTotalLines = (bookId: number, totalLines: number) => {
//             const key = `GetTotalLines:${bookId}`;
//             const r = pendingRequests.get(key);
//             if (r) {
//                 r.resolve(totalLines);
//                 pendingRequests.delete(key);
//             }
//         };

//         win.receiveLineContent = (
//             bookId: number,
//             lineIndex: number,
//             content: string | null
//         ) => {
//             const key = `GetLineContent:${bookId}:${lineIndex}`;
//             const r = pendingRequests.get(key);
//             if (r) {
//                 r.resolve(content);
//                 pendingRequests.delete(key);
//             }
//         };

//         win.receivePdfFilePath = (
//             virtualUrl: string | null,
//             fileName: string | null,
//             originalPath: string | null
//         ) => {
//             const r = pendingRequests.get("OpenPdfFilePicker");
//             if (r) {
//                 r.resolve({ fileName, dataUrl: virtualUrl, originalPath });
//                 pendingRequests.delete("OpenPdfFilePicker");
//             }
//         };
//     }
//     /* ---------------- public API ---------------- */

//     function send(command: string, args: any[]): void {
//         setupGlobalHandlers();

//         if (!webViewAvailable) return;

//         (window as any).chrome.webview.postMessage({ command, args });
//     }

//     function createRequest<T>(requestId: string): Promise<T> {
//         setupGlobalHandlers();

//         return new Promise((resolve, reject) => {
//             pendingRequests.set(requestId, { resolve, reject });
//         });
//     }

//     /* ---------------- exposed object ---------------- */

//     return {
//         send,
//         createRequest,
//     };
// }

