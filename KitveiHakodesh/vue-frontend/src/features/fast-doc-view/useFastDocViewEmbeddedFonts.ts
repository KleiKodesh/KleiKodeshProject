/**
 * Fonts embedded in a document (FastDocView Docx/DocxEmbeddedFonts.cs): the library's stylesheet starts with one @font-face
 * rule per embedded face - the installed font first (local()), the embedded data by its picture id (`url("fdv-font:<id>")`).
 * A @font-face rule cannot live in the page's `@scope` block, and its URL is the service's, so the rules are taken out of the
 * stylesheet here and each face is registered with the FontFace API instead - only when the font is not installed (as Word
 * draws an embedded font only on a machine without it): the data is read then, after the text is shown, never before.
 */
import { onScopeDispose, watch } from 'vue'
import type { FastDocViewApi } from '@/webview-host/fastDocViewApi'

const FONT_FACE_RULE = /@font-face \{ font-family: '([^']+)'; font-weight: (\d+); font-style: (\w+); src: local\('([^']+)'\), url\("fdv-font:([^"]+)"\); \}\n?/g

export type FastDocViewEmbeddedFont = { family: string; weight: string; style: string; localName: string; pictureId: string }

/** The embedded faces a library stylesheet declares. */
export function embeddedFontsOf(stylesheet: string): FastDocViewEmbeddedFont[] {
  return [...stylesheet.matchAll(FONT_FACE_RULE)].map(([, family, weight, style, localName, id]) =>
    ({ family: family!, weight: weight!, style: style!, localName: localName!, pictureId: decodeURIComponent(id!) }))
}

/** The stylesheet without its @font-face rules (they are registered by useFastDocViewEmbeddedFonts). */
export function withoutEmbeddedFonts(stylesheet: string): string {
  return stylesheet.replace(FONT_FACE_RULE, '')
}

async function isInstalled(font: FastDocViewEmbeddedFont): Promise<boolean> {
  try {
    await new FontFace(font.family, `local("${font.localName}")`, { weight: font.weight, style: font.style }).load()
    return true
  } catch {
    return false
  }
}

export function useFastDocViewEmbeddedFonts(
  api: Pick<FastDocViewApi, 'pictureSource'>,
  documentId: () => string | undefined,
  stylesheet: () => string | undefined,
  fontSet: () => FontFaceSet | undefined = () => (typeof document === 'undefined' ? undefined : document.fonts),
) {
  let added: { set: FontFaceSet; face: FontFace; source: string }[] = []
  let generation = 0

  function clear() {
    for (const { set, face, source } of added) {
      set.delete(face)
      if (source.startsWith('blob:')) URL.revokeObjectURL(source)
    }
    added = []
  }

  watch([documentId, stylesheet], async ([id, css]) => {
    const current = ++generation
    clear()
    const fonts = id && css ? embeddedFontsOf(css) : []
    const set = fontSet()
    if (!set || fonts.length === 0 || typeof FontFace === 'undefined') return
    for (const font of fonts) {
      if (await isInstalled(font)) continue
      if (current !== generation) return
      let source: string | null = null
      try { source = await api.pictureSource(id!, font.pictureId) } catch { source = null }
      if (!source) continue
      if (current !== generation) { if (source.startsWith('blob:')) URL.revokeObjectURL(source); return }
      const face = new FontFace(font.family, `url("${source}")`, { weight: font.weight, style: font.style })
      set.add(face)
      added.push({ set, face, source })
      face.load().catch(() => console.warn(`[fast-doc-view] embedded font ${font.family} could not be read`))
    }
  }, { immediate: true })

  onScopeDispose(() => { generation++; clear() })
}
