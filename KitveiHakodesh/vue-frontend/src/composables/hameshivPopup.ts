/**
 * hameshiv.com refuses to be iframed, so it opens in a child popup window.
 * The size here is the first-open default (and dev's only size: a browser can't read a
 * cross-origin popup's size). Hosted, AppViewerPopup.cs owns the window — it persists
 * the size the user leaves it at, centres it and makes it a child of the app/Word.
 */
const WIDTH = 390
const HEIGHT = 440

export function openHameshivPopup(): void {
  const left = Math.round(((screen as Screen & { availLeft?: number }).availLeft ?? 0) + (screen.availWidth - WIDTH) / 2)
  const top = Math.round(((screen as Screen & { availTop?: number }).availTop ?? 0) + (screen.availHeight - HEIGHT) / 2)
  window.open('https://hameshiv.com/', 'hameshiv', `popup,width=${WIDTH},height=${HEIGHT},left=${left},top=${top}`)
}
