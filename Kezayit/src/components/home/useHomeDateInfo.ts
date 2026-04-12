import { HDate } from '@hebcal/core'
import { getDailyLearning } from '@/utils/hebrewLearning'

const MONTH_NAMES: Record<number, string> = {
  1: 'ניסן',
  2: 'אייר',
  3: 'סיון',
  4: 'תמוז',
  5: 'אב',
  6: 'אלול',
  7: 'תשרי',
  8: 'חשון',
  9: 'כסלו',
  10: 'טבת',
  11: 'שבט',
  12: 'אדר',
  13: 'אדר ב׳',
}

function stripGeresh(s: string): string {
  return s.replace(/[\u05F3\u05F4]/g, '')
}

export interface HomeDateInfo {
  hebrewDate: string
  dafYomi: string | null
  mishnaYomi: string | null
  nachYomi: string | null
}

export function getHomeDateInfo(): HomeDateInfo {
  const today = new Date()
  const hd = new HDate(today)

  const parts = hd.renderGematriya().split(' ')
  const day = stripGeresh(parts[0] ?? '')
  const year = parts[parts.length - 1] ?? ''
  const monthName = MONTH_NAMES[hd.getMonth()] ?? ''
  const hebrewDate = `${day} ${monthName} ${year}`

  const { dafYomi, mishnaYomi, nachYomi } = getDailyLearning(hd)
  return { hebrewDate, dafYomi, mishnaYomi, nachYomi }
}
