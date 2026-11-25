import { describe, expect, it } from 'vitest'
import { embeddedFontsOf, withoutEmbeddedFonts } from './useFastDocViewEmbeddedFonts'

// the library's rules (FastDocView Docx/DocxEmbeddedFonts.cs StylesheetRules)
const SHEET = `@font-face { font-family: 'Gabriola'; font-weight: 700; font-style: normal; src: local('Gabriola Bold'), url("fdv-font:o%3Aword%2Ffonts%2Ffont3.odttf"); }
@font-face { font-family: 'Calibri'; font-weight: 400; font-style: italic; src: local('Calibri Italic'), url("fdv-font:o%3Aword%2Ffonts%2Ffont1.odttf"); }
.word-document { font-family: 'Calibri'; font-size: 11pt; }`

describe('embedded fonts in the library stylesheet', () => {
  it('reads each face with its picture id', () => {
    expect(embeddedFontsOf(SHEET)).toEqual([
      { family: 'Gabriola', weight: '700', style: 'normal', localName: 'Gabriola Bold', pictureId: 'o:word/fonts/font3.odttf' },
      { family: 'Calibri', weight: '400', style: 'italic', localName: 'Calibri Italic', pictureId: 'o:word/fonts/font1.odttf' },
    ])
  })
  it('leaves the rest of the sheet for the scoped style tag', () => {
    expect(withoutEmbeddedFonts(SHEET)).toBe(".word-document { font-family: 'Calibri'; font-size: 11pt; }")
    expect(embeddedFontsOf('.word-document { color: red }')).toEqual([])
  })
})
