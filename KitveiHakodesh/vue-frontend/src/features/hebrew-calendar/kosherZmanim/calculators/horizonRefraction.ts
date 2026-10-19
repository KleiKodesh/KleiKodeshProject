// Ported from KosherKotlin (LGPL-2.1), https://github.com/kdroidFilter/KosherKotlin commit 582e904,
// zmanim/src/commonMain/kotlin/io/github/kdroidfilter/kosherkotlin/util/HorizonRefraction.kt
import type { Atmosphere } from './atmosphere'
import { indexOfLast } from '../indexOfLast'
import * as AtmosphereModels from './atmosphereModels'

/**
 * Monthly climatology of a place (degrees C, January first), which drives the refraction of the horizon: minimum
 * and maximum temperatures for sunrise and sunset, mean for the dip seen from an elevation, measured at [elevation]
 * meters. [STANDARD_CLIMATE] is the 15 C standard atmosphere at sea level, every month.
 */
export class ItimLabinaClimate {
  constructor(
    readonly minimum: readonly number[],
    readonly maximum: readonly number[],
    readonly mean: readonly number[],
    readonly elevation = 0.0,
  ) {
    if (minimum.length !== 12 || maximum.length !== 12 || mean.length !== 12) throw new Error('One value per month')
  }
}

const fifteenEveryMonth = () => Array.from({ length: 12 }, () => 15.0)
export const STANDARD_CLIMATE = new ItimLabinaClimate(fifteenEveryMonth(), fifteenEveryMonth(), fifteenEveryMonth())

/** Surface correction: [lapse] in K/m, k morning and evening, and the share of the daily range at sunset. */
interface Correction {
  readonly lapse: number
  readonly morningScale: number
  readonly eveningScale: number
  readonly eveningShare: number
}

const ISRAEL: Correction = { lapse: 0.00367, morningScale: 0.98878, eveningScale: 0.99227, eveningShare: 0.5927 }
const ELSEWHERE: Correction = { lapse: 0.0063, morningScale: 0.99141, eveningScale: 0.99491, eveningShare: 0.5927 }

/** The dip's refraction numerator C(h), in K^2, sampled over ln h; it drops by 3 % above 1000 m. */
const DIP_LOW_H = [50.0, 100.0, 200.0, 300.0, 500.0, 700.0, 800.0, 900.0, 1000.0]
const DIP_LOW_C = [
  6993.222, 6985.416, 6977.447, 6971.939, 6965.847, 6961.634, 6960.139, 6958.717, 6957.381,
]
const DIP_HIGH_H = [1100.0, 1200.0, 1300.0, 1400.0, 1500.0, 1600.0, 2000.0, 2500.0, 3000.0, 3500.0, 4000.0]
const DIP_HIGH_C = [
  6743.439, 6740.706, 6737.905, 6735.475, 6733.212, 6730.913, 6723.462, 6716.106, 6709.922, 6704.669, 6700.149,
]

/**
 * Refraction of the horizon: the horizontal ray is traced through a model atmosphere (Dr. Menat's winter or summer
 * atmosphere in Israel, the AFGL/LOWTRAN ones elsewhere) from the observer's height, floored to 100 m, then scaled
 * by the model's temperature over the place's, both at the observer's height:
 *
 *     R = k * R_model(h) * T_model(h) / (T + L * (h_climate - h))
 *
 * T being the month's minimum temperature at sunrise, and at sunset the minimum plus 59 % of the daily range.
 * In degrees, without the semidiameter. [month] is 0-based.
 */
export function horizonRefractionDegrees(
  latitude: number,
  longitude: number,
  climate: ItimLabinaClimate,
  month: number,
  morning: boolean,
  elevation: number,
): number {
  const israel = isInIsrael(latitude, longitude)
  const atmosphere = atmosphereFor(latitude, israel, isWinter(climate, israel, latitude, month))
  const correction = israel ? ISRAEL : ELSEWHERE
  const min = climate.minimum[month]!
  const celsius = morning ? min : min + correction.eveningShare * (climate.maximum[month]! - min)
  const observed = celsius + 273.15 + correction.lapse * (climate.elevation - elevation)
  const scale = morning ? correction.morningScale : correction.eveningScale
  return scale * atmosphere.refraction(elevation) * atmosphere.temperature(Math.floor(elevation / 100) * 100) / observed
}

