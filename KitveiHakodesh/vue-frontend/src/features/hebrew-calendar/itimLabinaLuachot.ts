// What the Itim Labina luach (itimlabina.co.il) shows for each of the app's zmanim rows, per luach, and how each
// of those zmanim is defined. Taken from their config (GET https://itimlabina.co.il/api/boot/data): the zman in the
// slot of their web daily list (view 127) for the row, resolved for the luach (the luach's own entries, else its
// parent luach's, else the zman group's default), with that zman's definition and default round type. The slots:
// chatzot halaila 557, alot 397, misheyakir 443, netz 436, sof zman shma 44 / 438, sof zman tfila 43 / 505, chatzot
// 13, mincha gedola 49, mincha ketana 75, plag 506, shkia 290, tzeit 110. Their netz and, in a few luachot, their
// shkia are the visible ones (terrain based), which cannot be computed here, so every luach uses their sea-level
// netz (14) and those luachot their shkia from the height (58). The luach and city tables are generated from boot.json.
import type { CalendarZmanim, CityKey } from './calendarTypes'

/** An Itim Labina zman id (`zmanimList[].id`). */
export type ZmanId = number

/** How their client rounds and prints a time (see itimLabinaTimeFormat.ts). */
export type RoundType = 'plusminus' | '15sc' | 'candle1'

/** How a zman is computed. Degrees are below the horizon (their config stores them negative). */
export type ZmanRule =
  /** The sun this far below the apparent horizon of the Itim Labina refraction (their gloyberman methods). */
  | { kind: 'belowHorizon', morning: boolean, degrees: number }
  /** The sun this far below the geometric horizon (no refraction method). */
  | { kind: 'geometric', morning: boolean, degrees: number }
  /** Sunset seen from the place's elevation (full horizon dip at any height, as their server computes it). */
  | { kind: 'sunsetFromHeight' }
  /** Solar noon. */
  | { kind: 'transit' }
  /** Solar midnight of the night that opens the day. */
  | { kind: 'midnight' }
  /** [hours] seasonal hours (twelfths of start..end) after [start]. */
  | { kind: 'zmanit', start: ZmanId, end: ZmanId, hours: number }
  /** The later of two zmanim. */
  | { kind: 'latest', of: readonly [ZmanId, ZmanId] }
  /** Fixed minutes after another zman. */
  | { kind: 'minutesAfter', of: ZmanId, minutes: number }

export interface ZmanDefinition {
  rule: ZmanRule
  roundType: RoundType
}

