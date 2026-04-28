import type { SenseRow, DictLink, MetzudatRow, MenchemRow } from '@/host/dictionaryDb'

export interface WordPageData {
  headword:               string
  senses:                 SenseRow[]
  radak:                  SenseRow[]
  metzudat:               MetzudatRow[]
  malbim:                 MetzudatRow[]
  menchemRows:            MenchemRow[]
  links:                  DictLink[]
  synonyms:               string[]
  variants:               string[]
  ketivSuggestions:       string[]
  levenshteinSuggestions: string[]
}
