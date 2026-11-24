// A link to one daily-learning cycle's reading for a day, in this app's own scheme:
//   kitveihakodeshapp://daily-learning/<cycle>?date=YYYY-MM-DD
// The standalone calendar opens these so a click on a cycle opens its book in the app
// (started with the link, or the running window). The app parses them in
// CSharpBackend/KitveiHakodeshLib/DailyLearningLink.cs - change both together.
import type { HomeDailyLearningItem } from '@/features/home/homeDailyLearningTypes'

export type DailyLearningCycle = HomeDailyLearningItem['key']

/** The day as YYYY-MM-DD in local time (the date the user sees). */
export function localDateKey(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** A YYYY-MM-DD key back to a local date; null when it is not one. */
export function dateFromLocalKey(key: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(key)
  if (!m) return null
  const date = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  return date.getMonth() === Number(m[2]) - 1 ? date : null
}

export function dailyLearningLink(cycle: DailyLearningCycle, date: Date): string {
  return `kitveihakodeshapp://daily-learning/${cycle}?date=${localDateKey(date)}`
}
