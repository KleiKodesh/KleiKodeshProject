/**
 * Talmudic, Biblical, and modern units of measure — definitions.
 *
 * Sources:
 *  [Naeh]    ר' אברהם חיים נאה, שיעורי תורה (1947) — widely accepted Sephardic/general practice
 *  [CI]      חזון איש (ר' אברהם ישעיה קרליץ), שיעורין של תורה — stringent Ashkenazic/Litvish opinion
 *  [Wiki]    Wikipedia: "Biblical and Talmudic units of measurement"
 *  [TC]      TorahCalc.com — "Information on Biblical / Talmudic Units" (cites primary sources)
 *  [SA]      שולחן ערוך (ר' יוסף קארו)
 *  [MB]      משנה ברורה (ר' ישראל מאיר הכהן)
 *  [SAR]     shulchanaruchharav.com — "Did the Beitzim/Eggs become smaller"
 *  [Rambam]  רמב"ם, משנה תורה / פירוש המשנה
 *  [Mish]    משנה שקלים א:ג — complete coin chain (primary Mishnaic source)
 *  [SI]      International System of Units — exact defined constants
 *
 * Anchor notes:
 *  LENGTH  — אצבעות (finger-breadths)
 *  VOLUME  — כביצות (egg volumes)
 *  WEIGHT  — גרות (gerahs)
 *  COINS   — פרוטות (perutot) — smallest Talmudic coin
 *  TIME    — רגעים (moments)
 */

export type MeasurementSystem = 'length' | 'volume' | 'weight' | 'coins' | 'time'

export interface Unit {
  anchor: number
  system: MeasurementSystem
  heDesc?: string
  disputed?: string
}

// ── Metric/imperial conversion factors ───────────────────────────────────────
// Applied to the base unit of each system to get SI/metric values.

export interface MetricFactors {
  lengthCmPerEtzba: number    // cm per אצבע
  volumeMlPerBeitza: number   // ml per כביצה
  weightGramsPerGerah: number // grams per גרה
}

/**
 * ר' חיים נאה — שיעורי תורה (1947), ג:כה עמ' 249-250
 * אצבע = 2.0 ס"מ → אמה = 48 ס"מ [TC, Wiki, JTA]
 * כביצה = 54 מ"ל (שיעורי תורה ג:ט; בפועל 54 מ"ל) [SAR, TC]
 * גרה: שקל ≈ 14 גרם → גרה = 0.7 גרם [TC]
 */
export const NAEH: MetricFactors = {
  lengthCmPerEtzba: 2.0,
  volumeMlPerBeitza: 54,
  weightGramsPerGerah: 0.7,
}

/**
 * חזון איש — שיעורין של תורה (ר' יעקב קניבסקי בשם החזון איש), עמ' 3
 * אצבע = 2.38 ס"מ → אמה = 57.12 ס"מ [TC, Wiki]
 * כביצה = 100 מ"ל [SAR, TC, MB 486:א]
 * גרה: שקל ≈ 17 גרם → גרה = 0.85 גרם [TC]
 */
export const CHAZON_ISH: MetricFactors = {
  lengthCmPerEtzba: 2.38,
  volumeMlPerBeitza: 100,
  weightGramsPerGerah: 0.85,
}

// ── LENGTH ────────────────────────────────────────────────────────────────────
// Base: אצבע
// Talmudic chain [Wiki: תוספתא כלים מציעא ו:ד; TC; Rambam]:
//   טפח = 4 אצבעות | זרת = 3 טפחים | אמה = 6 טפחים | קנה = 6 אמות
//   ריס = 266⅔ אמות (= 1600 טפחים) | מיל = 2000 אמות | פרסה = 4 מילים
//
// Modern units anchored via Naeh: 1 אצבע = 2.0 ס"מ (exact for ratio purposes)
// All SI/imperial exact definitions [SI]:
//   1 inch = 2.54 cm (exact) | 1 foot = 30.48 cm (exact) | 1 yard = 91.44 cm (exact)
//   1 mile = 160934.4 cm (exact) | 1 km = 100000 cm
// Modern units use a neutral anchor (average of Naeh/CI) for inter-modern conversions,
// but when converting TO/FROM Talmudic units the opinion factor is applied.
// For the unit-to-unit table we store modern units in cm directly (opinion-independent).

