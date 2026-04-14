/**
 * Shared types for all unit definition files.
 */

export type MeasurementSystem = 'length' | 'area' | 'volume' | 'weight' | 'coins' | 'time'

export interface UnitSource {
  label: string // Hebrew description of the source
  url?: string // link to open in new window
}

export interface Unit {
  anchor: number
  system: MeasurementSystem
  heDesc?: string
  disputed?: string
  /**
   * Primary Talmudic/Biblical/posek source for the unit's ratio.
   * Used when showing the source of a Talmudic↔Talmudic conversion.
   * e.g. תוספתא כלים מציעא ו:ד | תלמוד בבלי פסחים קט א
   */
  talmudSource?: UnitSource
  /**
   * Secondary reference source (encyclopedia, website).
   * Used as fallback when no talmudSource is available.
   * e.g. המכלול | TorahCalc
   */
  refSource?: UnitSource
  /** @deprecated use talmudSource / refSource */
  source?: string | UnitSource | UnitSource[]
}

// ── Metric/imperial opinion factors ──────────────────────────────────────────

// ── Metric/imperial opinion factors ──────────────────────────────────────────
//
// הקשר בין מידות אורך לנפח — מבוסס על תלמוד בבלי פסחים קט א:
//   "רביעית = אצבעיים על אצבעיים ברום אצבעיים וחצי וחומש"
//   = 2 × 2 × 2.7 = 10.8 אצבעות מעוקבות [המכלול; דף יומי שקלים ט]
//
// ולפיכך: volumeMlPerBeitza = (10.8 × etzba³) / 1.5
//   כי רביעית = 1.5 כביצות, ו-רביעית = 10.8 × etzba³
//   → כביצה = 10.8 × etzba³ / 1.5 = 7.2 × etzba³
//
// אימות:
//   נאה:    7.2 × 2.0³ = 7.2 × 8     = 57.6 מ"ל ✓
//   מרגולין: 7.2 × 1.9³ = 7.2 × 6.859 = 49.4 מ"ל ≈ 50 ✓
//   חזו"א:  7.2 × 2.38³ = 7.2 × 13.48 = 97.1 מ"ל (≈99.53 עם תיקון צורת הכלי)
//
// גם: מקווה = 3 אמות³ = 40 סאה [המכלול; תלמוד עירובין ד ב]
//   נאה: 3 × 48³ = 331,776 סמ"ק = 5760 כביצות × 57.6 מ"ל ✓
//
// המסקנה: volumeMlPerBeitza נגזר מ-lengthCmPerEtzba — הם אינם עצמאיים!
// הערכים שלהלן עקביים עם הנוסחה הזו.

export interface MetricFactors {
  /** cm per אצבע */
  lengthCmPerEtzba: number
  /** ml per כביצה — נגזר מ: 7.2 × etzba³ (ראה הסבר לעיל) */
  volumeMlPerBeitza: number
  /** grams per גרה */
  weightGramsPerGerah: number
  /** rounded practical values used in sefarim (e.g. הידורי המידות) */
  rounded?: MetricFactors
}

/**
 * ר' אברהם חיים נאה — שיעורי תורה (1947) ג:כה עמ' 249-250
 * אצבע = 2.0 ס"מ → טפח = 8 ס"מ → אמה = 48 ס"מ [המכלול; TC; Wiki; הידורי המידות]
 * כביצה = 57.6 מ"ל ("בגימטריא כוס") [הידורי המידות; המכלול]
 *   נגזר מנוסחת הקוביה: 7.2 × 2.0³ = 57.6 מ"ל ✓
 *   מבוסס על: רמב"ם — רביעית = 27 דרהם; דרהם = 3.2 גרם → רביעית = 86.4 מ"ל → כביצה = 57.6 מ"ל
 * גרה = 0.48 גרם [המכלול: שקל = 9.6 גרם = 20 גרות → גרה = 0.48 גרם]
 */
export const NAEH: MetricFactors = {
  lengthCmPerEtzba: 2.0,
  volumeMlPerBeitza: 57.6,
  weightGramsPerGerah: 0.48,
  // נאה: הערכים המדויקים והמעוגלים זהים — אין הבדל
}

/**
 * ערוך השולחן / משנה ברורה
 * AH YD 286:21 (4 אמות = 1 Russian sazhen = 7 feet); MB 358:7 [TC source 2]
 * אצבע = 2.223 ס"מ → אמה = 53.35 ס"מ
 * כביצה = 57.6 מ"ל — עוקבים אחר שיטת נאה בנפח הביצה [TC; לא מאושר במכלול]
 * הערה: הנוסחה הקובייתית נותנת 7.2 × 2.223³ = 79.1 מ"ל, אך בפועל פוסקים אלו
 *   לא חלקו על נאה בשאלת גודל הביצה — רק על מידות האורך
 * אין ערכים מעוגלים מאושרים ממקור — לא מוצגת אפשרות מעוגל
 */
export const ARUCH_HASHULCHAN: MetricFactors = {
  lengthCmPerEtzba: 2.223,
  volumeMlPerBeitza: 57.6,
  weightGramsPerGerah: 0.48,
}

/**
 * ר' משה פיינשטיין — אגרות משה OC א:קלו עמ' 228
 * אצבע = 2.249 ס"מ [TC source 3]
 * כביצה = 57.6 מ"ל — עוקב אחר שיטת נאה בנפח הביצה [TC; לא מאושר במכלול]
 * הערה: כנ"ל — ר"מ פיינשטיין חלק על מידות האורך בלבד, לא על גודל הביצה
 * אין ערכים מעוגלים מאושרים ממקור — לא מוצגת אפשרות מעוגל
 */
