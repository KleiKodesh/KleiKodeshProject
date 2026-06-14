export type OcrScript = 'hebrew' | 'rashi' | 'mixed'

export interface OcrSelectionResult {
  text: string
  isOcr: boolean
}
