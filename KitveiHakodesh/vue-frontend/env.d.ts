/// <reference types="vite/client" />

interface Window {
  chrome?: {
    webview: {
      postMessage(message: unknown): void
      addEventListener(event: string, handler: (event: MessageEvent) => void): void
      removeEventListener(event: string, handler: (event: MessageEvent) => void): void
    }
  }
  /** Injected by C# on startup — persisted dark mode from registry. Used by themeStore
   *  to sync Vue's theme to the title bar on startup, so both always agree regardless
   *  of which host last saved the setting. */
  __webviewIsDark?: boolean
}
