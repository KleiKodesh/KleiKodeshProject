import { describe, it, expect } from 'vitest'
import { normalizeAddinQuery, queryTargetsAddins, addinDisplayTitle } from './otzariaAddins'

const INDEX_PREFIX = 'תוסף אוצריא: '

describe('normalizeAddinQuery', () => {
  it('rewrites the bare shorthand to the prefix the index stores', () => {
    expect(normalizeAddinQuery('תוספים')).toBe(INDEX_PREFIX)
  })

  it('accepts the trailing colon users actually type', () => {
    expect(normalizeAddinQuery('תוספים:')).toBe(INDEX_PREFIX)
    expect(normalizeAddinQuery('תוספים: לוח')).toBe(`${INDEX_PREFIX}לוח`)
  })

  it('rewrites the term mid-query without eating the preceding word', () => {
    expect(normalizeAddinQuery('חפש תוספים כאן')).toBe(`חפש ${INDEX_PREFIX}כאן`)
  })

  // The bug this guards: an unanchored pattern rewrote any word ENDING in
  // תוספים, turning an ordinary book query into a nonsense index query. These
  // inputs reach the home bar and address bar, where people type book titles.
  it('leaves the term alone inside a longer word', () => {
    expect(normalizeAddinQuery('בעלי התוספים')).toBe('בעלי התוספים')
    expect(normalizeAddinQuery('התוספים שלי')).toBe('התוספים שלי')
  })

  it('leaves a query without the term untouched', () => {
    expect(normalizeAddinQuery('משנה ברורה')).toBe('משנה ברורה')
    expect(normalizeAddinQuery('')).toBe('')
  })
})

describe('queryTargetsAddins', () => {
  it('agrees with normalizeAddinQuery on what counts as the shorthand', () => {
    for (const query of ['תוספים', 'תוספים:', 'תוספים: לוח', 'חפש תוספים כאן']) {
      expect(queryTargetsAddins(query)).toBe(true)
      expect(normalizeAddinQuery(query)).not.toBe(query)
    }
    for (const query of ['בעלי התוספים', 'התוספים שלי', 'משנה ברורה', '']) {
      expect(queryTargetsAddins(query)).toBe(false)
      expect(normalizeAddinQuery(query)).toBe(query)
    }
  })

  // A non-global regex reused across calls would advance lastIndex and start
  // alternating true/false.
  it('is not stateful across repeated calls', () => {
    for (let i = 0; i < 5; i++) expect(queryTargetsAddins('תוספים')).toBe(true)
  })
})

describe('addinDisplayTitle', () => {
  it('strips the baked index prefix', () => {
    expect(addinDisplayTitle(`${INDEX_PREFIX}לוח השנה`)).toBe('לוח השנה')
  })

  it('tolerates a missing space after the colon', () => {
    expect(addinDisplayTitle('תוסף אוצריא:לוח השנה')).toBe('לוח השנה')
  })

  it('returns a name that carries no prefix unchanged', () => {
    expect(addinDisplayTitle('לוח השנה')).toBe('לוח השנה')
    expect(addinDisplayTitle('')).toBe('')
  })
})
