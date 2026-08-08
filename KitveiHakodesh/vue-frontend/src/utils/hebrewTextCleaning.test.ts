import { describe, it, expect } from 'vitest'
import { cleanHebrewText } from './hebrewTextCleaning'

describe('cleanHebrewText — colon handling', () => {
  it('keeps chapter:verse citation colons between Hebrew letters', () => {
    expect(cleanHebrewText('(ירושלמי סוטה ט:יג)')).toBe('(ירושלמי סוטה ט:יג)')
    expect(cleanHebrewText('(דברים ח:טו)')).toBe('(דברים ח:טו)')
  })

  it('keeps citation colons between digits and in mixed letter/digit refs', () => {
    expect(cleanHebrewText('12:5')).toBe('12:5')
    expect(cleanHebrewText('ג:2')).toBe('ג:2')
    expect(cleanHebrewText('2:ב')).toBe('2:ב')
  })

  it('still drops mid-sentence artifact colons with surrounding space', () => {
    expect(cleanHebrewText('הוא : שני')).toBe('הוא שני')
    expect(cleanHebrewText('הוא: שני')).toBe('הוא שני')
    expect(cleanHebrewText('הוא :שני')).toBe('הוא שני')
  })

  it('still keeps end-of-line colons', () => {
    expect(cleanHebrewText('סוף הפסוק:')).toBe('סוף הפסוק:')
    expect(cleanHebrewText('סוף הפסוק:<br>')).toBe('סוף הפסוק:<br>')
    expect(cleanHebrewText('סוף הפסוק: <span>')).toBe('סוף הפסוק: <span>')
  })

  it('keeps a colon that is followed by a tag (pre-existing end-of-line branch)', () => {
    // Neither neighbour is a citation char here ('>' and '<'), so the citation
    // rule does not fire — but the colon is immediately followed by '<', which
    // the end-of-line branch already kept before this change. Asserted so the
    // two rules' interaction stays pinned.
    expect(cleanHebrewText('<b>ט</b>:<b>יג</b>')).toBe('<b>ט</b>:<b>יג</b>')
  })

  // The reported bug: Rashi on Ezekiel 3:9. Two citations (ט:יג and ח:טו) were
  // being collapsed to טיג / חטו by the old "mid-line colon ⇒ artifact" rule.
  it('preserves both citations in the reported Rashi passage', () => {
    const input =
      'כְּשָׁמִיר. מִין תּוֹלַעַת הוּא, שֶׁמַּרְאִין אוֹתוֹ עַל הָאֶבֶן וְהִיא נִבְקַעַת כְּנֶגְדּוֹ ' +
      '(ירושלמי סוטה ט:יג). (הגה: לָשׁוֹן אַחֵר שָׁמִיר לְשׁוֹן סֶלַע חָזָק, וּבְתַרְגּוּם יְרוּשַׁלְמִי ' +
      'הוּא תַּרְגּוּם שֶׁל צוּר, וְכֵן תַּרְגּוּם ״מִצּוּר הַחַלָּמִישׁ״ (דברים ח:טו) ״שָׁמִיר טְנָרָא״.'

    const output = cleanHebrewText(input)

    expect(output).toContain('(ירושלמי סוטה ט:יג)')
    expect(output).toContain('(דברים ח:טו)')
    expect(output).not.toContain('טיג')
    expect(output).not.toContain('חטו')
    // The 'הגה:' colon is followed by a space + letter — an artifact, still dropped.
    expect(output).not.toContain('הגה:')
    // Nikkud is gone.
    expect(output).toContain('כשמיר')
  })
})

describe('cleanHebrewText — gershayim / quote handling', () => {
  it('keeps gershayim that sit between the last two letters (ראשי תיבות)', () => {
    expect(cleanHebrewText('רשב"א')).toBe('רשב"א')
    expect(cleanHebrewText('שליט"א')).toBe('שליט"א')
    expect(cleanHebrewText('אמר רשב״א כאן')).toBe('אמר רשב״א כאן')
  })

  it('drops a mid-word quote that opens a quoted particle, not an acronym', () => {
    // The mark sits after the first letter — two letters follow before the
    // word ends, so this is not ראשי תיבות and the quote must go.
    expect(cleanHebrewText('ו״אין דקאמרי ליה קושטא הוא')).toBe('ואין דקאמרי ליה קושטא הוא')
    expect(cleanHebrewText('ו"אין')).toBe('ואין')
  })

  it('applies the same rule to &quot; entities', () => {
    expect(cleanHebrewText('רשב&quot;א')).toBe('רשב"א')
    expect(cleanHebrewText('ו&quot;אין')).toBe('ואין')
  })

  it('still drops quotes at word boundaries', () => {
    expect(cleanHebrewText('"שלום"')).toBe('שלום')
  })
})
