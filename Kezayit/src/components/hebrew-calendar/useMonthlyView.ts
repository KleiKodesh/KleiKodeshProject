import { ref, computed } from 'vue'
import { HDate } from '@hebcal/core'

export const HEB_MONTHS = [
  { num: 1, name: 'ניסן' },
  { num: 2, name: 'אייר' },
  { num: 3, name: 'סיון' },
  { num: 4, name: 'תמוז' },
  { num: 5, name: 'אב' },
  { num: 6, name: 'אלול' },
  { num: 7, name: 'תשרי' },
  { num: 8, name: 'חשון' },
  { num: 9, name: 'כסלו' },
  { num: 10, name: 'טבת' },
  { num: 11, name: 'שבט' },
  { num: 12, name: 'אדר' },
  { num: 13, name: 'אדר ב׳' },
]

export const GREG_MONTHS = [
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

function hebYearGem(year: number): string {
  try {
    return new HDate(1, 7, year).renderGematriya().split(' ').pop() ?? String(year)
  } catch {
    return String(year)
  }
}

export function useMonthlyView() {
  const today = new Date()
  const todayHd = new HDate(today)

  const gregMonth = ref(today.getMonth()) // 0-based
  const gregYear = ref(today.getFullYear())
  const hebMonth = ref(todayHd.getMonth())
  const hebYear = ref(todayHd.getFullYear())

  const hebrewLabel = computed(() => {
    const m = HEB_MONTHS.find((x) => x.num === hebMonth.value)?.name ?? ''
    return `${m} ${hebYearGem(hebYear.value)}`
  })

  const gregLabel = computed(() => {
    return `${GREG_MONTHS[gregMonth.value] ?? ''} ${gregYear.value}`
  })

  function prevMonth() {
    if (gregMonth.value === 0) {
      gregMonth.value = 11
      gregYear.value--
    } else gregMonth.value--
    const hd = new HDate(new Date(gregYear.value, gregMonth.value, 15))
    hebMonth.value = hd.getMonth()
    hebYear.value = hd.getFullYear()
  }

  function nextMonth() {
    if (gregMonth.value === 11) {
      gregMonth.value = 0
      gregYear.value++
    } else gregMonth.value++
    const hd = new HDate(new Date(gregYear.value, gregMonth.value, 15))
    hebMonth.value = hd.getMonth()
    hebYear.value = hd.getFullYear()
  }

  function goToday() {
    gregMonth.value = today.getMonth()
    gregYear.value = today.getFullYear()
    hebMonth.value = todayHd.getMonth()
    hebYear.value = todayHd.getFullYear()
  }

  function jumpToHebrew(year: number, month: number) {
    hebYear.value = year
    hebMonth.value = month
    const greg = new HDate(1, month, year).greg()
    gregMonth.value = greg.getMonth()
    gregYear.value = greg.getFullYear()
  }

  function reset() {
    goToday()
  }

  return {
    gregMonth,
    gregYear,
    hebMonth,
    hebYear,
    hebrewLabel,
    gregLabel,
    prevMonth,
    nextMonth,
    goToday,
    jumpToHebrew,
    reset,
  }
}
