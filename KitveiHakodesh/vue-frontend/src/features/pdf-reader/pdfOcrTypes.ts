export type OcrScript = 'hebrew' | 'rashi' | 'mixed' | 'english'

export interface OcrSelectionResult {
  text: string
  isOcr: boolean
  /** Recognised with force on (the image read even where the PDF has text). */
  forced?: boolean
}