/**
 * Dip of the horizon seen from [elevation], in degrees: the geometric dip over the WGS84 geocentric radius,
 * deepened by C(h) / T^2, T being the month's mean temperature rounded to the degree.
 */
export function horizonDipDegrees(latitude: number, climate: ItimLabinaClimate, month: number, elevation: number): number {
  if (elevation <= 0) return 0.0
  const radius = geocentricRadius(latitude)
  const geometric = Math.acos(radius / (radius + elevation)) * 180 / Math.PI
  const kelvin = Math.floor(climate.mean[month]! + 0.5) + 273.15
  return geometric * (1 + dipNumerator(elevation) / (kelvin * kelvin))
}

function atmosphereFor(latitude: number, israel: boolean, winter: boolean): Atmosphere {
  if (israel) return winter ? AtmosphereModels.MENAT_WINTER : AtmosphereModels.MENAT_SUMMER
  if (Math.abs(latitude) < 15) return AtmosphereModels.TROPICAL
  if (Math.abs(latitude) >= 60) return winter ? AtmosphereModels.SUBARCTIC_WINTER : AtmosphereModels.SUBARCTIC_SUMMER
  return winter ? AtmosphereModels.MIDLATITUDE_WINTER : AtmosphereModels.MIDLATITUDE_SUMMER
}

/**
 * Israel: November to April. Elsewhere the season follows the city's own climate, month by month:
 * winter when the month's mean temperature is at or below the yearly average of the monthly means
 * (matches itimlabina.co.il, e.g. Kyiv/Mexico City winter October-March, London/New York November-April).
 * A flat climate (no data, e.g. STANDARD_CLIMATE) falls back to November-April, mirrored south of the equator.
 */
function isWinter(climate: ItimLabinaClimate, israel: boolean, latitude: number, month: number): boolean {
  const flat = climate.mean.every((mean) => mean === climate.mean[0])
  if (israel || flat) {
    const northern = latitude >= 0 ? month : (month + 6) % 12
    return northern >= 10 || northern <= 3
  }
  const yearly = climate.mean.reduce((sum, mean) => sum + mean, 0) / 12
  return climate.mean[month]! <= yearly
}

function dipNumerator(elevation: number): number {
  return elevation <= 1000
    ? interpolateLn(DIP_LOW_H, DIP_LOW_C, elevation)
    : interpolateLn(DIP_HIGH_H, DIP_HIGH_C, elevation)
}

/** Linear in ln h between samples, extrapolated from the end segments. */
function interpolateLn(hs: readonly number[], cs: readonly number[], h: number): number {
  const i = Math.min(Math.max(indexOfLast(hs, sample => sample <= h), 0), hs.length - 2)
  const t = (Math.log(h) - Math.log(hs[i]!)) / (Math.log(hs[i + 1]!) - Math.log(hs[i]!))
  return cs[i]! + t * (cs[i + 1]! - cs[i]!)
}

/** WGS84 distance from the center of the Earth at [latitude], in meters. */
function geocentricRadius(latitude: number): number {
  const a = 6_378_137.0
  const b = a * (1 - 1 / 298.257223563)
  const c = Math.cos(latitude * Math.PI / 180)
  const s = Math.sin(latitude * Math.PI / 180)
  return Math.sqrt(((a * a * c) * (a * a * c) + (b * b * s) * (b * b * s)) / ((a * c) * (a * c) + (b * s) * (b * s)))
}

/** A bounding box; the few km it overlaps with Sinai, Jordan and Lebanon share Israel's atmosphere. */
function isInIsrael(latitude: number, longitude: number): boolean {
  return latitude >= 29.4 && latitude <= 33.4 && longitude >= 34.2 && longitude <= 35.9
}
