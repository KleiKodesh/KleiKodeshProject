// @vitest-environment jsdom
import { expect, it } from 'vitest'
import { withSpelledHeadingNumbers } from './readAloudHeadingNumbers'
import { DEFAULT_SKIP_OPTIONS, readAloudTextOfPlain } from './readAloudSkip'
import { withDivineNamesSpoken } from './readAloudDivineNames'
import { chooseExpansion, withExpansions, withVocalization } from './readAloudDicta'

it('long lines are processed fast (no regex backtracking)', () => {
  const words = 'ומשה היה רועה את צאן יתרו חותנו (בראשית י"ב כ\') ויבא אל הר האלהים חורבה ר\' יהושע אומר ויחר אף ד\' במשה אלהינו ה\' אלקים'.split(' ')
  const long = Array.from({ length: 300 }, (_, i) => words[i % words.length]).join(' ') + ' ('
  const t0 = performance.now()
  // 20 rounds of every transform: well under a frame per line.
  for (let i = 0; i < 20; i++) {
    const t = readAloudTextOfPlain(long, { ...DEFAULT_SKIP_OPTIONS, links: true, parentheses: true, squareBrackets: true, singleLetterWords: true })
    withSpelledHeadingNumbers(withDivineNamesSpoken(t), false)
    withSpelledHeadingNumbers(t, true)
    withVocalization(withExpansions(t, []), t.spoken)
    chooseExpansion('רשב"י', ['רשב"י', 'רבי שמעון בר יוחאי', 'רבי שמואל בן יעקב'])
  }
  // Per line: tens of ms at most (a backtracking regex took seconds).
  expect((performance.now() - t0) / 20).toBeLessThan(30)
})
