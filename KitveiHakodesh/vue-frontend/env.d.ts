/// <reference types="vite/client" />

interface Window {
  chrome?: {
    webview: {
      postMessage(message: unknown): void
      addEventListener(event: string, handler: (event: MessageEvent) => void): void
      removeEventListener(event: string, handler: (event: MessageEvent) => void): void
    }
  }
  __webviewDbPath?: string
  __webviewDbReady?: boolean
  __webviewShowPopOut?: boolean
  /** Injected by the C# host to seed the default workspace ID on first launch. */
  __webviewDefaultWorkspaceId?: string
}
