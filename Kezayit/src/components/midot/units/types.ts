/**
 * Shared types for all unit definition files.
 */

export type MeasurementSystem = 'length' | 'area' | 'volume' | 'weight' | 'coins' | 'time'

export interface Unit {
  anchor: number // ratio relative to system base unit
  system: MeasurementSystem
  heDesc?: string // short Hebrew description
  disputed?: string // note when ratio is disputed between authorities
}

// ── Metric/imperial opinion factors ──────────────────────────────────────────

export interface MetricFactors {
  /** cm per אצבע */
  lengthCmPerEtzba: number
  /** ml per כביצה */
  volumeMlPerBeitza: number
  /** grams per גרה */
  weightGramsPerGerah: number
}

/**
 * ר' אברהם חיים נאה — שיעורי תורה (1947) ג:כה עמ' 249-250
 * אצבע = 2.0 ס"מ → אמה = 48 ס"מ [TC, Wiki, JTA]
 * כביצה = 54 מ"ל [SAR, TC — Shiurei Torah 3:9]
 * גרה = 0.7 גרם (שקל ≈ 14 גרם) [TC]
 */
export const NAEH: MetricFactors = {
  lengthCmPerEtzba: 2.0,
  volumeMlPerBeitza: 54,
  weightGramsPerGerah: 0.7,
}

/**
 * ערוך השולחן / משנה ברורה
 * AH YD 286:21 (4 אמות = 1 Russian sazhen = 7 feet); MB 358:7 [TC source 2]
 * אצבע = 2.223 ס"מ → אמה = 53.35 ס"מ
 * כביצה = 54 מ"ל (follows Naeh on egg volume) [SAR]
 */
export const ARUCH_HASHULCHAN: MetricFactors = {
  lengthCmPerEtzba: 2.223,
  volumeMlPerBeitza: 54,
  weightGramsPerGerah: 0.75,
}

/**
 * ר' משה פיינשטיין — אגרות משה OC א:קלו עמ' 228
 * אצבע = 2.249 ס"מ (מחמיר: 2.434 ס"מ) → אמה = 53.98 ס"מ [TC source 3]
 * כביצה = 57 מ"ל [SAR — follows Naeh on eggs]
 */
export const RAV_MOSHE: MetricFactors = {
  lengthCmPerEtzba: 2.249,
  volumeMlPerBeitza: 57,
  weightGramsPerGerah: 0.75,
}

/**
 * חזון איש — שיעורין של תורה (ר' יעקב קניבסקי בשם החזון איש) עמ' 3
 * אצבע = 2.38 ס"מ → אמה = 57.12 ס"מ [TC source 4, Wiki]
 * כביצה = 100 מ"ל [SAR, TC, MB 486:א]
 * גרה = 0.85 גרם (שקל ≈ 17 גרם) [TC]
 */
export const CHAZON_ISH: MetricFactors = {
  lengthCmPerEtzba: 2.38,
  volumeMlPerBeitza: 100,
  weightGramsPerGerah: 0.85,
}

export const ALL_OPINIONS = {
  naeh: NAEH,
  aruchHashulchan: ARUCH_HASHULCHAN,
  ravMoshe: RAV_MOSHE,
  chazonIsh: CHAZON_ISH,
} as const

export type OpinionKey = keyof typeof ALL_OPINIONS
