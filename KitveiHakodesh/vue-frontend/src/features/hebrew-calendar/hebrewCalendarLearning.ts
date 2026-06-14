/**
 * Shared utilities for formatting Hebrew daily learning (daf yomi, mishna yomi, nach yomi).
 * Used by both the home page bottom bar and the calendar zmanim panel.
 */
import { HDate, gematriya } from '@hebcal/hdate'
import {
  DafYomi,
  MishnaYomiIndex,
  MishnaYomiEvent,
  NachYomiIndex,
  NachYomiEvent,
  YerushalmiYomiEvent,
  yerushalmiYomi,
  vilna,
  DailyRambamEvent,
  dailyRambam1,
  DailyRambam3Event,
  dailyRambam3,
  KitzurShulchanAruchEvent,
  kitzurShulchanAruch,
  ChofetzChaimEvent,
  chofetzChaim,
  PsalmsEvent,
  dailyPsalms,
  PerekYomiEvent,
  perekYomi,
  DirshuAmudYomiEvent,
} from '@hebcal/learning'

// ── Hebrew tractate names ─────────────────────────────────────────────────────

const TRACTATE_HE: Record<string, string> = {
  Berakhot: 'ברכות',
  Peah: 'פאה',
  Demai: 'דמאי',
  Kilayim: 'כלאים',
  Sheviit: 'שביעית',
  Terumot: 'תרומות',
  Maasrot: 'מעשרות',
  'Maaser Sheni': 'מעשר שני',
  Challah: 'חלה',
  Orlah: 'ערלה',
  Bikkurim: 'ביכורים',
  Shabbat: 'שבת',
  Eruvin: 'עירובין',
  Pesachim: 'פסחים',
  Shekalim: 'שקלים',
  Yoma: 'יומא',
  Sukkah: 'סוכה',
  Beitzah: 'ביצה',
  'Rosh Hashanah': 'ראש השנה',
  Taanit: 'תענית',
  Megillah: 'מגילה',
  'Moed Katan': 'מועד קטן',
  Chagigah: 'חגיגה',
  Yevamot: 'יבמות',
  Ketubot: 'כתובות',
  Nedarim: 'נדרים',
  Nazir: 'נזיר',
  Sotah: 'סוטה',
  Gittin: 'גיטין',
  Kiddushin: 'קידושין',
  'Bava Kamma': 'בבא קמא',
  'Bava Metzia': 'בבא מציעא',
  'Bava Batra': 'בבא בתרא',
  Sanhedrin: 'סנהדרין',
  Makkot: 'מכות',
  Shevuot: 'שבועות',
  Eduyot: 'עדויות',
  'Avodah Zarah': 'עבודה זרה',
  Avot: 'אבות',
  Horayot: 'הוריות',
  Zevachim: 'זבחים',
  Menachot: 'מנחות',
  Chullin: 'חולין',
  Bekhorot: 'בכורות',
  Arakhin: 'ערכין',
  Temurah: 'תמורה',
  Keritot: 'כריתות',
  Meilah: 'מעילה',
  Tamid: 'תמיד',
  Middot: 'מידות',
  Kinnim: 'קינים',
  Kelim: 'כלים',
  Oholot: 'אהלות',
  Negaim: 'נגעים',
  Parah: 'פרה',
  Tahorot: 'טהרות',
  Mikvaot: 'מקוואות',
  Niddah: 'נידה',
  Makhshirin: 'מכשירין',
  Zavim: 'זבים',
  'Tevul Yom': 'טבול יום',
  Yadayim: 'ידיים',
  Oktzin: 'עוקצין',
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function stripNiqqud(s: string): string {
  return s.replace(/[\u0591-\u05BD\u05BF-\u05C7]/g, '')
}

function stripGeresh(s: string): string {
  return s.replace(/[\u05F3\u05F4]/g, '')
}

function clean(s: string): string {
  return stripGeresh(stripNiqqud(s))
}

function numToHe(n: number): string {
  try {
    return gematriya(n).replace(/[\u05F3\u05F4]/g, '')
  } catch {
    return String(n)
  }
}

function convertVerseHe(v: string): string {
  return v.replace(/\d+/g, (m) => numToHe(parseInt(m)))
}

function numsToGem(s: string): string {
  return clean(s).replace(/\d+/g, (m) => numToHe(parseInt(m)))
}

function formatMishnaYomi(entry: Array<{ k: string; v: string }>): string {
  if (!entry.length) return ''
  const tractate = TRACTATE_HE[entry[0]!.k] ?? entry[0]!.k
  const verses = entry.map((e) => convertVerseHe(e.v))
  if (verses.length === 2) {
    const [ch1, v1] = verses[0]!.split(':')
    const [ch2, v2] = verses[1]!.split(':')
    if (ch1 === ch2) return `${tractate} ${ch1}:${v1}-${v2}`
    return `${tractate} ${verses.join(', ')}`
  }
  return `${tractate} ${verses[0]}`
}

// ── Public API ────────────────────────────────────────────────────────────────

export interface DailyLearning {
  dafYomi: string | null
  mishnaYomi: string | null
  nachYomi: string | null
  yerushalmiVilna: string | null
  rambam1: string | null
  rambam3: string | null
  kitzurShulchanAruch: string | null
  chofetzChaim: string | null
  psalms: string | null
  perekYomi: string | null
  dirshuAmudYomi: string | null
}

export function getDailyLearning(hd: HDate): DailyLearning {
  let dafYomi: string | null = null
  let mishnaYomi: string | null = null
  let nachYomi: string | null = null
  let yerushalmiVilna: string | null = null
  let rambam1: string | null = null
  let rambam3: string | null = null
  let kitzurShulchanAruchVal: string | null = null
  let chofetzChaimVal: string | null = null
  let psalms: string | null = null
  let perekYomiVal: string | null = null
  let dirshuAmudYomi: string | null = null

  try {
    dafYomi = clean(new DafYomi(hd).render('he'))
  } catch {}
  try {
    const myIdx = new MishnaYomiIndex()
    mishnaYomi = formatMishnaYomi(myIdx.lookup(hd) as Array<{ k: string; v: string }>)
  } catch {}
  try {
    const nyIdx = new NachYomiIndex()
    nachYomi = clean(new NachYomiEvent(hd, nyIdx.lookup(hd)).render('he'))
  } catch {}
  try {
    const reading = yerushalmiYomi(hd, vilna)
    if (reading) yerushalmiVilna = clean(new YerushalmiYomiEvent(hd, reading).renderBrief('he'))
  } catch {}
  try {
    rambam1 = clean(new DailyRambamEvent(hd, dailyRambam1(hd)).render('he'))
  } catch {}
  try {
    rambam3 = numsToGem(new DailyRambam3Event(hd, dailyRambam3(hd)).render('he'))
  } catch {}
  try {
    const r = kitzurShulchanAruch(hd)
    if (r) kitzurShulchanAruchVal = clean(new KitzurShulchanAruchEvent(hd, r).renderBrief('he'))
  } catch {}
  try {
    chofetzChaimVal = numsToGem(new ChofetzChaimEvent(hd, chofetzChaim(hd)).renderBrief('he'))
  } catch {}
  try {
    psalms = numsToGem(new PsalmsEvent(hd, dailyPsalms(hd)).render('he'))
  } catch {}
  try {
    perekYomiVal = clean(new PerekYomiEvent(hd, perekYomi(hd)).render('he'))
  } catch {}
  try {
    dirshuAmudYomi = clean(new DirshuAmudYomiEvent(hd).renderBrief('he'))
  } catch {}

  return {
    dafYomi,
    mishnaYomi,
    nachYomi,
    yerushalmiVilna,
    rambam1,
    rambam3,
    kitzurShulchanAruch: kitzurShulchanAruchVal,
    chofetzChaim: chofetzChaimVal,
    psalms,
    perekYomi: perekYomiVal,
    dirshuAmudYomi,
  }
}
