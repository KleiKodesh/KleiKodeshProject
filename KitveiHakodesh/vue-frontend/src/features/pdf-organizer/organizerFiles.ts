import type { HostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * Where the organizer's output goes: files the user picks (Save As dialog, a folder) - never
 * anywhere of the app's own. Where the browser has no picker, the files download instead.
 */

type SavePicker = (o: unknown) => Promise<FileSystemFileHandle>
type DirectoryPicker = (o?: unknown) => Promise<FileSystemDirectoryHandle>

function download(hostWindow: HostWindow, blob: Blob, name: string) {
  const url = URL.createObjectURL(blob)
  const a = hostWindow.document.createElement('a')
  a.href = url
  a.download = name
  hostWindow.document.body.append(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/** Save As dialog for one PDF. False when cancelled. */
export async function saveBlobAs(hostWindow: HostWindow, blob: Blob, suggestedName: string): Promise<boolean> {
  const picker = (hostWindow as unknown as { showSaveFilePicker?: SavePicker }).showSaveFilePicker
  if (!picker) {
    download(hostWindow, blob, suggestedName)
    return true
  }
  let handle: FileSystemFileHandle
  try {
    handle = await picker.call(hostWindow, { suggestedName, types: [{ description: 'PDF', accept: { 'application/pdf': ['.pdf'] } }] })
  } catch {
    return false // cancelled
  }
  const writable = await handle.createWritable()
  await writable.write(blob)
  await writable.close()
  return true
}

/**
 * A place for several files: a folder the user picks, or (no folder picker) the downloads.
 * Null when cancelled.
 */
export async function pickOutputFolder(hostWindow: HostWindow): Promise<((name: string, blob: Blob) => Promise<void>) | null> {
  const picker = (hostWindow as unknown as { showDirectoryPicker?: DirectoryPicker }).showDirectoryPicker
  if (!picker) return async (name, blob) => download(hostWindow, blob, name)
  let dir: FileSystemDirectoryHandle
  try {
    dir = await picker.call(hostWindow, { mode: 'readwrite' })
  } catch {
    return null
  }
  return async (name, blob) => {
    const handle = await dir.getFileHandle(name, { create: true })
    const writable = await handle.createWritable()
    await writable.write(blob)
    await writable.close()
  }
}

export function pdfBlob(bytes: Uint8Array): Blob {
  return new Blob([bytes as BlobPart], { type: 'application/pdf' })
}

/** "book.pdf" -> "book" (for "book - pages 3-5.pdf" and the like). */
export function baseName(fileName: string | null): string {
  return (fileName || 'document').replace(/\.pdf$/i, '')
}
