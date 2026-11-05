import { documentIcon, iconKeyForFileName, type DocumentIcon } from '@/utils/documentIcons'
import type { LocalFileItem } from './bookCatalogTree'

/** The icon a computer file shows in the catalog views - the same one file search gives it. */
export function fileIcon(file: LocalFileItem): DocumentIcon {
  return documentIcon(iconKeyForFileName(file.name))
}

/** A computer file's title: its name without the extension. */
export function fileTitle(file: LocalFileItem): string {
  if (file.title) return file.title
  const dot = file.name.lastIndexOf('.')
  return dot > 0 ? file.name.substring(0, dot) : file.name
}

/** The folder a computer file sits in - a file search result's second line. */
export function fileFolder(file: LocalFileItem): string {
  const cut = Math.max(file.path.lastIndexOf('\\'), file.path.lastIndexOf('/'))
  return cut > 0 ? file.path.substring(0, cut) : ''
}
