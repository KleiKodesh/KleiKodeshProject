import { describe, it, expect } from 'vitest'
import {
  censorDivineNames,
  normalizeDivineNameMode,
  DEFAULT_DIVINE_NAME_MODE,
} from './censorDivineNames'

// Vocalized tetragrammaton variants. Written with explicit escapes where the
// combining marks matter, so the expectations stay readable in any editor.
const YHWH_SHEVA_KAMATZ = 'יְה' + 'וָה'        // יְהוָה
const YHWH_WITH_HOLAM = 'יְהֹ' + 'וָה'    // יְהֹוָה
const YHWH_WITH_TEAM = 'יְה' + 'וָ֖ה'     // יְהוָ֖ה  (tipcha on the ו group)

describe('mode: yudDaled', () => {
  it('replaces both ה with ד keeping every mark in place', () => {
    expect(censorDivineNames(YHWH_SHEVA_KAMATZ, 'yudDaled')).toBe('יְד' + 'וָד')
    expect(censorDivineNames(YHWH_WITH_HOLAM, 'yudDaled')).toBe('יְדֹ' + 'וָד')
  })

  it('is the default when no mode is passed (legacy call signature)', () => {
    expect(censorDivineNames(YHWH_SHEVA_KAMATZ)).toBe(
      censorDivineNames(YHWH_SHEVA_KAMATZ, 'yudDaled'),
    )
    expect(DEFAULT_DIVINE_NAME_MODE).toBe('yudDaled')
  })
})

describe('mode: yudKuf', () => {
  it('replaces both ה with ק keeping every mark in place', () => {
    expect(censorDivineNames(YHWH_SHEVA_KAMATZ, 'yudKuf')).toBe('יְק' + 'וָק')
    expect(censorDivineNames(YHWH_WITH_TEAM, 'yudKuf')).toBe('יְק' + 'וָ֖ק')
  })
})

describe('mode: doubleYud', () => {
  it('drops both ה and moves the ו nikkud onto the second י', () => {
    // יְהוָה → יְיָ : the kamatz that sat under the ו now sits under the second י.
    expect(censorDivineNames(YHWH_SHEVA_KAMATZ, 'doubleYud')).toBe('יְ' + 'יָ')
  })

  it('discards the holam belonging to the dropped ה', () => {
    // The ה's own vowel cannot survive — its letter is gone.
    expect(censorDivineNames(YHWH_WITH_HOLAM, 'doubleYud')).toBe('יְ' + 'יָ')
  })

  it('preserves cantillation marks', () => {
    // The tipcha rides along with the ו's marks onto the second י.
    expect(censorDivineNames(YHWH_WITH_TEAM, 'doubleYud')).toBe('יְ' + 'יָ֖')
  })

  it('handles the unvocalized name', () => {
    expect(censorDivineNames('יהוה', 'doubleYud')).toBe('יי')
  })

  it('keeps a preceding prefix untouched', () => {
    expect(censorDivineNames('וַי' + YHWH_WITH_TEAM.slice(2), 'doubleYud')).toContain('וַ')
  })
})

describe("mode: heApostrophe", () => {
  it("collapses the name to ה' discarding all nikkud", () => {
    expect(censorDivineNames(YHWH_SHEVA_KAMATZ, 'heApostrophe')).toBe("ה'")
    expect(censorDivineNames(YHWH_WITH_HOLAM, 'heApostrophe')).toBe("ה'")
    expect(censorDivineNames('יהוה', 'heApostrophe')).toBe("ה'")
  })

  it("preserves cantillation, placing it on the ה before the apostrophe", () => {
    // Tipcha (U+0596) survives; the sheva and kamatz do not.
    expect(censorDivineNames(YHWH_WITH_TEAM, 'heApostrophe')).toBe('ה֖' + "'")
  })

  it('keeps a vocalized prefix untouched while the name itself goes bare', () => {
    // וְלַיהוָ֗ה → וְלַה֗' — the prefix keeps its points, the name keeps only the revia.
    expect(censorDivineNames('וְלַיהוָ֗ה', 'heApostrophe')).toBe('וְלַה֗' + "'")
  })
})

describe('mode: none', () => {
  it('returns the text completely untouched', () => {
    const sources = [YHWH_SHEVA_KAMATZ, YHWH_WITH_TEAM, 'אֲדֹנָי', 'אֱלֹהִים']
    for (const source of sources) {
      expect(censorDivineNames(source, 'none')).toBe(source)
    }
  })
})

describe('the other divine names', () => {
  // These are censored identically in every mode except 'none' — only the
  // tetragrammaton rendering varies.
  const OTHERS: [string, string][] = [
    ['אֲדֹנָי', 'אֲדֹנָ-י'],   // אֲדֹנָי → אֲדֹנָ-י
    ['אֱלֹהִים', 'אֱ-לֹהִים'], // אֱלֹהִים → אֱ-לֹהִים
    ['שַׁדַּי', 'שַׁ-דַּי'], // שַׁדַּי → שַׁ-דַּי
  ]

  for (const mode of ['yudDaled', 'yudKuf', 'doubleYud', 'heApostrophe'] as const) {
    it(`applies the dash treatment in mode ${mode}`, () => {
      for (const [source, expected] of OTHERS) {
        expect(censorDivineNames(source, mode)).toBe(expected)
      }
    })
  }

  it('leaves אלהים אחרים uncensored', () => {
    const text = 'אֱלֹהִים אחרים'
    expect(censorDivineNames(text, 'yudDaled')).toBe(text)
  })
})

describe('normalizeDivineNameMode', () => {
  it('migrates the legacy boolean setting', () => {
    expect(normalizeDivineNameMode(true)).toBe('yudDaled')
    expect(normalizeDivineNameMode(false)).toBe('none')
  })

  it('passes valid mode strings through', () => {
    expect(normalizeDivineNameMode('yudKuf')).toBe('yudKuf')
    expect(normalizeDivineNameMode('doubleYud')).toBe('doubleYud')
    expect(normalizeDivineNameMode('heApostrophe')).toBe('heApostrophe')
    expect(normalizeDivineNameMode('none')).toBe('none')
  })

  it('rejects unknown values so the caller keeps its default', () => {
    expect(normalizeDivineNameMode('bogus')).toBeNull()
    expect(normalizeDivineNameMode(null)).toBeNull()
    expect(normalizeDivineNameMode(undefined)).toBeNull()
    expect(normalizeDivineNameMode(3)).toBeNull()
  })
})

describe('repeated calls (shared module-level regex state)', () => {
  it('produces identical output across successive calls', () => {
    const text = `${YHWH_SHEVA_KAMATZ} ${YHWH_SHEVA_KAMATZ} ${YHWH_SHEVA_KAMATZ}`
    const first = censorDivineNames(text, 'doubleYud')
    expect(censorDivineNames(text, 'doubleYud')).toBe(first)
    expect(censorDivineNames(text, 'doubleYud')).toBe(first)
  })

  it('censors every occurrence in a longer string', () => {
    const out = censorDivineNames(`${YHWH_SHEVA_KAMATZ} א ${YHWH_SHEVA_KAMATZ}`, 'yudKuf')
    expect(out.match(/ק/g)?.length).toBe(4)
  })
})
