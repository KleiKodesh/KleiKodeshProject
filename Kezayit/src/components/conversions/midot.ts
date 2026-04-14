/**
 * Midot — converter for biblical, Talmudic, and modern units of measure.
 * Vendored and extended from https://github.com/AmarShaked/Midotjs (ISC license)
 *
 * Modern units are stored with Naeh-space anchors.
 * When converting between two modern units the opinion cancels out exactly.
 * When crossing Talmudic ↔ modern the opinion factor scales the Talmudic side.
 */

import { LENGTH, AREA, VOLUME, WEIGHT, COINS, TIME, NAEH, ALL_OPINIONS } from './units'
import type { MeasurementSystem, MetricFactors, OpinionKey, UnitSource } from './units'

export type { OpinionKey }

export type MetricOpinion = 'naeh' | 'chazonIsh'

// ── All units in one lookup map ───────────────────────────────────────────────

const ALL_UNITS = { ...LENGTH, ...AREA, ...VOLUME, ...WEIGHT, ...COINS, ...TIME }

// ── Modern unit sets (stored in Naeh-space, opinion-independent in real world) ─

const MODERN: Partial<Record<MeasurementSystem, Set<string>>> = {
  length: new Set(['מ"מ', 'ס"מ', "מ'", 'ק"מ', "אינץ'", 'רגל', 'יארד', 'מייל']),
  area: new Set(['מ"מ²', 'ס"מ²', "מ'²", 'דונם', 'הקטאר', "אינץ'²", 'רגל²', 'יארד²', 'אקר']),
  volume: new Set([
    'מ"ל',
    "ל'",
    'כפית',
    'כף',
    'fl oz',
    'כוס',
    'פינט',
    'קווארט',
    'גלון',
    'UK fl oz',
    'UK פינט',
    'UK קווארט',
    'UK גאלון',
  ]),
  weight: new Set(['גרם', 'ק"ג', 'טון', 'אונס', 'ליברה', 'סטון', 'אונקיה טרוי']),
  time: new Set(['שנייה', 'דקה', 'שעה (מודרנית)', 'יום (מודרני)', 'שבוע_מ']),
}

function getFactors(opinion: OpinionKey, useRounded: boolean): MetricFactors {
  const f = ALL_OPINIONS[opinion]
  return useRounded && f.rounded ? f.rounded : f
}

function isModern(unit: string, system: MeasurementSystem): boolean {
  return MODERN[system]?.has(unit) ?? false
}

// ── Opinion scaling ───────────────────────────────────────────────────────────
// Talmudic units are in abstract base-unit space (אצבעות / כביצות / גרות).
// Modern units are pinned to real-world cm/ml/g via Naeh factors.
// To convert Talmudic → modern under opinion O:
//   effective_anchor(talmudic) = anchor × (O_factor / Naeh_factor)
// Modern units keep their anchor unchanged.

function effectiveAnchor(
  unit: string,
  system: MeasurementSystem,
  opinion: OpinionKey,
  useRounded: boolean,
): number {
  const u = ALL_UNITS[unit]!
  if (isModern(unit, system) || opinion === 'naeh') return u.anchor

  const f: MetricFactors = getFactors(opinion, useRounded)
  switch (system) {
    case 'length':
      return u.anchor * (f.lengthCmPerEtzba / NAEH.lengthCmPerEtzba)
    case 'area':
      return u.anchor * (f.lengthCmPerEtzba / NAEH.lengthCmPerEtzba) ** 2
    case 'volume':
      return u.anchor * (f.volumeMlPerBeitza / NAEH.volumeMlPerBeitza)
    case 'weight':
      return u.anchor * (f.weightGramsPerGerah / NAEH.weightGramsPerGerah)
    default:
      return u.anchor
  }
}

// ── Core conversion ───────────────────────────────────────────────────────────

export function convert(
  value: number,
  from: string,
  to: string,
  opinion: OpinionKey = 'naeh',
  useRounded = false,
): number {
  if (from === to) return value

  const fromUnit = ALL_UNITS[from]
  const toUnit = ALL_UNITS[to]

  if (!fromUnit) throw new Error(`יחידה לא מוכרת: ${from}`)
  if (!toUnit) throw new Error(`יחידה לא מוכרת: ${to}`)
  if (fromUnit.system !== toUnit.system)
    throw new Error(`לא ניתן להמיר בין ${fromUnit.system} ל-${toUnit.system}`)

  return (
    (value * effectiveAnchor(from, fromUnit.system, opinion, useRounded)) /
    effectiveAnchor(to, toUnit.system, opinion, useRounded)
  )
}

// ── Metric hint (shown below each block) ─────────────────────────────────────

