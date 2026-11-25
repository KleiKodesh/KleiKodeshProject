// The daily-learning log: "mark as learned" rows in a CSV the user keeps (and can open in Excel).
// The service owns the file (Core DailyLearningLog); the page holds only the shown day's marks.
import { shallowRef, watch, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { serviceCall } from '@/webview-host/serviceClient'
import { pickFolder } from '@/webview-host/bridge'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'

interface LogResult { marks: Array<{ cycle: string; times: number }>; path: string; error?: string | null }

const isoDay = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

export function useDailyLearningLog(day: Ref<Date>) {
  /** Times learned per cycle label, for the shown day only. */
  const counts = shallowRef(new Map<string, number>())
  /** The log file; empty until the user picks a folder. */
  const path = shallowRef('')
  let generation = 0
  /** Marks/unmarks still waiting for their reply: a focus re-read then would race them. */
  let pending = 0

  function apply(r: LogResult, mine: number) {
    if (mine !== generation) return // the day changed meanwhile
    path.value = r.path
    if (r.error) { showToast(t(r.error === 'locked' ? 'home.learningLog.locked' : 'home.learningLog.failed'), { variant: 'error' }); return }
    const map = new Map<string, number>()
    for (const m of r.marks) map.set(m.cycle, m.times)
    counts.value = map
  }

  async function call(op: string, args: object) {
    const mine = ++generation
    if (op !== 'dailyLearningDay') pending++
    try { apply(await serviceCall<LogResult>(op, args), mine) } catch {
      // Reading a day quietly shows nothing; a failed mark/unmark must not look like it worked.
      if (op !== 'dailyLearningDay') showToast(t('home.learningLog.failed'), { variant: 'error' })
    } finally {
      if (op !== 'dailyLearningDay') pending--
    }
  }

  watch(() => isoDay(day.value), (date) => {
    counts.value = new Map()
    void call('dailyLearningDay', { date })
  }, { immediate: true })
  // Back from Excel (or anywhere): read the day again, so edits made in the file show here.
  useEventListener(useHostWindow(), 'focus', () => { if (!pending) void call('dailyLearningDay', { date: isoDay(day.value) }) })

  /** Asks for a folder; an existing log moves into it. False when cancelled or failed. */
  async function chooseFolder(): Promise<boolean> {
    const folder = await pickFolder(t('home.learningLog.pickFolder'))
    if (!folder) return false
    const r = await serviceCall<LogResult>('dailyLearningMoveTo', { value: folder }).catch(() => null)
    if (!r) { showToast(t('home.learningLog.failed'), { variant: 'error' }); return false }
    path.value = r.path
    if (r.error) {
      showToast(t(r.error === 'locked' ? 'home.learningLog.locked' : r.error === 'exists' ? 'home.learningLog.exists' : 'home.learningLog.failed'), { variant: 'error' })
      return false
    }
    return true
  }

  async function mark(cycle: string, place: string) {
    if (!path.value && !(await chooseFolder())) return
    const d = day.value
    await call('dailyLearningMark', { date: isoDay(d), cycle, place })
  }

  async function unmark(cycle: string) {
    await call('dailyLearningUnmark', { date: isoDay(day.value), cycle })
  }

  return { counts, path, mark, unmark, chooseFolder }
}
