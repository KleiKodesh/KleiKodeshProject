import type { SenseRow, DictLink, MetzudatRow, MenchemRow, AruchRow, MicropediaRow } from '@/webview-host/dictionaryDb'

export interface WordPageData {
  headword:               string
  senses:                 SenseRow[]
  radak:                  SenseRow[]
  metzudat:               MetzudatRow[]
  malbim:                 MetzudatRow[]
  menchemRows:            MenchemRow[]
  micropediaRows:         MicropediaRow[]
  aruchRows:              AruchRow[]
  links:                  DictLink[]
  synonyms:               string[]
  variants:               string[]
  ketivSuggestions:       string[]
  levenshteinSuggestions: string[]
}