export function toMetric(
  value: number,
  unit: string,
  opinion: OpinionKey,
  useRounded = false,
): { value: number; metricUnit: string } | null {
  const u = ALL_UNITS[unit]
  if (!u) return null
  if (isModern(unit, u.system)) return null

  const f = getFactors(opinion, useRounded)

  switch (u.system) {
    case 'length': {
      const cm = value * u.anchor * f.lengthCmPerEtzba
      if (cm >= 100000) return { value: cm / 100000, metricUnit: 'ק"מ' }
      if (cm >= 100) return { value: cm / 100, metricUnit: "מ'" }
      if (cm < 1) return { value: cm * 10, metricUnit: 'מ"מ' }
      return { value: cm, metricUnit: 'ס"מ' }
    }
    case 'area': {
      const cm2 = value * u.anchor * f.lengthCmPerEtzba ** 2
      if (cm2 >= 1e10) return { value: cm2 / 1e10, metricUnit: 'ק"מ²' }
      if (cm2 >= 1e8) return { value: cm2 / 1e8, metricUnit: 'הקטאר' }
      if (cm2 >= 1e7) return { value: cm2 / 1e7, metricUnit: 'דונם' }
      if (cm2 >= 10000) return { value: cm2 / 10000, metricUnit: "מ'²" }
      return { value: cm2, metricUnit: 'ס"מ²' }
    }
    case 'volume': {
      const ml = value * u.anchor * f.volumeMlPerBeitza
      if (ml >= 1000) return { value: ml / 1000, metricUnit: "ל'" }
      return { value: ml, metricUnit: 'מ"ל' }
    }
    case 'weight': {
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

// ── Conversion explanation ────────────────────────────────────────────────────
// Returns { calc, source } describing HOW the conversion was derived.

export interface ConversionExplanation {
  calc: string // e.g. "24 אצבעות × 2.0 ס"מ = 48 ס"מ"
  source: UnitSource // the authoritative source for this specific conversion
}

export function explainConversion(
  from: string,
  to: string,
  opinion: OpinionKey,
  useRounded: boolean,
): ConversionExplanation | null {
  const fromUnit = ALL_UNITS[from]
  const toUnit = ALL_UNITS[to]
  if (!fromUnit || !toUnit || fromUnit.system !== toUnit.system) return null

  const f = getFactors(opinion, useRounded)
  const fromModern = isModern(from, fromUnit.system)
  const toModern = isModern(to, toUnit.system)

  // ── Opinion sources per posek ──────────────────────────────────────────────
  const OPINION_SOURCES: Record<string, UnitSource> = {
    naeh: { label: 'ר"ח נאה, שיעורי תורה ג:כה' },
    margolin: { label: 'הידורי המידות (ר׳ הדר מרגולין)' },
    aruchHashulchan: { label: 'ערוך השולחן YD רפו:כא' },
    ravMoshe: { label: 'ר"מ פיינשטיין, אגרות משה OC א:קלו' },
    chazonIsh: { label: 'חזו"א / סטייפלר, שיעורין של תורה עמ׳ 3' },
  }

  // ── Both modern: pure SI math ──────────────────────────────────────────────
  if (fromModern && toModern) {
    const ratio = formatResult(fromUnit.anchor / toUnit.anchor)
    return {
      calc: `1 ${from} = ${ratio} ${to}`,
      source: { label: 'SI — הגדרה מדויקת בינלאומית' },
    }
  }

  // ── Talmudic → metric (or vice versa) ─────────────────────────────────────
  if (fromModern !== toModern) {
    const talmudUnit = fromModern ? to : from
    const metricUnit = fromModern ? from : to
    const u = ALL_UNITS[talmudUnit]!
    const sys = u.system

    let calc = ''
    if (sys === 'length') {
      const etzba = f.lengthCmPerEtzba
      calc = `${talmudUnit} = ${u.anchor} אצבעות × ${etzba} ס"מ = ${formatResult(u.anchor * etzba)} ס"מ`
    } else if (sys === 'volume') {
      const ml = f.volumeMlPerBeitza
      calc = `${talmudUnit} = ${u.anchor} כביצות × ${ml} מ"ל = ${formatResult(u.anchor * ml)} מ"ל`
    } else if (sys === 'weight') {
      const g = f.weightGramsPerGerah
      calc = `${talmudUnit} = ${u.anchor} גרות × ${g} גרם = ${formatResult(u.anchor * g)} גרם`
    } else if (sys === 'area') {
      const etzba = f.lengthCmPerEtzba
      calc = `${talmudUnit} = ${u.anchor} אצבעות² × ${etzba}² = ${formatResult(u.anchor * etzba ** 2)} ס"מ²`
    }

    return {
      calc,
      source: OPINION_SOURCES[opinion] ?? { label: opinion },
    }
  }

  // ── Both Talmudic: ratio from anchor chain ─────────────────────────────────
  const ratio = fromUnit.anchor / toUnit.anchor
  const ratioStr = Number.isInteger(ratio) ? ratio.toString() : formatResult(ratio)
  const calc = `1 ${from} = ${ratioStr} ${to}`

  // Use talmudSource of the "from" unit, fall back to refSource
  const source = fromUnit.talmudSource ??
    fromUnit.refSource ?? {
      label: 'המכלול',
      url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
    }

  return { calc, source }
}

// ── Unit name lists ───────────────────────────────────────────────────────────

export const LENGTH_UNIT_NAMES = Object.keys(LENGTH)
export const AREA_UNIT_NAMES = Object.keys(AREA)
export const VOLUME_UNIT_NAMES = Object.keys(VOLUME)
export const WEIGHT_UNIT_NAMES = Object.keys(WEIGHT)
export const COINS_UNIT_NAMES = Object.keys(COINS)
export const TIME_UNIT_NAMES = Object.keys(TIME)
