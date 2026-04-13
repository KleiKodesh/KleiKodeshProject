/**
 * מטבעות — Talmudic coin units
 *
 * Base unit: פרוטה (smallest Talmudic coin)
 *
 * Complete chain from Mishnah Shekalim 1:3 (primary source — verbatim):
 *   "סלע = 4 דינרים; דינר = 6 מעות; מעה = 2 פונדיון; פונדיון = 2 איסר; פרוטה = 1/8 איסר"
 *
 * Derived chain (all from Mishnah Shekalim 1:3 + TorahCalc [TC]):
 *   פרוטה   = 1
 *   קונטרנק = 2 פרוטות  [TC]
 *   מסימס   = 4 פרוטות  [TC]
 *   איסר    = 8 פרוטות  [Mish Shekalim 1:3]
 *   פונדיון = 2 איסרים = 16 פרוטות  [Mish Shekalim 1:3]
 *   מעה/גרה = 2 פונדיון = 32 פרוטות  [Mish Shekalim 1:3]
 *   איסתרא/טרפעיק = 3 מעות = 96 פרוטות  [TC; Mish Shekalim 1:3 implied]
 *   דינר/זוז = 6 מעות = 192 פרוטות  [Mish Shekalim 1:3: "דינר = 6 מעות"]
 *   שקל     = 2 דינרים = 384 פרוטות  [Mish Shekalim 1:2; TC]
 *   סלע     = 4 דינרים = 768 פרוטות  [Mish Shekalim 1:3: "סלע = 4 דינרים"]
 *   דינר זהב = 6¼ סלעים = 4800 פרוטות  [TC; תלמוד בכורות נ א]
 *   מנה     = 25 סלעים = 19200 פרוטות  [TC; תלמוד בכורות מט ב]
 *   מנה של קודש = 2 מנה = 38400 פרוטות  [TC]
 *   כיכר    = 60 מנה = 1152000 פרוטות  [TC; Wiki]
 *   כיכר של קודש = 2 כיכר = 2304000 פרוטות  [TC]
 *
 * Note on דינר vs. זוז: in Talmudic literature these are synonyms for the same coin.
 * Note on מעה vs. גרה: in coin context these are the same; גרה is the biblical name [שמות ל:יג].
 */

import type { Unit } from './types'

export const COINS: Record<string, Unit> = {
  פרוטה: { anchor: 1, system: 'coins', heDesc: '1/8 איסר' },
  קונטרנק: { anchor: 2, system: 'coins', heDesc: '2 פרוטות' },
  מסימס: { anchor: 4, system: 'coins', heDesc: '4 פרוטות' },
  איסר: { anchor: 8, system: 'coins', heDesc: '8 פרוטות' },
  פונדיון: { anchor: 16, system: 'coins', heDesc: '2 איסרים' },
  מעה: { anchor: 32, system: 'coins', heDesc: '2 פונדיון' },
  גרה: { anchor: 32, system: 'coins', heDesc: 'כמעה (שם בתורה)' },
  איסתרא: { anchor: 96, system: 'coins', heDesc: '3 מעות' },
  טרפעיק: { anchor: 96, system: 'coins', heDesc: 'כאיסתרא' },
  דינר: { anchor: 192, system: 'coins', heDesc: '6 מעות [משנה שקלים א:ג]' },
  זוז: { anchor: 192, system: 'coins', heDesc: 'כדינר' },
  שקל: { anchor: 384, system: 'coins', heDesc: '2 דינרים [משנה שקלים א:ב]' },
  סלע: { anchor: 768, system: 'coins', heDesc: '4 דינרים [משנה שקלים א:ג]' },
  'דינר זהב': { anchor: 4800, system: 'coins', heDesc: '6¼ סלעים [בכורות נ א]' },
  מנה: { anchor: 19200, system: 'coins', heDesc: '25 סלעים [בכורות מט ב]' },
  'מנה של קודש': { anchor: 38400, system: 'coins', heDesc: '2 מנה' },
  כיכר: { anchor: 1152000, system: 'coins', heDesc: '60 מנה' },
  'כיכר של קודש': { anchor: 2304000, system: 'coins', heDesc: '2 כיכר' },
}
