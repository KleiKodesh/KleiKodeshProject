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

export const DAY_NAMES = ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת']

export interface CalendarDay {
  date: Date
  hebrewDayStr: string // gematriya of the day number only
  isToday: boolean
  isCurrentMonth: boolean
  isShabbat: boolean
  holidays: string[]
}

export interface CalendarWeek {
  days: CalendarDay[]
}

// renderGematriya() returns "כ״ה נִיסָן תשפ״ו" — first token is the day gematriya
function dayGematriya(hd: HDate): string {
  return hd.renderGematriya().split(' ')[0] ?? String(hd.getDate())
}

// Strip niqqud from Hebrew text for cleaner display
function stripNiqqud(s: string): string {
  // Preserve U+05BE (מקף — Hebrew hyphen used in double-parasha names)
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

export function useHebrewCalendar() {
  const today = new Date()
  today.setHours(0, 0, 0, 0)

  const displayYear = ref(today.getFullYear())
  const displayMonth = ref(today.getMonth()) // 0-indexed Gregorian

  // Today's full Hebrew date label e.g. "כ״ה ניסן תשפ״ו"
  const todayHebrewLabel = computed(() => {
    const hd = new HDate(today)
    const parts = hd.renderGematriya().split(' ')
    const day = parts[0] ?? ''
    const year = parts[parts.length - 1] ?? ''
    const monthName = MONTH_NAMES[hd.getMonth()] ?? ''
    return `${day} ${monthName} ${year}`
  })

  const todayHolidays = computed(() => getHolidayNames(today))

  // Parasha of the current week (shown on Shabbat and the preceding days)
  const parasha = computed(() => {
    try {
      // Look ahead up to 7 days to find the upcoming Shabbat's parasha
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

  // Month header labels
  const hebrewMonthLabel = computed(() => {
    const mid = new Date(displayYear.value, displayMonth.value, 15)
    const hd = new HDate(mid)
    const parts = hd.renderGematriya().split(' ')
    const year = parts[parts.length - 1] ?? ''
    const monthName = MONTH_NAMES[hd.getMonth()] ?? ''
    return `${monthName} ${year}`
  })

  const gregorianMonthLabel = computed(() =>
    new Date(displayYear.value, displayMonth.value, 1).toLocaleDateString('he-IL', {
      month: 'long',
      year: 'numeric',
    }),
  )

  const weeks = computed((): CalendarWeek[] => {
    const year = displayYear.value
    const month = displayMonth.value
    const firstDay = new Date(year, month, 1)
    const startDow = firstDay.getDay() // 0=Sun

    const startDate = new Date(firstDay)
    startDate.setDate(startDate.getDate() - startDow)

    const days: CalendarDay[] = []
    for (let i = 0; i < 42; i++) {
      const d = new Date(startDate)
      d.setDate(startDate.getDate() + i)
      d.setHours(0, 0, 0, 0)

      const hd = new HDate(d)
      days.push({
        date: d,
        hebrewDayStr: dayGematriya(hd),
        isToday: d.getTime() === today.getTime(),
        isCurrentMonth: d.getMonth() === month,
        isShabbat: d.getDay() === 6,
        holidays: getHolidayNames(d),
      })
    }

    const result: CalendarWeek[] = []
    for (let w = 0; w < 6; w++) {
      result.push({ days: days.slice(w * 7, w * 7 + 7) })
    }
    return result
  })

  function prevMonth() {
    if (displayMonth.value === 0) {
      displayMonth.value = 11
      displayYear.value--
    } else {
      displayMonth.value--
    }
  }

  function nextMonth() {
    if (displayMonth.value === 11) {
      displayMonth.value = 0
      displayYear.value++
    } else {
      displayMonth.value++
    }
  }

  function goToToday() {
    displayYear.value = today.getFullYear()
    displayMonth.value = today.getMonth()
  }

  return {
    weeks,
    dayNames: DAY_NAMES,
    hebrewMonthLabel,
    gregorianMonthLabel,
    todayHebrewLabel,
    todayHolidays,
    parasha,
    prevMonth,
    nextMonth,
    goToToday,
  }
}
