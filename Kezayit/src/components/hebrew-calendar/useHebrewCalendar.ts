import { ref, computed } from 'vue'
import { HDate, HebrewCalendar, flags } from '@hebcal/core'

// Hebrew month names (niqqud-free for display)
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

// Standard 12-month list (no leap month) for the picker
export const HEB_MONTH_LIST = [
  { num: 7, name: 'תשרי' },
  { num: 8, name: 'חשון' },
  { num: 9, name: 'כסלו' },
  { num: 10, name: 'טבת' },
  { num: 11, name: 'שבט' },
  { num: 12, name: 'אדר' },
  { num: 1, name: 'ניסן' },
  { num: 2, name: 'אייר' },
  { num: 3, name: 'סיון' },
  { num: 4, name: 'תמוז' },
  { num: 5, name: 'אב' },
  { num: 6, name: 'אלול' },
]

// Convert a Hebrew year number to gematriya string (e.g. 5786 → "תשפ״ו")
export function hebYearToGematriya(year: number): string {
  try {
    const hd = new HDate(1, 7, year)
    const parts = hd.renderGematriya().split(' ')
    return parts[parts.length - 1] ?? String(year)
  } catch {
    return String(year)
  }
}

export const GREG_MONTH_LIST = [
  'ינואר',
  'פברואר',
  'מרץ',
  'אפריל',
  'מאי',
  'יוני',
  'יולי',
  'אוגוסט',
  'ספטמבר',
  'אוקטובר',
  'נובמבר',
  'דצמבר',
]

export const DAY_NAMES = ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת']

export interface CalendarDay {
  date: Date
  hebrewDayStr: string
  isToday: boolean
  isCurrentMonth: boolean // true when day's Hebrew month === displayed Hebrew month
  isShabbat: boolean
  holidays: string[]
}

export interface CalendarWeek {
  days: CalendarDay[]
}

function dayGematriya(hd: HDate): string {
  return hd.renderGematriya().split(' ')[0] ?? String(hd.getDate())
}

function stripNiqqud(s: string): string {
  return s.replace(/[\u0591-\u05BD\u05BF-\u05C7]/g, '')
}

function getHolidayNames(date: Date): string[] {
  try {
    const hd = new HDate(date)
    const events = HebrewCalendar.getHolidaysOnDate(hd, true) ?? []
    return events
      .filter((e) => {
        const f = e.getFlags()
        return (
          f & flags.CHAG ||
          f & flags.MINOR_FAST ||
          f & flags.MAJOR_FAST ||
          f & flags.ROSH_CHODESH ||
          f & flags.SPECIAL_SHABBAT ||
          f & flags.MODERN_HOLIDAY ||
          f & flags.CHOL_HAMOED
        )
      })
      .map((e) => stripNiqqud(e.render('he')))
  } catch {
    return []
  }
}

// Number of days in a Hebrew month
function daysInHebMonth(month: number, year: number): number {
  return HDate.daysInMonth(month, year)
}

// Advance Hebrew month by +1 or -1, wrapping year correctly
function advanceHebMonth(
  month: number,
  year: number,
  delta: 1 | -1,
): { month: number; year: number } {
  const months = HDate.monthsInYear(year)
  let m = month + delta
  let y = year
  if (m > months) {
    m = 1
    y++
  }
  if (m < 1) {
    y--
    m = HDate.monthsInYear(y)
  }
  return { month: m, year: y }
}

