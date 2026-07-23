import { ref } from 'vue'

export interface HomeDateInfo {
  hebrewDate: string
  dafYomi: string | null
}

const MONTH_NAMES: Record<number, string> = {
  1: 'ניסן', 2: 'אייר', 3: 'סיון', 4: 'תמוז', 5: 'אב', 6: 'אלול',
  7: 'תשרי', 8: 'חשון', 9: 'כסלו', 10: 'טבת', 11: 'שבט', 12: 'אדר', 13: 'אדר ב׳',
}

function stripGeresh(s: string): string {
  return s.replace(/[׳״]/g, '')
}

export const dateInfo = ref<HomeDateInfo>({ hebrewDate: '', dafYomi: null })

/**
 * Recompute the displayed Hebrew date + daf yomi for a given reference date.
 *
 * The halachic day rolls at צאת הכוכבים (nightfall), not civil midnight — so the
 * caller passes a reference already advanced by a day once we're past today's
 * tzeit. Tzeit itself comes from useNextZman (single zmanim source of truth), so
 * this stays a pure calendar render with no zmanim engine of its own.
 */
export async function loadDateInfo(reference: Date = new Date()): Promise<void> {
  const { HDate } = await import('@hebcal/hdate')

  const hd = new HDate(reference)
  const parts = hd.renderGematriya().split(' ')
  const day = stripGeresh(parts[0] ?? '')
  const year = parts[parts.length - 1] ?? ''
  const monthName = MONTH_NAMES[hd.getMonth()] ?? ''

  // DafYomi is the only schedule shown on the home page bar.
  // The full learning suite (mishna, nach, rambam, etc.) loads only when the
  // calendar page is opened, avoiding the @hebcal/learning parse cost at boot.
  const { DafYomi } = await import('@hebcal/learning')
  let dafYomi: string | null = null
  try {
    dafYomi = new DafYomi(hd).render('he').replace(/[׳״]/g, '')
  } catch {}

  dateInfo.value = { hebrewDate: `${day} ${monthName} ${year}`, dafYomi }
}