/** Every zman the luachot below show, plus the ones those are built from. */
export const ZMANIM: Record<ZmanId, ZmanDefinition> = {
  557: { rule: { kind: 'midnight' }, roundType: 'plusminus' }, // chatzot halaila
  35: { rule: { kind: 'belowHorizon', morning: true, degrees: 18.9712 }, roundType: 'plusminus' }, // alot, 90 minutes
  65: { rule: { kind: 'belowHorizon', morning: true, degrees: 15.2193 }, roundType: 'plusminus' }, // alot, 72 minutes
  57: { rule: { kind: 'belowHorizon', morning: false, degrees: 15.2193 }, roundType: 'plusminus' }, // tzeit rabbeinu tam, 72 minutes
  234: { rule: { kind: 'geometric', morning: true, degrees: 12 }, roundType: 'plusminus' }, // alot, 12 degrees
  71: { rule: { kind: 'geometric', morning: true, degrees: 11.5 }, roundType: 'plusminus' }, // misheyakir, 11.5 degrees
  235: { rule: { kind: 'geometric', morning: true, degrees: 11 }, roundType: 'plusminus' }, // misheyakir, 11 degrees
  72: { rule: { kind: 'geometric', morning: true, degrees: 10.5 }, roundType: 'plusminus' }, // misheyakir, 10.5 degrees
  14: { rule: { kind: 'belowHorizon', morning: true, degrees: 0 }, roundType: '15sc' }, // netz, sea level
  52: { rule: { kind: 'belowHorizon', morning: false, degrees: 0 }, roundType: '15sc' }, // shkia, sea level
  58: { rule: { kind: 'sunsetFromHeight' }, roundType: '15sc' }, // shkia from the height
  68: { rule: { kind: 'zmanit', start: 65, end: 57, hours: 3 }, roundType: 'plusminus' }, // sof zman shma MGA, 72 minutes
  86: { rule: { kind: 'zmanit', start: 65, end: 57, hours: 4 }, roundType: 'plusminus' }, // sof zman tfila MGA, 72 minutes
  44: { rule: { kind: 'zmanit', start: 14, end: 52, hours: 3 }, roundType: 'plusminus' }, // sof zman shma GRA
  43: { rule: { kind: 'zmanit', start: 14, end: 52, hours: 4 }, roundType: 'plusminus' }, // sof zman tfila GRA
  13: { rule: { kind: 'transit' }, roundType: 'plusminus' }, // chatzot
  67: { rule: { kind: 'minutesAfter', of: 13, minutes: 30 }, roundType: 'candle1' }, // mincha gedola, 30 minutes
  48: { rule: { kind: 'zmanit', start: 14, end: 52, hours: 6.5 }, roundType: 'plusminus' }, // mincha gedola GRA
  49: { rule: { kind: 'latest', of: [67, 48] }, roundType: 'plusminus' }, // mincha gedola, the later of the two
  75: { rule: { kind: 'zmanit', start: 14, end: 52, hours: 9.5 }, roundType: 'plusminus' }, // mincha ketana GRA
  74: { rule: { kind: 'geometric', morning: false, degrees: 6.45 }, roundType: 'plusminus' }, // tzeit, 6.45 degrees
  176: { rule: { kind: 'geometric', morning: false, degrees: 7.0833 }, roundType: 'plusminus' }, // tzeit, 7.0833 degrees
  106: { rule: { kind: 'belowHorizon', morning: false, degrees: 2.86674 }, roundType: 'plusminus' }, // tzeit geonim, 13.5 minutes
  110: { rule: { kind: 'belowHorizon', morning: false, degrees: 3.8217 }, roundType: 'plusminus' }, // tzeit geonim, 18 minutes
  126: { rule: { kind: 'zmanit', start: 35, end: 74, hours: 10.75 }, roundType: 'plusminus' }, // plag, alot 90 to tzeit 6.45
  266: { rule: { kind: 'zmanit', start: 65, end: 74, hours: 10.75 }, roundType: 'plusminus' }, // plag, alot 72 to tzeit 6.45
  231: { rule: { kind: 'zmanit', start: 65, end: 106, hours: 10.75 }, roundType: 'plusminus' }, // plag, alot 72 to tzeit 13.5
  496: { rule: { kind: 'zmanit', start: 234, end: 176, hours: 10.75 }, roundType: 'plusminus' }, // plag, alot 12 to tzeit 7.0833
}

/** The rows every luach shows the same way. */
const SHARED_ROWS = {
  chatzotNight: 557,
  sunrise: 14,
  sofShmaGra: 44,
  sofShmaMga: 68,
  sofTfillaGra: 43,
  sofTfillaMga: 86,
  chatzot: 13,
  minchaGedola: 49,
  minchaKetana: 75,
  tzeit: 110,
} as const

type LuachRows = Record<keyof CalendarZmanim, ZmanId>

