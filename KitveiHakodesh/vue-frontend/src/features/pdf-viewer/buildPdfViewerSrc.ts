import { uiLanguage } from '@/i18n'

/**
 * The PDF.js viewer iframe src for one file - ONE implementation shared by the PDF tab
 * (PdfViewPage) and the book view's side-by-side HebrewBooks edition, so both load the
 * viewer with the same parameters.
 *
 * `outlineHash` is the one-shot outline jump (`#khOutline=...`, see outlineJumpHash);
 * viewer.mjs strips it and jumps once the outline is in.
 */
export function buildPdfViewerSrc(fileUrl: string, fileName?: string | null, outlineHash?: string | null): string {
  const p = new URLSearchParams({ file: fileUrl, locale: uiLanguage === 'en' ? 'en-US' : 'he', cMapPacked: 'true' })
  if (fileName) p.set('filename', encodeURIComponent(fileName))
  return `/pdfjs/web/viewer.html?${p}${outlineHash ?? ''}`
}

/** The hash that makes the viewer jump to the outline entry matching `outlineTarget`. */
export function outlineJumpHash(outlineTarget: string): string {
  return `#khOutline=${encodeURIComponent(outlineTarget)}`
}
