// The daily-learning log: "mark as learned" rows in a CSV the user keeps (and can edit in Excel).
// The service owns the file (Core DailyLearningLog); the page holds only the shown days' marks:
// the shown day, plus its review days while reviews are shown.
import { computed, shallowRef, watch, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { serviceCall } from '@/webview-host/serviceClient'
import { pickFolder } from '@/webview-host/bridge'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'

interface LogResult { marks: Array<{ cycle: string; times: number; reviews: number[] }>; path: string; error?: string | null }

export const isoDay = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

/** `days` = the dates (yyyy-MM-dd) whose marks are needed now. */
export function useDailyLearningLog(days: Ref<string[]>) {
  /** Counts: date -> cycle label -> [times, review 1..4], for the needed dates only. */
  const counts = shallowRef(new Map<string, Map<string, number[]>>())
  /** The log file; empty until the user picks a folder. */
  const path = shallowRef('')
  let generation = 0
  /** Marks/unmarks still waiting for their reply: a focus re-read then would race them. */
  let pending = 0

  function toMap(r: LogResult) {
    const map = new Map<string, number[]>()
    for (const m of r.marks) map.set(m.cycle, [m.times, ...m.reviews])
    return map
  }

  function showError(error: string) {
    showToast(t(error === 'locked' ? 'home.learningLog.locked' : error === 'exists' ? 'home.learningLog.exists' : 'home.learningLog.failed'), { variant: 'error' })
  }

  /** Reads every needed date (one service call each; the file is halved per day, nothing else loaded). */
  async function readAll() {
    const mine = ++generation
    const dates = days.value
    const results = await Promise.all(dates.map((date) =>
      serviceCall<LogResult>('dailyLearningDay', { date }).catch(() => null)))
    if (mine !== generation) return // the days changed meanwhile
    const map = new Map<string, Map<string, number[]>>()
    results.forEach((r, i) => {
      if (!r) return
      path.value = r.path
      if (!r.error) map.set(dates[i]!, toMap(r))
    })
    counts.value = map
  }

  const key = computed(() => days.value.join(','))
  watch(key, () => { counts.value = new Map(); void readAll() }, { immediate: true })
  // Back from Excel (or anywhere): read again, so edits made in the file show here.
  useEventListener(useHostWindow(), 'focus', () => { if (!pending) void readAll() })

  /** A mark/unmark on one date: its reply is that date's marks. */
  async function change(op: 'dailyLearningMark' | 'dailyLearningUnmark', args: { date: string; cycle: string; place?: string; review: number }) {
    pending++
    generation++ // a read still running would bring back the state before this change
    try {
      const r = await serviceCall<LogResult>(op, args)
      path.value = r.path
      if (r.error) { showError(r.error); return }
      const map = new Map(counts.value)
      map.set(args.date, toMap(r))
      counts.value = map
    } catch {
      showToast(t('home.learningLog.failed'), { variant: 'error' })
    } finally {
      pending--
    }
    // A read dropped above (the day had just changed) would leave the other dates empty: read again.
    if (!pending) void readAll()
  }

  /** Asks for a folder; an existing log moves into it. False when cancelled or failed. */
  async function chooseFolder(): Promise<boolean> {
    const folder = await pickFolder(t('home.learningLog.pickFolder'))
    if (!folder) return false
    const r = await serviceCall<LogResult>('dailyLearningMoveTo', { value: folder }).catch(() => null)
    if (!r) { showToast(t('home.learningLog.failed'), { variant: 'error' }); return false }
    path.value = r.path
    if (r.error) { showError(r.error); return false }
    void readAll()
    return true
  }

  /** How many times a cycle's portion of a date was learned (review 0) or reviewed (1..4); 0 when not loaded. */
  const timesOf = (date: string, cycle: string, review = 0) => counts.value.get(date)?.get(cycle)?.[review] ?? 0

  /** One more time: the portion itself (review 0) or one of its reviews (1..4), on the portion's own day. */
  async function mark(date: string, cycle: string, place: string, review = 0) {
    if (!path.value && !(await chooseFolder())) return
    await change('dailyLearningMark', { date, cycle, place, review })
  }

  const unmark = (date: string, cycle: string, review = 0) => change('dailyLearningUnmark', { date, cycle, review })

  return { path, timesOf, mark, unmark, chooseFolder }
}