export function useHebrewCalendar() {
  const today = new Date()
  today.setHours(0, 0, 0, 0)

  const todayHeb = new HDate(today)

  // State: current Hebrew month + year
  const displayHebMonth = ref(todayHeb.getMonth())
  const displayHebYear = ref(todayHeb.getFullYear())

  // Expose as displayMonth/displayYear (Gregorian) for the header Gregorian pickers
  // These are derived from the 1st of the Hebrew month
  const displayMonth = computed({
    get() {
      return new HDate(1, displayHebMonth.value, displayHebYear.value).greg().getMonth()
    },
    set(m: number) {
      // When Gregorian month picker selects, derive Hebrew month from mid of that greg month
      const mid = new Date(displayYear.value, m, 15)
      const hd = new HDate(mid)
      displayHebMonth.value = hd.getMonth()
      displayHebYear.value = hd.getFullYear()
    },
  })

  const displayYear = computed({
    get() {
      return new HDate(1, displayHebMonth.value, displayHebYear.value).greg().getFullYear()
    },
    set(y: number) {
      const mid = new Date(y, displayMonth.value, 15)
      const hd = new HDate(mid)
      displayHebMonth.value = hd.getMonth()
      displayHebYear.value = hd.getFullYear()
    },
  })

  const currentHebMonth = computed(() => displayHebMonth.value)
  const currentHebYear = computed(() => displayHebYear.value)

  const hebrewMonthLabel = computed(() => {
    const monthName = MONTH_NAMES[displayHebMonth.value] ?? ''
    const yearStr = hebYearToGematriya(displayHebYear.value)
    return `${monthName} ${yearStr}`
  })

  const gregorianMonthLabel = computed(() => {
    // Show the Gregorian month(s) that this Hebrew month spans
    const first = new HDate(1, displayHebMonth.value, displayHebYear.value).greg()
    const last = new HDate(
      daysInHebMonth(displayHebMonth.value, displayHebYear.value),
      displayHebMonth.value,
      displayHebYear.value,
    ).greg()
    const firstLabel = first.toLocaleDateString('he-IL', { month: 'long', year: 'numeric' })
    const lastLabel = last.toLocaleDateString('he-IL', { month: 'long', year: 'numeric' })
    return firstLabel === lastLabel ? firstLabel : `${lastLabel} – ${firstLabel}`
  })

  const weeks = computed((): CalendarWeek[] => {
    const hebMonth = displayHebMonth.value
    const hebYear = displayHebYear.value

    // First and last Gregorian dates of this Hebrew month
    const firstGreg = new HDate(1, hebMonth, hebYear).greg()
    const lastDay = daysInHebMonth(hebMonth, hebYear)
    const lastGreg = new HDate(lastDay, hebMonth, hebYear).greg()

    // Pad to Sunday at start, Saturday at end
    const startDate = new Date(firstGreg)
    startDate.setDate(startDate.getDate() - startDate.getDay())
    startDate.setHours(0, 0, 0, 0)

    const endDate = new Date(lastGreg)
    endDate.setDate(endDate.getDate() + (6 - endDate.getDay()))
    endDate.setHours(0, 0, 0, 0)

    const days: CalendarDay[] = []
    const cur = new Date(startDate)
    while (cur <= endDate) {
      const d = new Date(cur)
      const hd = new HDate(d)
      days.push({
        date: d,
        hebrewDayStr: dayGematriya(hd),
        isToday: d.getTime() === today.getTime(),
        isCurrentMonth: hd.getMonth() === hebMonth,
        isShabbat: d.getDay() === 6,
        holidays: getHolidayNames(d),
      })
      cur.setDate(cur.getDate() + 1)
    }

    const result: CalendarWeek[] = []
    for (let i = 0; i < days.length; i += 7) {
      result.push({ days: days.slice(i, i + 7) })
    }
    return result
  })

  function prevMonth() {
    const { month, year } = advanceHebMonth(displayHebMonth.value, displayHebYear.value, -1)
    displayHebMonth.value = month
    displayHebYear.value = year
  }

  function nextMonth() {
    const { month, year } = advanceHebMonth(displayHebMonth.value, displayHebYear.value, 1)
    displayHebMonth.value = month
    displayHebYear.value = year
  }

  function goToToday() {
    displayHebMonth.value = todayHeb.getMonth()
    displayHebYear.value = todayHeb.getFullYear()
  }

  function jumpToHebrew(hebYear: number, hebMonth: number) {
    displayHebMonth.value = hebMonth
    displayHebYear.value = hebYear
  }

  const todayHebrewLabel = computed(() => {
    const hd = new HDate(today)
    const parts = hd.renderGematriya().split(' ')
    const day = parts[0] ?? ''
    const year = parts[parts.length - 1] ?? ''
    const monthName = MONTH_NAMES[hd.getMonth()] ?? ''
    return `${day} ${monthName} ${year}`
  })

  const todayHolidays = computed(() => getHolidayNames(today))

  const parasha = computed(() => {
    try {
      for (let offset = 0; offset <= 6; offset++) {
        const d = new Date(today)
        d.setDate(d.getDate() + offset)
        const hd = new HDate(d)
        const events = HebrewCalendar.calendar({
          start: hd,
          end: hd,
          sedrot: true,
          noHolidays: true,
          il: true,
        })
        const p = events.find((e) => e.getFlags() & flags.PARSHA_HASHAVUA)
        if (p) return stripNiqqud(p.render('he'))
      }
      return null
    } catch {
      return null
    }
  })

  return {
    weeks,
    dayNames: DAY_NAMES,
    displayMonth,
    displayYear,
    currentHebMonth,
    currentHebYear,
    hebrewMonthLabel,
    gregorianMonthLabel,
    jumpToHebrew,
    todayHebrewLabel,
    todayHolidays,
    parasha,
    prevMonth,
    nextMonth,
    goToToday,
  }
}
