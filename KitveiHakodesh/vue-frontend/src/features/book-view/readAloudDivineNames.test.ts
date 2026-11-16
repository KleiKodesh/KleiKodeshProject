// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { withDivineNamesSpoken } from './readAloudDivineNames'
import { withSpelledHeadingNumbers } from './readAloudHeadingNumbers'
import { readAloudTextOfPlain } from './readAloudSkip'

const spoken = (s: string) => withDivineNamesSpoken(readAloudTextOfPlain(s, null)).spoken

describe('withDivineNamesSpoken', () => {
  it('reads the four-letter name and its stand-ins as השם', () => {
    for (const name of ['יהוה', 'יקוק', 'ידוד', 'יי', "ה'", "ד'", 'ה׳', 'י-ה-ו-ה']) {
      expect(spoken(`ברוך ${name} לעולם`)).toBe('ברוך השם לעולם')
    }
    expect(spoken("וה' אמר")).toBe('והשם אמר')
    expect(spoken("לה' הארץ")).toBe('להשם הארץ')
    expect(spoken('ביהוה')).toBe('בהשם')
    expect(spoken("ויחר אף ד' במשה")).toBe('ויחר אף השם במשה')
  })
  it('reads the אלהים family with ק', () => {
    expect(spoken('אלהים')).toBe('אלוקים')
    expect(spoken('אלקים')).toBe('אלוקים')
    expect(spoken('אלדים')).toBe('אלוקים')
    expect(spoken('א‑להים')).toBe('אלוקים')
    expect(spoken('אלהינו')).toBe('אלוקינו')
    expect(spoken('והאלהים')).toBe('והאלוקים')
    expect(spoken('אלוה')).toBe('אלוק')
    expect(spoken('אלהי ישראל')).toBe('אלוקי ישראל')
  })
  it('leaves ordinary words and numbers alone', () => {
    expect(spoken('אלה הדברים')).toBe('אלה הדברים')
    expect(spoken("פרק ה'")).toBe("פרק ה'")
    expect(spoken("סימן ד'")).toBe("סימן ד'")
    expect(spoken("ג' ד' ה'")).toBe("ג' ד' ה'")
    expect(spoken("ד' אמות")).toBe("ד' אמות")
    expect(spoken("(ה') ומשה")).toBe("(ה') ומשה")
    expect(spoken('אלקנה ויהודה')).toBe('אלקנה ויהודה')
  })
  it('maps the spoken name back onto the shown name', () => {
    const t = withDivineNamesSpoken(readAloudTextOfPlain("אף ד' במשה", null))
    const at = t.spoken.indexOf('השם')
    expect(t.toFull[at]).toBe(3)
    expect(t.toFull[at + 3]).toBe(t.full.indexOf('במשה') - 1)
  })
})

describe('line-start number markers', () => {
  const line = (s: string) => withSpelledHeadingNumbers(withDivineNamesSpoken(readAloudTextOfPlain(s, null)), false).spoken
  it('reads a bracketed number by its letters', () => {
    expect(line('(כה) ומשה')).toBe('כף הא ומשה')
    expect(line("[ז'] ומשה")).toBe('זין ומשה')
    expect(line('יב) ומשה')).toBe('יוד בית ומשה')
    expect(line("(ה') ומשה")).toBe('הא ומשה')
    expect(line('(ך) ומשה')).toBe('כף ומשה')
  })
  it('leaves labels and other forms alone', () => {
    expect(line('(הג"ה) ומשה')).toBe('(הג"ה) ומשה')
    expect(line('[שו"ע] ומשה')).toBe('[שו"ע] ומשה')
    expect(line('(יה) ומשה')).toBe('(יה) ומשה')
    expect(line('א. ומשה')).toBe('א. ומשה')
    expect(line('א: ומשה')).toBe('א: ומשה')
    expect(line('(משה) אמר')).toBe('(משה) אמר')
  })
})