export const LENGTH: Record<string, Unit> = {
  // ── Talmudic ──
  אצבע:          { anchor: 1,        system: 'length', heDesc: 'רוחב אצבע' },
  אגודל:         { anchor: 1,        system: 'length', heDesc: 'כאצבע' },
  טפח:           { anchor: 4,        system: 'length', heDesc: '4 אצבעות' },
  חסיט:          { anchor: 8,        system: 'length', heDesc: '2 טפחים' },
  זרת:           { anchor: 12,       system: 'length', heDesc: '3 טפחים' },
  אמה:           { anchor: 24,       system: 'length', heDesc: '6 טפחים' },
  'אמה בת חמשה': { anchor: 20,       system: 'length', heDesc: '5 טפחים' },
  גומד:          { anchor: 20,       system: 'length', heDesc: 'אמה קצרה' },
  קנה:           { anchor: 144,      system: 'length', heDesc: '6 אמות' },
  ריס:           { anchor: 6400,     system: 'length', heDesc: '266⅔ אמות' },
  מיל:           { anchor: 48000,    system: 'length', heDesc: '2000 אמות' },
  פרסה:          { anchor: 192000,   system: 'length', heDesc: '4 מילים' },
  'דרך יום':     { anchor: 1920000,  system: 'length', heDesc: '10 פרסאות' },

  // ── Modern metric — anchored in cm, expressed as אצבעות via Naeh (2.0 cm/אצבע) ──
  // Stored as cm/2.0 so they sit in the same anchor space as Talmudic units under Naeh.
  // When the user switches opinion the Talmudic units scale; modern units stay fixed in real-world cm.
  // We handle this in midot.ts by treating modern units as "absolute cm" units.
  מ״מ:   { anchor: 0.1  / 2.0, system: 'length', heDesc: 'מילימטר' },
  'ס"מ': { anchor: 1    / 2.0, system: 'length', heDesc: 'סנטימטר' },
  'מ׳':  { anchor: 100  / 2.0, system: 'length', heDesc: 'מטר' },
  'ק"מ': { anchor: 100000 / 2.0, system: 'length', heDesc: 'קילומטר' },
  // Imperial [SI exact definitions]:
  'אינץ׳': { anchor: 2.54  / 2.0, system: 'length', heDesc: 'inch = 2.54 ס"מ' },
  'רגל':   { anchor: 30.48 / 2.0, system: 'length', heDesc: 'foot = 30.48 ס"מ' },
  'יארד':  { anchor: 91.44 / 2.0, system: 'length', heDesc: 'yard = 91.44 ס"מ' },
  'מייל':  { anchor: 160934.4 / 2.0, system: 'length', heDesc: 'mile = 1.609 ק"מ' },
}

// ── VOLUME ────────────────────────────────────────────────────────────────────
// Base: כביצה
// Chain [Wiki; TC; Rambam; Danby Mishnah Appendix II]:
//   לוג = 6 כביצות | רביעית = 1.5 כביצות | קב = 24 | סאה = 144 | איפה = 432 | כור = 4320
// Modern units anchored via Naeh: 1 כביצה = 54 מ"ל
// SI exact: 1 fl oz (US) = 29.5735 ml | 1 pint (US) = 473.176 ml | 1 liter = 1000 ml
//           1 cup (US) = 236.588 ml | 1 gallon (US) = 3785.41 ml

