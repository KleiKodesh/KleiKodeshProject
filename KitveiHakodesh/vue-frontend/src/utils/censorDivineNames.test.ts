import { describe, it, expect } from 'vitest'
import {
  censorDivineNames,
  normalizeDivineNameMode,
  normalizeElokimMode,
  normalizeOtherNamesMode,
  DEFAULT_DIVINE_NAME_MODE,
  DEFAULT_ELOKIM_MODE,
  DEFAULT_OTHER_NAMES_MODE,
} from './censorDivineNames'

// Vocalized tetragrammaton variants. Written with explicit escapes where the
// combining marks matter, so the expectations stay readable in any editor.
const YHWH_SHEVA_KAMATZ = 'יְה' + 'וָה'        // יְהוָה
const YHWH_WITH_HOLAM = 'יְהֹ' + 'וָה'    // יְהֹוָה
const YHWH_WITH_TEAM = 'יְה' + 'וָ֖ה'     // יְהוָ֖ה  (tipcha on the ו group)

/**
 * U+2011 NON-BREAKING HYPHEN — the separator inside censored names. A plain
 * hyphen-minus would let the browser wrap a divine name across two lines.
 */
const NBHY = '‑'

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
  // tetragrammaton rendering varies. The separator is U+2011, not a plain hyphen.
  const OTHERS: [string, string][] = [
    ['אֲדֹנָי', 'אֲדֹנָ' + NBHY + 'י'],
    ['אֱלֹהִים', 'אֱ' + NBHY + 'לֹהִים'],
    ['שַׁדַּי', 'שַׁ' + NBHY + 'דַּי'],
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

describe('the separator is non-breaking', () => {
  // A plain hyphen-minus (U+002D) is a line-break opportunity, so a censored
  // name could be split across two lines. Every rule must emit U+2011 instead.
  // One sample per dash-emitting rule:
  const SAMPLES = [
    'יָהּ',      // יה
    'אֲדֹנָי',   // אדני  (the string-replacement rule, easy to miss)
    'אֱלֹהִים',  // אלהים
    'אֱלוֹהִים', // אלוהים
    'אֱלֹהֵי',   // אלהי
    'אֱלוֹהַ',   // אלוה
    'אֵל',       // אל
    'וְאֵל',     // אל with a prefix
    'שַׁדַּי',   // שדי
  ]

  for (const mode of ['yudDaled', 'yudKuf', 'doubleYud', 'heApostrophe'] as const) {
    it(`never emits a plain hyphen in mode ${mode}`, () => {
      for (const sample of SAMPLES) {
        const out = censorDivineNames(sample, mode)
        expect(out, `${sample} → ${out}`).not.toContain('-')
        expect(out, `${sample} → ${out}`).toContain(NBHY)
      }
    })
  }

  it('renders the same glyph a reader would expect from a hyphen', () => {
    // Sanity check on the codepoint itself, so a stray edit to SEP is caught.
    expect(NBHY.codePointAt(0)).toBe(0x2011)
  })
})

describe('elokim mode (the אלהים family)', () => {
  // Each entry: source, hyphen result, kuf result, daled result.
  const FAMILY: [string, string, string, string][] = [
    ['אֱלֹהִים', 'אֱ' + NBHY + 'לֹהִים', 'אֱלֹקִים', 'אֱלֹדִים'],
    ['אֱלוֹהִים', 'אֱ' + NBHY + 'לוֹהִים', 'אֱלוֹקִים', 'אֱלוֹדִים'],
    ['אֱלֹהֵי', 'אֱ' + NBHY + 'לֹהֵי', 'אֱלֹקֵי', 'אֱלֹדֵי'],
    ['אֱלוֹהַ', 'אֱ' + NBHY + 'לוֹהַ', 'אֱלוֹקַ', 'אֱלוֹדַ'],
  ]

  it('separates with a non-breaking hyphen in hyphen mode', () => {
    for (const [source, hyphen] of FAMILY) {
      expect(censorDivineNames(source, { elokim: 'hyphen' })).toBe(hyphen)
    }
  })

  it('swaps the ה for ק in kuf mode, keeping points and te\'amim in place', () => {
    for (const [source, , kuf] of FAMILY) {
      expect(censorDivineNames(source, { elokim: 'kuf' })).toBe(kuf)
    }
  })

  it('swaps the ה for ד in daled mode', () => {
    for (const [source, , , daled] of FAMILY) {
      expect(censorDivineNames(source, { elokim: 'daled' })).toBe(daled)
    }
  })

  it('inserts no separator when substituting a letter', () => {
    for (const mode of ['kuf', 'daled'] as const) {
      for (const [source] of FAMILY) {
        expect(censorDivineNames(source, { elokim: mode })).not.toContain(NBHY)
      }
    }
  })

  it('still exempts אלהים אחרים under substitution', () => {
    const text = 'אֱלֹהִים אחרים'
    for (const mode of ['hyphen', 'kuf', 'daled'] as const) {
      expect(censorDivineNames(text, { elokim: mode })).toBe(text)
    }
  })

  it('defaults to hyphen', () => {
    expect(DEFAULT_ELOKIM_MODE).toBe('hyphen')
    expect(censorDivineNames('אֱלֹהִים')).toBe('אֱ' + NBHY + 'לֹהִים')
  })

  it('does not reach יה, which always hyphenates', () => {
    for (const mode of ['hyphen', 'kuf', 'daled'] as const) {
      expect(censorDivineNames('יָהּ', { elokim: mode })).toBe('יָ' + NBHY + 'הּ')
    }
  })
})

describe('otherNames mode (אדני, אל, שדי — no ה to swap)', () => {
  const NO_HE: [string, string][] = [
    ['אֲדֹנָי', 'אֲדֹנָ' + NBHY + 'י'],
    ['אֵל', 'אֵ' + NBHY + 'ל'],
    ['שַׁדַּי', 'שַׁ' + NBHY + 'דַּי'],
  ]

  it('separates them in hyphen mode', () => {
    for (const [source, expected] of NO_HE) {
      expect(censorDivineNames(source, { otherNames: 'hyphen' })).toBe(expected)
    }
  })

  it('leaves them fully uncensored in none mode', () => {
    for (const [source] of NO_HE) {
      expect(censorDivineNames(source, { otherNames: 'none' })).toBe(source)
    }
  })

  it('is independent of the elokim setting', () => {
    // אלהים substituted, but the no-ה names left alone.
    const out = censorDivineNames('אֱלֹהִים אֲדֹנָי', { elokim: 'kuf', otherNames: 'none' })
    expect(out).toBe('אֱלֹקִים אֲדֹנָי')
  })

  it('defaults to hyphen', () => {
    expect(DEFAULT_OTHER_NAMES_MODE).toBe('hyphen')
    expect(censorDivineNames('אֵל')).toBe('אֵ' + NBHY + 'ל')
  })
})

describe('the three settings compose', () => {
  const SENTENCE = 'וַיֹּאמֶר יְהוָה אֱלֹהִים אֲדֹנָי שַׁדַּי אֵל יָהּ'

  it('applies each group according to its own setting', () => {
    const out = censorDivineNames(SENTENCE, {
      mode: 'yudKuf',
      elokim: 'kuf',
      otherNames: 'none',
    })
    expect(out).toContain('יְקוָק')      // tetragrammaton → יקוק
    expect(out).toContain('אֱלֹקִים')    // אלהים → אלקים
    expect(out).toContain('אֲדֹנָי')     // untouched
    expect(out).toContain('שַׁדַּי')     // untouched
    expect(out).toContain('יָ' + NBHY + 'הּ') // יה always hyphenated
  })

  it("mode 'none' is the master off switch, overriding the other two", () => {
    expect(
      censorDivineNames(SENTENCE, { mode: 'none', elokim: 'kuf', otherNames: 'hyphen' }),
    ).toBe(SENTENCE)
  })

  it('a bare mode string still works and uses the group defaults', () => {
    expect(censorDivineNames(SENTENCE, 'yudDaled')).toBe(censorDivineNames(SENTENCE))
  })
})

describe('normalizeElokimMode / normalizeOtherNamesMode', () => {
  it('passes valid values through', () => {
    expect(normalizeElokimMode('hyphen')).toBe('hyphen')
    expect(normalizeElokimMode('kuf')).toBe('kuf')
    expect(normalizeElokimMode('daled')).toBe('daled')
    expect(normalizeOtherNamesMode('hyphen')).toBe('hyphen')
    expect(normalizeOtherNamesMode('none')).toBe('none')
  })

  it('rejects unknown values so the caller keeps its default', () => {
    for (const bad of ['bogus', '', null, undefined, true, 3]) {
      expect(normalizeElokimMode(bad)).toBeNull()
      expect(normalizeOtherNamesMode(bad)).toBeNull()
    }
    // Cross-contamination guard: these enums do not share all their values.
    expect(normalizeElokimMode('none')).toBeNull()
    expect(normalizeOtherNamesMode('kuf')).toBeNull()
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