export const RAV_MOSHE: MetricFactors = {
  lengthCmPerEtzba: 2.249,
  volumeMlPerBeitza: 57.6,
  weightGramsPerGerah: 0.48,
}

/**
 * חזון איש / סטייפלר — שיעורין של תורה (ר' יעקב ישראל קנייבסקי, בשם החזון איש) עמ' 3
 * הסטייפלר כתב את שיעורין של תורה כתמיכה בשיטת החזון איש נגד שיטת הגר"ח נאה (1947)
 * אצבע ≈ 2.4 ס"מ (מדויק: 2.405 ס"מ) → טפח = 9.6 ס"מ → אמה ≈ 57.6–58 ס"מ
 *   [שיעורין של תורה עמ' 3; הידורי המידות; המכלול; TC; Wiki]
 *   מבוסס על: נודע ביהודה (צל"ח פסחים קטז ע"א) — מדד אצבע = 2.4 ס"מ
 * כביצה = 99.53 מ"ל → רביעית ≈ 149.3 מ"ל [המכלול]
 *   נגזר מנוסחת הקוביה: 7.2 × 2.38³ = 7.2 × 13.48 = 97.1 מ"ל
 *   (ההפרש מ-99.53 נובע מתיקון צורת הכלי — גליל vs. קוביה, ראה תוספות פסחים קט א)
 *   הידורי המידות מעגל ל-150 מ"ל ("בגימטריא כוס הגון") — הערך המדויק הוא 99.53
 * גרה = 0.48 גרם (weight system same across opinions) [המכלול]
 */
export const CHAZON_ISH: MetricFactors = {
  lengthCmPerEtzba: 2.38,
  volumeMlPerBeitza: 99.53,
  weightGramsPerGerah: 0.48,
  // מעוגל: אצבע = 2.4 ס"מ, כביצה = 100 מ"ל, רביעית = 150 מ"ל
  // מקור: הידורי המידות (תרשים הכוס); שיעורין של תורה; מקור ג' השיטות שהוצג
  rounded: { lengthCmPerEtzba: 2.4, volumeMlPerBeitza: 100, weightGramsPerGerah: 0.48 },
}

/**
 * שיטת הדרהם הקטן — [המכלול: "שיטת הדרהם הקטן"]
 * מבוססת על דרהם מצרי קדום קטן יותר מהדרהם הטורקי שהיה בידי הגר"ח נאה
 * משקל הדרהם המצרי ≈ 2.8 גרם (במקום 3.2 גרם) → הפחתה של כ-5% ממידות נאה
 * אצבע ≈ 1.9 ס"מ → טפח ≈ 7.6 ס"מ → אמה ≈ 45.6 ס"מ [המכלול; הידורי המידות]
 * כביצה = 7.2 × 1.9³ = 49.4 מ"ל → רביעית ≈ 74.1 מ"ל [נגזר מנוסחת הקוביה]
 *   הידורי המידות (ר' הדר מרגולין) מעגל ל-50 מ"ל / 75 מ"ל לצורך שימוש מעשי
 * כזית: לא יותר מ-20 סמ"ק [הידורי המידות]
 * "שיטה נוספת, פחות מקובלת" [לשון המכלול]
 * גרה = 0.48 גרם (weight system same across opinions)
 */
export const MARGOLIN: MetricFactors = {
  lengthCmPerEtzba: 1.9,
  volumeMlPerBeitza: 49.4,
  weightGramsPerGerah: 0.48,
  // מעוגל: כביצה = 50 מ"ל, רביעית = 75 מ"ל
  // מקור: הידורי המידות (תרשים הכוס — סימון 75 מ"ל); מקור ג' השיטות שהוצג
  rounded: { lengthCmPerEtzba: 1.9, volumeMlPerBeitza: 50, weightGramsPerGerah: 0.48 },
}

/**
 * מידות התורה המדויקות — שלמה ב.ד הכהן (תשפ"ג)
 * https://tora-forum.co.il/attachments/מידות-התורה-המדויקות-pdf.101762/
 *
 * אצבע (אגודל) = 1.94 ס"מ (ממוצע טווח 1.84–2.04) → טפח = 7.77 ס"מ → אמה = 46.63 ס"מ
 * כביצה = 52.82 מ"ל (מהטבלה המסכמת; נגזר: 7.2 × 1.94³ ≈ 52.57)
 * גרה = 0.73392 גרם (שקל = 14.6784 גרם = 20 גרות → גרה = 14.6784/20)
 *   שיטת המשקל שונה לחלוטין מכל שאר הדעות — מבוססת על מחקר עצמאי של המחבר
 */
export const MIDOT_HATORA_MEDUYAKOT: MetricFactors = {
  lengthCmPerEtzba: 1.94,
  volumeMlPerBeitza: 52.82,
  weightGramsPerGerah: 0.73392,
  // טווח: אצבע 1.84–2.04, כביצה ~50.1–55.4 מ"ל
  rounded: { lengthCmPerEtzba: 1.94, volumeMlPerBeitza: 52.82, weightGramsPerGerah: 0.73392 },
}

export const ALL_OPINIONS = {
  naeh: NAEH,
  margolin: MARGOLIN,
  aruchHashulchan: ARUCH_HASHULCHAN,
  ravMoshe: RAV_MOSHE,
  chazonIsh: CHAZON_ISH,
  midotHatoraMeduyakot: MIDOT_HATORA_MEDUYAKOT,
} as const

export type OpinionKey = keyof typeof ALL_OPINIONS
