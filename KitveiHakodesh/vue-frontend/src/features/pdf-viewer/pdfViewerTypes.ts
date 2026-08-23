export type OcrScript = 'hebrew' | 'rashi' | 'mixed' | 'english'

export interface OcrSelectionResult {
  text: string
  isOcr: boolean
}
