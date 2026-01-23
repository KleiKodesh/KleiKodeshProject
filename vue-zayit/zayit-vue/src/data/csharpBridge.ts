/**
 * C# WebView2 Bridge (function-based, class-equivalent)
 */

type PendingRequest = {
    resolve: Function;
    reject: Function;
};

let bridgeInstance: ReturnType<typeof createBridge> | null = null;

export const getCSharpBridge = () => bridgeInstance || (bridgeInstance = createBridge());


function createBridge() {
    /* ---------------- private state ---------------- */

    const pendingRequests = new Map<string, PendingRequest>();
    let initialized = false;

    /* ---------------- private helpers ---------------- */

    function isAvailable(): boolean {
        return typeof window !== "undefined" &&
            (window as any).chrome?.webview !== undefined;
    }

    function setupGlobalHandlers(): void {
        if (initialized || typeof window === "undefined") return;
        initialized = true;

        const win = window as any;

        win.receiveTreeData = (data: any) => {
            const r = pendingRequests.get("GetTree");
            if (r) {
                r.resolve(data);
                pendingRequests.delete("GetTree");
            }
        };

        win.receiveTocData = (bookId: number, data: any) => {
            const key = `GetToc:${bookId}`;
            const r = pendingRequests.get(key);
            if (r) {
                r.resolve(data);
                pendingRequests.delete(key);
            }
        };

        win.receiveLinks = (tabId: string, bookId: number, links: any) => {
            const key = `GetLinks:${tabId}:${bookId}`;
            const r = pendingRequests.get(key);
            if (r) {
                r.resolve(links);
                pendingRequests.delete(key);
            }
        };

        win.receiveTotalLines = (bookId: number, totalLines: number) => {
            const key = `GetTotalLines:${bookId}`;
            const r = pendingRequests.get(key);
            if (r) {
                r.resolve(totalLines);
                pendingRequests.delete(key);
            }
        };

        win.receiveLineContent = (
            bookId: number,
            lineIndex: number,
            content: string | null
        ) => {
            const key = `GetLineContent:${bookId}:${lineIndex}`;
            const r = pendingRequests.get(key);
            if (r) {
                r.resolve(content);
                pendingRequests.delete(key);
            }
        };

        win.receivePdfFilePath = (
            virtualUrl: string | null,
            fileName: string | null,
            originalPath: string | null
        ) => {
            const r = pendingRequests.get("OpenPdfFilePicker");
            if (r) {
                r.resolve({ fileName, dataUrl: virtualUrl, originalPath });
                pendingRequests.delete("OpenPdfFilePicker");
            }
        };
    }

    /* ---------------- public API ---------------- */

    function send(command: string, args: any[]): void {
        setupGlobalHandlers();

        if (!isAvailable()) return;

        (window as any).chrome.webview.postMessage({ command, args });
    }

    function createRequest<T>(requestId: string): Promise<T> {
        setupGlobalHandlers();

        return new Promise((resolve, reject) => {
            pendingRequests.set(requestId, { resolve, reject });
        });
    }

    /* ---------------- exposed object ---------------- */

    return {
        send,
        createRequest,
        isAvailable,
    };
}

