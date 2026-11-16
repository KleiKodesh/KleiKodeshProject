// @vitest-environment jsdom
import { expect, it } from 'vitest'
import { withSpelledHeadingNumbers } from './readAloudHeadingNumbers'
import { DEFAULT_SKIP_OPTIONS, readAloudTextOfPlain } from './readAloudSkip'
import { chooseExpansion, withExpansions, withVocalization } from './readAloudDicta'

it('long lines are processed fast (no regex backtracking)', () => {
  const words = 'ומשה היה רועה את צאן יתרו חותנו (בראשית י"ב כ\') ויבא אל הר האלהים חורבה ר\' יהושע אומר'.split(' ')
  const long = Array.from({ length: 300 }, (_, i) => words[i % words.length]).join(' ') + ' ('
  const t0 = performance.now()
  for (let i = 0; i < 20; i++) {
    const t = readAloudTextOfPlain(long, { ...DEFAULT_SKIP_OPTIONS, links: true, parentheses: true, squareBrackets: true, singleLetterWords: true })
    withSpelledHeadingNumbers(t, false)
    withSpelledHeadingNumbers(t, true)
    withVocalization(withExpansions(t, []), t.spoken)
    chooseExpansion('רשב"י', ['רשב"י', 'רבי שמעון בר יוחאי', 'רבי שמואל בן יעקב'])
  }
  expect(performance.now() - t0).toBeLessThan(1500)
})