export const VOLUME: Record<string, Unit> = {
  // ── Talmudic ──
  קורטוב:  { anchor: 6/64,    system: 'volume', heDesc: '1/64 לוג' },
  משורה:   { anchor: 6/32,    system: 'volume', heDesc: '1/32 לוג' },
  גרוגרת:  { anchor: 0.3,     system: 'volume', heDesc: '3/10 כביצה' },
  כזית:    { anchor: 0.5,     system: 'volume', heDesc: '1/2 כביצה', disputed: 'דעה אחרת: 1/3 כביצה' },
  כביצה:   { anchor: 1,       system: 'volume', heDesc: 'נפח ביצה' },
  עוכלא:   { anchor: 1.2,     system: 'volume', heDesc: '1/20 קב' },
  רביעית:  { anchor: 1.5,     system: 'volume', heDesc: '1/4 לוג' },
  תומן:    { anchor: 3,       system: 'volume', heDesc: '1/8 קב' },
  פרס:     { anchor: 4,       system: 'volume', heDesc: 'רש"י: 4 כביצות', disputed: 'רמב"ם: 3 כביצות' },
  לוג:     { anchor: 6,       system: 'volume', heDesc: '6 כביצות' },
  ליטרא:   { anchor: 12,      system: 'volume', heDesc: '2 לוגים' },
  עומר:    { anchor: 43.2,    system: 'volume', heDesc: '1/10 איפה' },
  קב:      { anchor: 24,      system: 'volume', heDesc: '4 לוגים' },
  הין:     { anchor: 72,      system: 'volume', heDesc: '12 לוגים' },
  סאה:     { anchor: 144,     system: 'volume', heDesc: '6 קבים' },
  איפה:    { anchor: 432,     system: 'volume', heDesc: '3 סאות' },
  לתך:     { anchor: 2160,    system: 'volume', heDesc: '5 איפות' },
  כור:     { anchor: 4320,    system: 'volume', heDesc: '10 איפות' },
  מקווה:   { anchor: 5760,    system: 'volume', heDesc: '40 סאות' },

  // ── Modern metric — anchored in ml, expressed as כביצות via Naeh (54 ml/כביצה) ──
  'מ"ל':       { anchor: 1      / 54, system: 'volume', heDesc: 'מיליליטר' },
  'ל׳':        { anchor: 1000   / 54, system: 'volume', heDesc: 'ליטר' },
  // Imperial liquid [SI exact]:
  'כף':        { anchor: 14.7868 / 54, system: 'volume', heDesc: 'tablespoon = 14.79 מ"ל' },
  'כפית':      { anchor: 4.92892 / 54, system: 'volume', heDesc: 'teaspoon = 4.93 מ"ל' },
  'fl oz':     { anchor: 29.5735 / 54, system: 'volume', heDesc: 'fluid ounce = 29.57 מ"ל' },
  'כוס':       { anchor: 236.588 / 54, system: 'volume', heDesc: 'cup = 236.6 מ"ל' },
  'פינט':      { anchor: 473.176 / 54, system: 'volume', heDesc: 'pint = 473.2 מ"ל' },
  'קווארט':    { anchor: 946.353 / 54, system: 'volume', heDesc: 'quart = 946.4 מ"ל' },
  'גלון':      { anchor: 3785.41 / 54, system: 'volume', heDesc: 'gallon = 3.785 ל׳' },
}

// ── WEIGHT ────────────────────────────────────────────────────────────────────
// Base: גרה
// Chain [Exodus 30:13; TC; Wiki; SA]:
//   שקל = 20 גרות | סלע = 2 שקלים | מנה = 60 שקלים | כיכר = 60 מנה
// Modern: 1 oz (avoirdupois) = 28.3495 g | 1 lb = 453.592 g | 1 kg = 1000 g
// Anchored via Naeh: גרה = 0.7 g

export const WEIGHT: Record<string, Unit> = {
  // ── Talmudic ──
  גרה:     { anchor: 1,     system: 'weight', heDesc: '1/20 שקל' },
  מעה:     { anchor: 2,     system: 'weight', heDesc: '2 גרות' },
  דינר:    { anchor: 10,    system: 'weight', heDesc: '10 גרות' },
  שקל:     { anchor: 20,    system: 'weight', heDesc: '20 גרות' },
  סלע:     { anchor: 40,    system: 'weight', heDesc: '2 שקלים' },
  אונקיא:  { anchor: 80,    system: 'weight', heDesc: '2 סלעים' },
  תרטימר:  { anchor: 500,   system: 'weight', heDesc: '1/2 מנה' },
  מנה:     { anchor: 1200,  system: 'weight', heDesc: '60 שקלים' },
  כיכר:    { anchor: 72000, system: 'weight', heDesc: '60 מנה' },

  // ── Modern — anchored in grams via Naeh (גרה = 0.7 g) ──
  'גרם':   { anchor: 1     / 0.7, system: 'weight', heDesc: 'גרם' },
  'ק"ג':   { anchor: 1000  / 0.7, system: 'weight', heDesc: 'קילוגרם' },
  // Imperial [SI exact]:
  'אונס':  { anchor: 28.3495 / 0.7, system: 'weight', heDesc: 'ounce = 28.35 גרם' },
  'ליברה': { anchor: 453.592 / 0.7, system: 'weight', heDesc: 'pound = 453.6 גרם' },
}

