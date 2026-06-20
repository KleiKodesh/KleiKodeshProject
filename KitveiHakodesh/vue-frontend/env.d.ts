/// <reference types="vite/client" />

interface Window {
  chrome?: {
    webview: {
      postMessage(message: unknown): void
      addEventListener(event: string, handler: (event: MessageEvent) => void): void
      removeEventListener(event: string, handler: (event: MessageEvent) => void): void
    }
  }
}