/** Per Itim Labina luach id: the zman shown for each row; the comment lists the locations (`locationsList[].id`). */
const LUACH_ROWS: Record<number, LuachRows> = {
  12: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 21
  16: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 1, 137, 191, 199, 202, 210, 217
  17: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 24
  18: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // locations 25, 236
  19: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 26
  20: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 27
  21: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 28, 29, 164
  22: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 31
  23: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 32
  24: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 33
  25: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 34
  26: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // locations 37, 227, 228
  29: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 41
  30: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 42
  32: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // locations 174, 209
  33: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 44
  34: { ...SHARED_ROWS, alot: 35, misheyakir: 72, plag: 126, sunset: 58 }, // locations 45, 216
  36: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 47
  37: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 48, 198
  39: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 50
  40: { ...SHARED_ROWS, alot: 35, misheyakir: 72, plag: 126, sunset: 58 }, // locations 51, 169
  41: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 266, sunset: 52 }, // locations 53, 125, 223, 224, 225, 226
  42: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 196
  43: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 55, 195
  44: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 58, 176
  45: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // locations 59, 190
  46: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 60
  47: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 61
  48: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 62
  49: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 63
  50: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 65
  51: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 66
  61: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 67
  62: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 68
  63: { ...SHARED_ROWS, alot: 35, misheyakir: 72, plag: 126, sunset: 58 }, // location 139
  66: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 71
  67: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 72, 156
  68: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 73
  69: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 74
  70: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // locations 75, 145, 146, 147, 148, 192, 200, 205, 207, 208, 229, 232, 233
  71: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 76
  72: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 126, sunset: 52 }, // location 77
  73: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 126, sunset: 52 }, // location 78
  74: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 79
  75: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 80
  76: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 81
  77: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 82
  78: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 126, sunset: 52 }, // location 83
  81: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 85
  82: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 87
  83: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 88
  84: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 89
  85: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 90
  86: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // locations 91, 153, 154, 155, 204, 206, 214, 215
  87: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 92
  88: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 93
  89: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 94
  90: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 95
  92: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 97
  93: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 98
  94: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 99
  95: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 100
  96: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 101
  97: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 102
  98: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 103
  99: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 104
  100: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 105
  101: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 106
  102: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 107
  103: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 108
  104: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 109
  105: { ...SHARED_ROWS, alot: 65, misheyakir: 235, plag: 231, sunset: 52 }, // locations 110, 122
  106: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 111
  107: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 112, 189
  108: { ...SHARED_ROWS, alot: 65, misheyakir: 235, plag: 231, sunset: 52 }, // location 113
  109: { ...SHARED_ROWS, alot: 65, misheyakir: 235, plag: 231, sunset: 52 }, // location 114
  110: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 126, sunset: 52 }, // locations 115, 222
  112: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 116, 172
  113: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 117, 170
  114: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 118
  115: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 119
  116: { ...SHARED_ROWS, alot: 65, misheyakir: 72, plag: 126, sunset: 52 }, // location 120
  118: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 126, sunset: 52 }, // location 123
  119: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 124
  120: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 127
  121: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 128
  122: { ...SHARED_ROWS, alot: 65, misheyakir: 235, plag: 231, sunset: 52 }, // location 129
  123: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 166
  125: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 131
  126: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 266, sunset: 52 }, // location 133
  127: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 231, sunset: 52 }, // location 134
  136: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // locations 14, 138, 142
  138: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 70
  139: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 141
  140: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 136
  144: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 144
  145: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 149
  146: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 151
  148: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 231, sunset: 58 }, // location 152
  150: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 157
  151: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 160
  152: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // locations 159, 235
  154: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 266, sunset: 52 }, // location 161
  155: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 162
  156: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 163
  157: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 266, sunset: 52 }, // location 165
  158: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 167
  160: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 168
  161: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 266, sunset: 58 }, // location 180
  166: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 184
  167: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 231, sunset: 52 }, // location 158
  169: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 231, sunset: 52 }, // location 173
  170: { ...SHARED_ROWS, alot: 65, misheyakir: 235, plag: 496, sunset: 52 }, // locations 187, 201
  171: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 171
  172: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 188
  173: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 231, sunset: 52 }, // location 193
  175: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 231, sunset: 52 }, // location 194
  177: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 56
  179: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 266, sunset: 58 }, // location 185
  181: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 179
  183: { ...SHARED_ROWS, alot: 65, misheyakir: 71, plag: 231, sunset: 58 }, // location 213
  184: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 218
  185: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 221
  186: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 220
  187: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 230
  188: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 231
  189: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 52 }, // location 211
  190: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 219
  192: { ...SHARED_ROWS, alot: 35, misheyakir: 71, plag: 126, sunset: 58 }, // location 234
  193: { ...SHARED_ROWS, alot: 65, misheyakir: 235, plag: 126, sunset: 52 }, // location 203
}

/**
 * The luach each city follows: its own Itim Labina location's luach. Modiin has none and falls in the region that
 * uses Jerusalem's; New York follows Brooklyn's; Montreal has none and uses the default luach.
 */
