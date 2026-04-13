/**
 * Midot — converter for biblical, Talmudic, and modern units of measure.
 * Vendored and extended from https://github.com/AmarShaked/Midotjs (ISC license)
 *
 * Metric conversion supports two halachic opinions:
 *  - ר' חיים נאה (Naeh): שיעורי תורה (1947) — widely accepted practice
 *  - חזון איש: שיעורין של תורה — stringent Ashkenazic/Litvish opinion
 *
 * Modern units (metric/imperial) are stored with anchors based on Naeh factors.
 * When converting between two modern units the opinion cancels out (ratio is exact).
 * When converting between a Talmudic and a modern unit the opinion factor applies.
 */

import { LENGTH, VOLUME, WEIGHT, COINS, TIME, NAEH, CHAZON_ISH } from './midotDefinitions'
import type { MeasurementSystem, MetricFactors } from './midotDefinitions'

export type MetricOpinion = 'naeh' | 'chazonIsh'

const ALL_UNITS = { ...LENGTH, ...VOLUME, ...WEIGHT, ...COINS, ...TIME }

// Modern unit keys per system — their anchors are stored in Naeh-space
const MODERN_LENGTH = new Set(['מ״מ', 'ס"מ', 'מ׳', 'ק"מ', "אינץ'", 'רגל', 'יארד', 'מייל'])
const MODERN_VOLUME = new Set(['מ"ל', 'ל׳', 'כף', 'כפית', 'fl oz', 'כוס', 'פינט', 'קווארט', 'גלון'])
const MODERN_WEIGHT = new Set(['גרם', 'ק"ג', 'אונס', 'ליברה'])
const MODERN_TIME = new Set(['שנייה', 'דקה', 'שעה מודרנית'])

function isModern(unit: string, system: MeasurementSystem): boolean {
  switch (system) {
    case 'length':
      return MODERN_LENGTH.has(unit)
    case 'volume':
      return MODERN_VOLUME.has(unit)
    case 'weight':
      return MODERN_WEIGHT.has(unit)
    case 'time':
      return MODERN_TIME.has(unit)
    default:
      return false
  }
}

function getFactors(opinion: MetricOpinion): MetricFactors {
  return opinion === 'naeh' ? NAEH : CHAZON_ISH
}

/**
 * Scale factor to apply to a unit's anchor when the opinion differs from Naeh.
 * Modern units are stored in Naeh-space; Talmudic units scale with opinion.
 * - Talmudic unit under CI: anchor stays the same (it's in אצבעות/כביצות/גרות)
 *   but the real-world size of the base unit changes.
 * - Modern unit: anchor is in Naeh-space cm/ml/g; stays fixed.
 *
 * To convert Talmudic → modern under opinion O:
 *   result = (talmudic_anchor / modern_anchor) * (O_factor / Naeh_factor)
 *
 * We implement this by adjusting the Talmudic unit's effective anchor:
 *   effective_anchor = talmudic_anchor * (O_factor / Naeh_factor)
 */
function effectiveAnchor(unit: string, system: MeasurementSystem, opinion: MetricOpinion): number {
  const u = ALL_UNITS[unit]!
  if (isModern(unit, system) || opinion === 'naeh') return u.anchor

  const f = getFactors(opinion)
  switch (system) {
    case 'length':
      return u.anchor * (f.lengthCmPerEtzba / NAEH.lengthCmPerEtzba)
    case 'volume':
      return u.anchor * (f.volumeMlPerBeitza / NAEH.volumeMlPerBeitza)
    case 'weight':
      return u.anchor * (f.weightGramsPerGerah / NAEH.weightGramsPerGerah)
    default:
      return u.anchor
  }
}

// ── Unit-to-unit conversion ───────────────────────────────────────────────────

export function convert(
  value: number,
  from: string,
  to: string,
  opinion: MetricOpinion = 'naeh',
): number {
  if (from === to) return value

  const fromUnit = ALL_UNITS[from]
  const toUnit = ALL_UNITS[to]

  if (!fromUnit) throw new Error(`יחידה לא מוכרת: ${from}`)
  if (!toUnit) throw new Error(`יחידה לא מוכרת: ${to}`)
  if (fromUnit.system !== toUnit.system) {
    throw new Error(`לא ניתן להמיר בין ${fromUnit.system} ל-${toUnit.system}`)
  }

  const fromAnchor = effectiveAnchor(from, fromUnit.system, opinion)
  const toAnchor = effectiveAnchor(to, toUnit.system, opinion)

  return (value * fromAnchor) / toAnchor
}

// ── Metric display (for the hint line under each block) ──────────────────────

export function toMetric(
  value: number,
  unit: string,
  opinion: MetricOpinion,
): { value: number; metricUnit: string } | null {
  const u = ALL_UNITS[unit]
  if (!u) throw new Error(`יחידה לא מוכרת: ${unit}`)

  const f = getFactors(opinion)

  switch (u.system) {
    case 'length': {
      if (isModern(unit, 'length')) return null // already metric/imperial
      const cm = value * u.anchor * f.lengthCmPerEtzba
      if (cm >= 100000) return { value: cm / 100000, metricUnit: 'ק"מ' }
      if (cm >= 100) return { value: cm / 100, metricUnit: 'מ׳' }
      if (cm < 1) return { value: cm * 10, metricUnit: 'מ"מ' }
      return { value: cm, metricUnit: 'ס"מ' }
    }
    case 'volume': {
      if (isModern(unit, 'volume')) return null
      const ml = value * u.anchor * f.volumeMlPerBeitza
      if (ml >= 1000) return { value: ml / 1000, metricUnit: 'ל׳' }
      return { value: ml, metricUnit: 'מ"ל' }
    }
    case 'weight': {
      if (isModern(unit, 'weight')) return null
      const g = value * u.anchor * f.weightGramsPerGerah
      if (g >= 1000) return { value: g / 1000, metricUnit: 'ק"ג' }
      return { value: g, metricUnit: 'גרם' }
    }
    default:
      return null
  }
}

// ── Formatting ────────────────────────────────────────────────────────────────

export function formatResult(value: number): string {
  if (!isFinite(value) || isNaN(value)) return '—'
  return parseFloat(value.toPrecision(8)).toString()
}

// ── Exports ───────────────────────────────────────────────────────────────────

export const LENGTH_UNIT_NAMES = Object.keys(LENGTH)
export const VOLUME_UNIT_NAMES = Object.keys(VOLUME)
export const WEIGHT_UNIT_NAMES = Object.keys(WEIGHT)
export const COINS_UNIT_NAMES = Object.keys(COINS)
export const TIME_UNIT_NAMES = Object.keys(TIME)

export type { MeasurementSystem }
export { NAEH, CHAZON_ISH }
