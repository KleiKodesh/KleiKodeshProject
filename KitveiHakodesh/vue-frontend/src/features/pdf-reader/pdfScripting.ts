import type { EventBus } from 'pdfjs-dist/web/pdf_viewer.mjs'

/**
 * Form scripts (calculated / formatted / validated fields) for one viewer, as PDF.js's
 * PDFScriptingManager `externalServices`.
 *
 * Security: the scripts run inside QuickJS compiled to wasm (PDF.js's sandbox) - a separate
 * interpreter with no access to the page's globals or the app's host bridge. The only way out is
 * the window stand-in below: timers, alert/confirm/prompt, URL parsing and field updates.
 *
 * Per viewer: PDF.js's own GenericScripting hands the sandbox the global window, so every viewer's
 * field updates arrive as one window event that every open PDF listens to (two tabs with the same
 * field ids would update each other) and alerts open in the main window. Here each viewer's sandbox
 * gets its own stand-in: updates go to this viewer's eventBus only, timers and dialogs use the
 * window that shows the PDF.
 */
export interface PdfScriptDialogs {
  /** The app's modal (non-blocking: a script's alert does not wait for OK). */
  alert: (message: string) => void
}

/** build/pdf.sandbox.mjs with its Sandbox class exported (vite.config.ts pdfjsResourcesPlugin). */
interface PdfSandbox {
  create(data: unknown): void
  dispatchEvent(event: unknown): void
  nukeSandbox(): void
}
type SandboxClass = new (win: object, module: unknown) => PdfSandbox

let sandboxClass: Promise<SandboxClass> | null = null

export function createPdfScriptingServices(opts: {
  eventBus: EventBus
  hostWindow: Window
  resources: string
  dialogs: PdfScriptDialogs
}) {
  const { eventBus, resources, dialogs } = opts
  // A real window (the main one or a detached one): it has its own globals.
  const hostWindow = opts.hostWindow as Window & typeof globalThis
  const abs = (rel: string) => new URL(`${resources}${rel}`, hostWindow.location.href).href

  function createScripting() {
    // This sandbox's own event target: its field updates reach only this viewer.
    const updates = new hostWindow.EventTarget()
    updates.addEventListener('updatefromsandbox', (e: Event) => {
      // PDFScriptingManager accepts updates whose source is the global window.
      eventBus.dispatch('updatefromsandbox', { source: globalThis, detail: (e as CustomEvent).detail })
    })
    const win = Object.freeze({
      setTimeout: hostWindow.setTimeout.bind(hostWindow),
      clearTimeout: hostWindow.clearTimeout.bind(hostWindow),
      setInterval: hostWindow.setInterval.bind(hostWindow),
      clearInterval: hostWindow.clearInterval.bind(hostWindow),
      console: hostWindow.console,
      URL: hostWindow.URL,
      CustomEvent: hostWindow.CustomEvent,
      dispatchEvent: (e: Event) => updates.dispatchEvent(e),
      alert: (message: string) => dialogs.alert(message),
      // The sandbox needs the answer synchronously, which an app dialog cannot give: the native
      // dialogs, in the window that shows the PDF.
      confirm: (message: string) => hostWindow.confirm(message),
      prompt: (question: string, def: string) => hostWindow.prompt(question, def),
    })
    const ready = (async () => {
      const Sandbox = await (sandboxClass ??= import(/* @vite-ignore */ abs('build/pdf.sandbox.kh.mjs')).then(
        (m: { KhSandbox: SandboxClass }) => m.KhSandbox,
      ))
      const { default: loadModule } = (await import(/* @vite-ignore */ abs('wasm/quickjs-eval.js'))) as {
        default: () => Promise<unknown>
      }
      return new Sandbox(win, await loadModule())
    })()
    return {
      async createSandbox(data: unknown) {
        ;(await ready).create(data)
      },
      async dispatchEventInSandbox(event: unknown) {
        const sandbox = await ready
        hostWindow.setTimeout(() => sandbox.dispatchEvent(event), 0)
      },
      async destroySandbox() {
        ;(await ready).nukeSandbox()
      },
    }
  }

  return { addScriptingWindowListener: false, createScripting }
}
