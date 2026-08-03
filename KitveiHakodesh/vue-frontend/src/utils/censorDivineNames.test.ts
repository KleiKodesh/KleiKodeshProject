import { describe, it, expect } from 'vitest'
import {
  censorDivineNames,
  normalizeDivineNameMode,
  normalizeElokimMode,
  normalizeOtherNamesSelected,
  DEFAULT_DIVINE_NAME_MODE,
  DEFAULT_ELOKIM_MODE,
  DEFAULT_OTHER_NAMES_SELECTED,
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

describe('mode: hyphen (י‑ה‑ו‑ה)', () => {
  it('separates all four letters, keeping each mark on its own letter', () => {
    expect(censorDivineNames('יהוה', 'hyphen')).toBe(
      ['י', 'ה', 'ו', 'ה'].join(NBHY),
    )
    // יְהוָה → יְ‑ה‑וָ‑ה : the sheva stays on the י, the kamatz on the ו.
    expect(censorDivineNames(YHWH_SHEVA_KAMATZ, 'hyphen')).toBe(
      ['יְ', 'ה', 'וָ', 'ה'].join(NBHY),
    )
    // The holam stays on the ה that carried it.
    expect(censorDivineNames(YHWH_WITH_HOLAM, 'hyphen')).toBe(
      ['יְ', 'הֹ', 'וָ', 'ה'].join(NBHY),
    )
  })

  it('uses exactly three separators', () => {
    const out = censorDivineNames('יהוה', 'hyphen')
    expect([...out].filter((c) => c === NBHY)).toHaveLength(3)
  })

  it('preserves every letter and every mark', () => {
    const out = censorDivineNames(YHWH_WITH_TEAM, 'hyphen')
    const letters = (s: string) => [...s].filter((c) => c >= 'א' && c <= 'ת').join('')
    const marks = (s: string) => [...s].filter((c) => c >= '֑' && c <= 'ׇ').join('')
    expect(letters(out)).toBe('יהוה')
    expect(marks(out)).toBe(marks(YHWH_WITH_TEAM))
  })

  it('leaves a prefix outside the separated name', () => {
    expect(censorDivineNames('וְלַיהוָ֗ה', 'hyphen')).toBe(
      'וְלַ' + ['י', 'ה', 'וָ֗', 'ה'].join(NBHY),
    )
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

  for (const mode of ['yudDaled', 'yudKuf', 'doubleYud', 'heApostrophe', 'hyphen'] as const) {
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

  for (const mode of ['yudDaled', 'yudKuf', 'doubleYud', 'heApostrophe', 'hyphen'] as const) {
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
    for (const mode of ['hyphen', 'kuf', 'daled', 'none'] as const) {
      expect(censorDivineNames(text, { elokim: mode })).toBe(text)
    }
  })

  it('leaves the family fully uncensored in none mode', () => {
    for (const [source] of FAMILY) {
      expect(censorDivineNames(source, { elokim: 'none' })).toBe(source)
    }
  })

  it('none does not fall through to the daled substitution', () => {
    // The swap letter is chosen with a ternary, so 'none' must be caught first.
    expect(censorDivineNames('אֱלֹהִים', { elokim: 'none' })).not.toContain('ד')
  })

  it('none leaves the other groups censoring independently', () => {
    const out = censorDivineNames('יְהוָה אֱלֹהִים אֲדֹנָי שַׁדַּי יָהּ', {
      mode: 'yudDaled',
      elokim: 'none',
      otherNames: ['adnai', 'el', 'shadai', 'yah'],
    })
    expect(out).toContain('אֱלֹהִים')            // untouched
    expect(out).toContain('יְדוָד')              // tetragrammaton still censored
    expect(out).toContain('אֲדֹנָ' + NBHY + 'י') // אדני still censored
    expect(out).toContain('יָ' + NBHY + 'הּ')    // יה still censored
  })

  it('defaults to hyphen', () => {
    expect(DEFAULT_ELOKIM_MODE).toBe('hyphen')
    expect(censorDivineNames('אֱלֹהִים')).toBe('אֱ' + NBHY + 'לֹהִים')
  })

  it('is independent of יה, which has its own otherNames key', () => {
    // Including 'none' — turning off the אלהים family must not affect יה
    // (still selected by default via DEFAULT_OTHER_NAMES_SELECTED).
    for (const mode of ['hyphen', 'kuf', 'daled', 'none'] as const) {
      expect(censorDivineNames('יָהּ', { elokim: mode })).toBe('יָ' + NBHY + 'הּ')
    }
  })
})

describe('otherNames selection (אדני, אל, שדי, יה, צבאות — no letter to substitute, each independently toggled)', () => {
  const NO_HE: [string, string, 'adnai' | 'el' | 'shadai' | 'yah' | 'tzevaot'][] = [
    ['אֲדֹנָי', 'אֲדֹנָ' + NBHY + 'י', 'adnai'],
    ['אֵל', 'אֵ' + NBHY + 'ל', 'el'],
    ['שַׁדַּי', 'שַׁ' + NBHY + 'דַּי', 'shadai'],
    ['יָהּ', 'יָ' + NBHY + 'הּ', 'yah'],
    ['צְבָאוֹת', 'צְ' + NBHY + 'בָאוֹת', 'tzevaot'],
  ]

  it('separates a name when its key is selected', () => {
    for (const [source, expected, key] of NO_HE) {
      expect(censorDivineNames(source, { otherNames: [key] })).toBe(expected)
    }
  })

  it('leaves a name fully uncensored when its key is not selected', () => {
    for (const [source] of NO_HE) {
      expect(censorDivineNames(source, { otherNames: [] })).toBe(source)
    }
  })

  it('censors only the selected names, leaving the rest untouched', () => {
    const sentence = 'אֲדֹנָי אֵל שַׁדַּי יָהּ'
    const out = censorDivineNames(sentence, { otherNames: ['adnai'] })
    expect(out).toContain('אֲדֹנָ' + NBHY + 'י')
    expect(out).toContain('אֵל') // untouched
    expect(out).toContain('שַׁדַּי') // untouched
    expect(out).toContain('יָהּ') // untouched
  })

  it('is independent of the elokim setting', () => {
    // אלהים substituted, but the no-ה names left alone.
    const out = censorDivineNames('אֱלֹהִים אֲדֹנָי', { elokim: 'kuf', otherNames: [] })
    expect(out).toBe('אֱלֹקִים אֲדֹנָי')
  })

  it('defaults to all but אהיה selected', () => {
    expect(DEFAULT_OTHER_NAMES_SELECTED).toEqual(['adnai', 'el', 'shadai', 'yah', 'tzevaot'])
    expect(censorDivineNames('אֵל')).toBe('אֵ' + NBHY + 'ל')
    expect(censorDivineNames('יָהּ')).toBe('יָ' + NBHY + 'הּ')
    expect(censorDivineNames('צְבָאוֹת')).toBe('צְ' + NBHY + 'בָאוֹת')
  })
})

describe('ehyeh selection (אהיה — only censored in the phrase אהיה אשר אהיה)', () => {
  const PHRASE = 'אֶהְיֶה אֲשֶׁר אֶהְיֶה'

  it('censors both occurrences in the phrase when selected', () => {
    const out = censorDivineNames(PHRASE, { otherNames: ['ehyeh'] })
    expect(out).toBe('אֶ' + NBHY + 'הְיֶה אֲשֶׁר אֶ' + NBHY + 'הְיֶה')
  })

  it('leaves the phrase untouched when not selected', () => {
    expect(censorDivineNames(PHRASE, { otherNames: [] })).toBe(PHRASE)
  })

  it('does not censor the ordinary verb אהיה outside the phrase', () => {
    const text = 'אֶהְיֶה עִמָּךְ' // "I will be with you" — mundane verb, not the divine Name
    expect(censorDivineNames(text, { otherNames: ['ehyeh'] })).toBe(text)
  })

  it('is not selected by default', () => {
    expect(DEFAULT_OTHER_NAMES_SELECTED).not.toContain('ehyeh')
    expect(censorDivineNames(PHRASE)).toBe(PHRASE)
  })
})

describe('the three settings compose', () => {
  const SENTENCE = 'וַיֹּאמֶר יְהוָה אֱלֹהִים אֲדֹנָי שַׁדַּי אֵל יָהּ'

  it('applies each group according to its own setting', () => {
    const out = censorDivineNames(SENTENCE, {
      mode: 'yudKuf',
      elokim: 'kuf',
      otherNames: [],
    })
    expect(out).toContain('יְקוָק')      // tetragrammaton → יקוק
    expect(out).toContain('אֱלֹקִים')    // אלהים → אלקים
    expect(out).toContain('אֲדֹנָי')     // untouched
    expect(out).toContain('שַׁדַּי')     // untouched
    expect(out).toContain('יָהּ')        // untouched — otherNames is empty
  })

  it("mode 'none' is the master off switch, overriding the other two", () => {
    expect(
      censorDivineNames(SENTENCE, { mode: 'none', elokim: 'kuf', otherNames: ['adnai', 'el', 'shadai', 'yah'] }),
    ).toBe(SENTENCE)
  })

  it('a bare mode string still works and uses the group defaults', () => {
    expect(censorDivineNames(SENTENCE, 'yudDaled')).toBe(censorDivineNames(SENTENCE))
  })
})

describe('normalizeElokimMode / normalizeOtherNamesSelected', () => {
  it('passes valid values through', () => {
    expect(normalizeElokimMode('hyphen')).toBe('hyphen')
    expect(normalizeElokimMode('kuf')).toBe('kuf')
    expect(normalizeElokimMode('daled')).toBe('daled')
    expect(normalizeOtherNamesSelected(['adnai'])).toEqual(['adnai'])
    expect(normalizeOtherNamesSelected([])).toEqual([])
    expect(normalizeOtherNamesSelected(['adnai', 'el', 'shadai', 'yah'])).toEqual(['adnai', 'el', 'shadai', 'yah'])
  })

  it('migrates the legacy mode string', () => {
    expect(normalizeOtherNamesSelected('hyphen')).toEqual(['adnai', 'el', 'shadai', 'yah', 'tzevaot'])
    expect(normalizeOtherNamesSelected('none')).toEqual([])
  })

  it('rejects unknown values so the caller keeps its default', () => {
    for (const bad of ['bogus', '', null, undefined, true, 3]) {
      expect(normalizeElokimMode(bad)).toBeNull()
      expect(normalizeOtherNamesSelected(bad)).toBeNull()
    }
    expect(normalizeOtherNamesSelected(['kuf'])).toBeNull()
    expect(normalizeOtherNamesSelected(['bogus'])).toBeNull()
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
    expect(normalizeDivineNameMode('hyphen')).toBe('hyphen')
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
