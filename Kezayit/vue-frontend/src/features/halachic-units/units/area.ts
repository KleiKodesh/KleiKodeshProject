import type { Unit, UnitSource } from './conversionUnitTypes'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const TC: UnitSource = { label: 'TorahCalc', url: 'https://www.torahcalc.com/info/biblical-units/' }
const SI: UnitSource = { label: 'SI' }

const BEIT_ROVA = (104 + 1 / 6) * 576

export const AREA: Record<string, Unit> = {
  שערה: { anchor: 1 / 100, system: 'area', heDesc: 'רוחב שערה²', refSource: TC },
  עדשה: { anchor: 4 / 100, system: 'area', heDesc: '4 שערות', refSource: TC },
  גריס: {
    anchor: 36 / 100,
    system: 'area',
    heDesc: '9 עדשות',
    talmudSource: { label: 'משנה נגעים ו:א' },
    refSource: M,
  },
  'אצבע מרובעת': { anchor: 1, system: 'area', heDesc: 'אצבע × אצבע', refSource: M },
  'טפח מרובע': { anchor: 16, system: 'area', heDesc: '16 אצבעות מרובעות', refSource: M },
  'אמה מרובעת': { anchor: 576, system: 'area', heDesc: '36 טפחים מרובעים', refSource: M },
  'בית רובע': { anchor: BEIT_ROVA, system: 'area', heDesc: '104⅙ אמות מרובעות', refSource: M },
  'בית קב': { anchor: BEIT_ROVA * 4, system: 'area', heDesc: '4 בית רובע', refSource: M },
  'בית סאה': { anchor: BEIT_ROVA * 24, system: 'area', heDesc: '50×50 אמות', refSource: M },
  'בית סאתים': { anchor: BEIT_ROVA * 48, system: 'area', heDesc: '2 בית סאה', refSource: M },
  'בית כור': { anchor: BEIT_ROVA * 720, system: 'area', heDesc: '30 בית סאה', refSource: M },
  'מ"מ²': { anchor: 0.01 / 4.0, system: 'area', heDesc: 'מ"מ רבוע', refSource: SI },
  'ס"מ²': { anchor: 1 / 4.0, system: 'area', heDesc: 'ס"מ רבוע', refSource: SI },
  "מ'²": { anchor: 10000 / 4.0, system: 'area', heDesc: 'מטר רבוע', refSource: SI },
  דונם: { anchor: 10000000 / 4.0, system: 'area', heDesc: '1,000 מ"ר', refSource: SI },
  הקטאר: { anchor: 100000000 / 4.0, system: 'area', heDesc: '10,000 מ"ר', refSource: SI },
  "אינץ'²": { anchor: 6.4516 / 4.0, system: 'area', heDesc: 'inch²', refSource: SI },
  'רגל²': { anchor: 929.0304 / 4.0, system: 'area', heDesc: 'foot²', refSource: SI },
  'יארד²': { anchor: 8361.2736 / 4.0, system: 'area', heDesc: 'yard²', refSource: SI },
  אקר: { anchor: 40468564.224 / 4.0, system: 'area', heDesc: 'acre', refSource: SI },
}
