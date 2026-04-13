/**
 * מידות שטח — Area units
 *
 * Base unit: אצבע מרובעת (square finger-breadth)
 *
 * Chain [TC; Wiki]:
 *   שערה ≈ 1/100 אצבע מרובעת ("space between two adjacent hairs") [TC]
 *   עדשה = 4 שערות | גריס = 9 עדשות
 *   טפח מרובע = 16 אצבעות מרובעות
 *   אמה מרובעת = 36 טפחים מרובעים = 576 אצבעות מרובעות
 *   בית רובע = 104⅙ אמות מרובעות [TC]
 *   בית קב = 4 בית רובע | בית סאה = 6 בית קב | בית כור = 30 בית סאה
 *
 * Modern units stored in Naeh-space (anchor = cm² / 4.0).
 * SI exact: 1 in² = 6.4516 cm² | 1 ft² = 929.0304 cm² | 1 yd² = 8361.2736 cm²
 *           1 acre = 40,468,564.224 cm² | 1 dunam = 10,000,000 cm² | 1 hectare = 100,000,000 cm²
 */

import type { Unit } from './types'

// בית רובע = 104⅙ אמות מרובעות = 104.1667 × 576 אצבעות מרובעות
const BEIT_ROVA = (104 + 1 / 6) * 576

export const AREA: Record<string, Unit> = {
  // ── Talmudic ───────────────────────────────────────────────────────────────
  שערה: { anchor: 1 / 100, system: 'area', heDesc: 'רוחב שערה²' },
  עדשה: { anchor: 4 / 100, system: 'area', heDesc: '4 שערות' },
  גריס: { anchor: 36 / 100, system: 'area', heDesc: '9 עדשות' },
  'אצבע מרובעת': { anchor: 1, system: 'area', heDesc: 'אצבע × אצבע' },
  'טפח מרובע': { anchor: 16, system: 'area', heDesc: '16 אצבעות מרובעות' },
  'אמה מרובעת': { anchor: 576, system: 'area', heDesc: '36 טפחים מרובעים' },
  'בית רובע': { anchor: BEIT_ROVA, system: 'area', heDesc: '104⅙ אמות מרובעות' },
  'בית קב': { anchor: BEIT_ROVA * 4, system: 'area', heDesc: '4 בית רובע' },
  'בית סאה': { anchor: BEIT_ROVA * 24, system: 'area', heDesc: '6 בית קב' },
  'בית סאתים': { anchor: BEIT_ROVA * 48, system: 'area', heDesc: '2 בית סאה' },
  'בית כור': { anchor: BEIT_ROVA * 720, system: 'area', heDesc: '30 בית סאה' },

  // ── Modern metric [SI exact] ───────────────────────────────────────────────
  'מ"מ²': { anchor: 0.01 / 4.0, system: 'area', heDesc: 'מ"מ רבוע' },
  'ס"מ²': { anchor: 1 / 4.0, system: 'area', heDesc: 'ס"מ רבוע' },
  "מ'²": { anchor: 10000 / 4.0, system: 'area', heDesc: 'מטר רבוע' },
  דונם: { anchor: 10000000 / 4.0, system: 'area', heDesc: '1,000 מ"ר' },
  הקטאר: { anchor: 100000000 / 4.0, system: 'area', heDesc: '10,000 מ"ר' },

  // ── Imperial [SI exact] ────────────────────────────────────────────────────
  "אינץ'²": { anchor: 6.4516 / 4.0, system: 'area', heDesc: 'inch²' },
  'רגל²': { anchor: 929.0304 / 4.0, system: 'area', heDesc: 'foot²' },
  'יארד²': { anchor: 8361.2736 / 4.0, system: 'area', heDesc: 'yard²' },
  אקר: { anchor: 40468564.224 / 4.0, system: 'area', heDesc: 'acre' },
}
