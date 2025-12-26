export enum SearchMode {
  All = 'All',
  Forward = 'Forward',
  Back = 'Back',
  Selection = 'Selection'
}

export interface SearchResult {
  start: number
  end: number
  before: string
  after: string
  text: string
}

export interface RegexFindBase {
  text: string
  bold: boolean
  italic: boolean
  underline: boolean
  superscript: boolean
  subscript: boolean
  style: string
  font: string
  fontSize?: number
}

export interface RegexFindOptions extends RegexFindBase {
  mode: SearchMode
  slop: number
  useWildcards: boolean
  replace: RegexFindBase
}

export interface RegexFindState {
  options: RegexFindOptions
  results: SearchResult[]
  currentIndex: number
  isSearching: boolean
}