// ── COINS ─────────────────────────────────────────────────────────────────────
// Base: פרוטה (smallest Talmudic coin)
// Complete chain from Mishnah Shekalim 1:3 (primary source):
//   "סלע = 4 דינרים; דינר = 6 מעות; מעה = 2 פונדיון; פונדיון = 2 איסר; פרוטה = 1/8 איסר"
//   → פרוטה=1 | איסר=8 | פונדיון=16 | מעה=32 | דינר=192 | סלע=768
// Additional:
//   שקל = 2 דינרים = 384 פרוטות [Mishnah Shekalim 1:2; TC]
//   מנה = 25 סלעים = 19200 פרוטות [TC; Talmud Bechoros 49b]
//   כיכר = 60 מנה = 1152000 פרוטות [TC; Wiki]
// Note: דינר זהב = 25 דינרים כסף [Talmud Bechoros 50a; TC]

export const COINS: Record<string, Unit> = {
  פרוטה:       { anchor: 1,        system: 'coins', heDesc: '1/8 איסר' },
  איסר:        { anchor: 8,        system: 'coins', heDesc: '8 פרוטות' },
  פונדיון:     { anchor: 16,       system: 'coins', heDesc: '2 איסרים' },
  מעה:         { anchor: 32,       system: 'coins', heDesc: '2 פונדיון' },
  'טרפעיק':    { anchor: 48,       system: 'coins', heDesc: '3 מעות (איסתרא)' },
  איסתרא:      { anchor: 48,       system: 'coins', heDesc: '3 מעות' },
  שקל:         { anchor: 384,      system: 'coins', heDesc: '2 דינרים' },
  דינר:        { anchor: 192,      system: 'coins', heDesc: '6 מעות' },
  סלע:         { anchor: 768,      system: 'coins', heDesc: '4 דינרים' },
  'דינר זהב':  { anchor: 4800,     system: 'coins', heDesc: '25 דינרים כסף', disputed: 'תלמוד בכורות נ' },
  מנה:         { anchor: 19200,    system: 'coins', heDesc: '25 סלעים' },
  'מנה של קודש': { anchor: 38400,  system: 'coins', heDesc: '2 מנה' },
  כיכר:        { anchor: 1152000,  system: 'coins', heDesc: '60 מנה' },
  'כיכר של קודש': { anchor: 2304000, system: 'coins', heDesc: '2 כיכר' },
}

// ── TIME ──────────────────────────────────────────────────────────────────────
// Base: רגע
// Chain [TC; Wiki; Talmud Berachot]:
//   חלק = 76 רגעים | שעה = 1080 חלקים | יום = 24 שעות
// Modern: seconds/minutes/hours anchored via רגע = 3/76 seconds (= 1 חלק/76 = 3.333s/76)
// 1 חלק = 3⅓ seconds [TC] → 1 רגע = 10/228 seconds = 5/114 seconds ≈ 0.04386 s

export const TIME: Record<string, Unit> = {
  // ── Talmudic ──
  רגע:    { anchor: 1,                  system: 'time', heDesc: '1/76 חלק' },
  חלק:    { anchor: 76,                 system: 'time', heDesc: '76 רגעים' },
  עת:     { anchor: 142.5,              system: 'time', heDesc: '1/24 עונה' },
  שעה:    { anchor: 82080,              system: 'time', heDesc: '1080 חלקים' },
  עונה:   { anchor: 984960,             system: 'time', heDesc: '12 שעות' },
  יום:    { anchor: 1969920,            system: 'time', heDesc: '24 שעות' },
  שבוע:   { anchor: 1969920 * 7,        system: 'time', heDesc: '7 ימים' },
  חודש:   { anchor: 1969920 * 29.5,     system: 'time', heDesc: '29–30 ימים' },
  שנה:    { anchor: 1969920 * 354,      system: 'time', heDesc: '354 ימים' },
  שמיטה:  { anchor: 1969920 * 354 * 7,  system: 'time', heDesc: '7 שנים' },
  יובל:   { anchor: 1969920 * 354 * 50, system: 'time', heDesc: '50 שנה' },

  // ── Modern — anchored in seconds, expressed as רגעים (1 רגע ≈ 0.04386 s) ──
  // 1 רגע = 5/114 s → 1 s = 114/5 = 22.8 רגעים [TC: "22.8 rega'im is 1 second"]
  'שנייה':  { anchor: 22.8,              system: 'time', heDesc: 'שנייה' },
  'דקה':    { anchor: 22.8 * 60,         system: 'time', heDesc: 'דקה' },
  'שעה מודרנית': { anchor: 22.8 * 3600,  system: 'time', heDesc: 'שעה (60 דקות)' },
}
