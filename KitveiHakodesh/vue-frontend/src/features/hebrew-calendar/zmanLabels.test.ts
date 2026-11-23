import { describe, expect, it } from 'vitest'
import { ZMAN_NAMES } from 'kleikodesh-zmanim'
import en from '@/i18n/locales/en/hebrewCalendar.json'
import he from '@/i18n/locales/he/hebrewCalendar.json'

// Every library zman shows in the lists, so each needs a label in both UI languages.
describe('zman labels', () => {
  for (const [lang, file] of [['en', en], ['he', he]] as const) {
    it(`every library zman has a ${lang} label`, () => {
      const labels = file.zmanim as Record<string, string | undefined>
      expect(ZMAN_NAMES.filter((name) => !labels[name]?.trim())).toEqual([])
      expect(file.zmanimNotReached).toBeTruthy()
    })
  }
})
