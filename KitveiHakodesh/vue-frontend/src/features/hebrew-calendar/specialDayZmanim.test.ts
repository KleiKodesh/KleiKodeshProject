import { describe, expect, it } from 'vitest'
import { SPECIAL_ZMAN_NAMES } from 'kleikodesh-zmanim'
import en from '@/i18n/locales/en/hebrewCalendar.json'
import he from '@/i18n/locales/he/hebrewCalendar.json'
import { specialKindsFor } from './specialDayZmanim'

describe('special-day zmanim', () => {
  for (const [lang, file] of [['en', en], ['he', he]] as const) {
    it(`every special zman has a ${lang} label`, () => {
      const labels = (file as { specialZmanim?: Record<string, string> }).specialZmanim ?? {}
      expect(SPECIAL_ZMAN_NAMES.filter((n) => !labels[n]?.trim())).toEqual([])
    })
  }
  it('erev Pesach 5786 (1 Apr 2026)', () => expect(specialKindsFor(new Date(2026, 3, 1), true)).toContain('erevPesach'))
  it('17 Tammuz 5786 (2 Jul 2026) is a fast', () => expect(specialKindsFor(new Date(2026, 6, 2), true)).toContain('fastDay'))
  it('kiddush levana window: molad 22 Sep 2025, 30 Sep in, 20 Sep out', () => {
    expect(specialKindsFor(new Date(2025, 8, 30), true)).toContain('kiddushLevana')
    expect(specialKindsFor(new Date(2025, 8, 20), true)).not.toContain('kiddushLevana')
    expect(specialKindsFor(new Date(2025, 8, 24), true)).toEqual([])
  })
})