const CITY_LUACH: Record<CityKey, number> = {
  jerusalem: 16,
  telAviv: 88,
  haifa: 43,
  beerSheva: 61,
  ashdod: 17,
  netanya: 18,
  petachTikva: 22,
  rishonLezion: 84,
  bneiBrak: 32,
  ramatGan: 32,
  herzliya: 95,
  rechovot: 49,
  modiin: 16,
  eilat: 140,
  tzfat: 44,
  tiberias: 46,
  newYork: 70,
  london: 110,
  antwerp: 193,
  montreal: 136,
  itimLabina123: 118,
  itimLabina206: 86,
  itimLabina41: 29,
  itimLabina196: 42,
  itimLabina151: 146,
  itimLabina92: 87,
  itimLabina172: 112,
  itimLabina234: 192,
  itimLabina164: 21,
  itimLabina29: 21,
  itimLabina28: 21,
  itimLabina168: 160,
  itimLabina218: 184,
  itimLabina137: 16,
  itimLabina97: 92,
  itimLabina199: 16,
  itimLabina44: 33,
  itimLabina145: 70,
  itimLabina99: 94,
  itimLabina165: 157,
  itimLabina167: 158,
  itimLabina210: 16,
  itimLabina70: 138,
  itimLabina118: 114,
  itimLabina61: 47,
  itimLabina48: 37,
  itimLabina198: 37,
  itimLabina26: 19,
  itimLabina75: 70,
  itimLabina190: 45,
  itimLabina191: 16,
  itimLabina109: 104,
  itimLabina142: 136,
  itimLabina21: 12,
  itimLabina213: 183,
  itimLabina201: 170,
  itimLabina189: 107,
  itimLabina108: 103,
  itimLabina216: 34,
  itimLabina146: 70,
  itimLabina154: 86,
  itimLabina233: 70,
  itimLabina62: 48,
  itimLabina229: 70,
  itimLabina215: 86,
  itimLabina200: 70,
  itimLabina144: 144,
  itimLabina51: 40,
  itimLabina98: 93,
  itimLabina220: 186,
  itimLabina59: 45,
  itimLabina195: 43,
  itimLabina194: 175,
  itimLabina68: 62,
  itimLabina90: 85,
  itimLabina192: 70,
  itimLabina185: 179,
  itimLabina141: 139,
  itimLabina120: 116,
  itimLabina153: 86,
  itimLabina139: 63,
  itimLabina116: 112,
  itimLabina76: 71,
  itimLabina138: 136,
  itimLabina163: 156,
  itimLabina14: 136,
  itimLabina236: 18,
  itimLabina221: 185,
  itimLabina128: 121,
  itimLabina235: 152,
  itimLabina157: 150,
  itimLabina71: 66,
  itimLabina214: 86,
  itimLabina33: 24,
  itimLabina204: 86,
  itimLabina205: 70,
  itimLabina78: 73,
  itimLabina79: 74,
  itimLabina230: 187,
  itimLabina166: 123,
  itimLabina180: 161,
  itimLabina107: 102,
  itimLabina50: 39,
  itimLabina161: 154,
  itimLabina45: 34,
  itimLabina91: 86,
  itimLabina81: 76,
  itimLabina56: 177,
  itimLabina134: 127,
  itimLabina187: 170,
  itimLabina87: 82,
  itimLabina94: 89,
  itimLabina111: 106,
  itimLabina188: 172,
  itimLabina152: 148,
  itimLabina225: 41,
  itimLabina223: 41,
  itimLabina226: 41,
  itimLabina224: 41,
  itimLabina53: 41,
  itimLabina125: 41,
  itimLabina158: 167,
  itimLabina149: 145,
  itimLabina85: 81,
  itimLabina112: 107,
  itimLabina72: 67,
  itimLabina106: 101,
  itimLabina169: 40,
  itimLabina27: 20,
  itimLabina133: 126,
  itimLabina80: 75,
  itimLabina155: 86,
  itimLabina124: 119,
  itimLabina129: 122,
  itimLabina159: 152,
  itimLabina105: 100,
  itimLabina184: 166,
  itimLabina104: 99,
  itimLabina202: 16,
  itimLabina102: 97,
  itimLabina37: 26,
  itimLabina228: 26,
  itimLabina227: 26,
  itimLabina114: 109,
  itimLabina103: 98,
  itimLabina77: 72,
  itimLabina232: 70,
  itimLabina173: 169,
  itimLabina162: 155,
  itimLabina207: 70,
  itimLabina110: 105,
  itimLabina222: 110,
  itimLabina219: 190,
  itimLabina176: 44,
  itimLabina217: 16,
  itimLabina208: 70,
  itimLabina73: 68,
  itimLabina119: 115,
  itimLabina148: 70,
  itimLabina160: 151,
  itimLabina47: 36,
  itimLabina74: 69,
  itimLabina88: 83,
  itimLabina193: 173,
  itimLabina34: 25,
  itimLabina66: 51,
  itimLabina95: 90,
  itimLabina117: 113,
  itimLabina83: 78,
  itimLabina231: 188,
  itimLabina147: 70,
  itimLabina131: 125,
  itimLabina32: 23,
  itimLabina101: 96,
  itimLabina211: 189,
  itimLabina170: 113,
  itimLabina122: 105,
  itimLabina82: 77,
  itimLabina171: 171,
  itimLabina113: 108,
  itimLabina179: 181,
  itimLabina127: 120,
  itimLabina65: 50,
  itimLabina42: 30,
}

/** The zman shown for each row in the city's luach. */
export function luachRowsOf(city: CityKey): LuachRows {
  return LUACH_ROWS[CITY_LUACH[city]!]!
}
