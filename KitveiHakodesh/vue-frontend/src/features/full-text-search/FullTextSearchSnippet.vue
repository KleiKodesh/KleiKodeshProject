<script lang="ts">
/** Full snippet HTML -> the trimmed HTML a mounted row shows, so copy takes what is on
 *  screen. Only mounted, trimmed rows are in it; each removes itself on unmount. */
const shownByHtml = new Map<string, { shown: string; rows: number }>()
export function shownSnippetHtml(html: string): string {
  return shownByHtml.get(html)?.shown ?? html
}
</script>

<script setup lang="ts">
/**
 * One result's clamped snippet, with the first match ALWAYS inside the visible lines.
 *
 * The snippet is a fixed window of context words on each side of the match (the search's
 * "context words" setting). In a narrow pane the leading words alone can fill the clamped
 * lines and push the <mark> below them, hidden. So after layout, if the first mark's bottom
 * sits below the box, leading words are dropped (binary search over the text before the
 * mark, so a handful of reflows at most) and a "…" takes their place. Rows whose mark is
 * already visible cost one rect read and nothing else.
 */
import { onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import { useResizeObserver } from '@vueuse/core'

const props = defineProps<{ html: string }>()

const el = ref<HTMLElement | null>(null)
/** The HTML actually shown — the full snippet, or one with leading words trimmed. */
const shown = shallowRef(props.html)
/** The full HTML this row registered a trimmed version for — rows can share one snippet
 *  (the same text in two books), so entries are counted per row. */
let registered: string | null = null

function unregister() {
  if (registered === null) return
  const entry = shownByHtml.get(registered)
  if (entry && --entry.rows <= 0) shownByHtml.delete(registered)
  registered = null
}

function register(html: string, trimmedHtml: string) {
  unregister()
  const entry = shownByHtml.get(html)
  if (entry) { entry.shown = trimmedHtml; entry.rows++ }
  else shownByHtml.set(html, { shown: trimmedHtml, rows: 1 })
  registered = html
}

function markHidden(): boolean {
  const box = el.value
  const mark = box?.querySelector('mark')
  if (!box || !mark) return false
  return mark.getBoundingClientRect().bottom > box.getBoundingClientRect().bottom + 0.5
}

/** `html` with its first `drop` leading words (before the first <mark>) replaced by "…". */
function trimmed(lead: string[], drop: number, rest: string): string {
  return '…' + lead.slice(drop).join(' ') + (drop < lead.length ? ' ' : '') + rest
}

function fit() {
  const box = el.value
  if (!box) return
  shown.value = props.html
  box.innerHTML = props.html
  unregister()
  if (!markHidden()) return
  const markAt = props.html.indexOf('<mark>')
  if (markAt < 0) return
  const lead = props.html.slice(0, markAt).trim().replace(/^…\s*/, '').split(/\s+/).filter(Boolean)
  const rest = props.html.slice(markAt)
  // Smallest number of dropped words that brings the mark into view.
  let lo = 1, hi = lead.length
  while (lo < hi) {
    const mid = (lo + hi) >> 1
    box.innerHTML = trimmed(lead, mid, rest)
    if (markHidden()) lo = mid + 1
    else hi = mid
  }
  shown.value = trimmed(lead, lo, rest)
  box.innerHTML = shown.value
  register(props.html, shown.value)
}

watch(() => props.html, () => fit(), { flush: 'post' })
onBeforeUnmount(unregister)
// Fires once on mount and on every width change of the row.
useResizeObserver(el, () => fit())
</script>

<template>
  <!-- eslint-disable-next-line vue/no-v-html -->
  <div ref="el" class="snippet" v-html="shown" />
</template>

<style scoped>
.snippet {
  font-family: var(--text-font);
  font-size: 1em;
  line-height: var(--line-height, 1.5);
  color: var(--text-secondary);
  direction: rtl;
  text-align: justify;
  text-align-last: right;
  user-select: text;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.snippet :deep(.match),
.snippet :deep(mark) {
  background: transparent;
  color: var(--accent-color);
  font-weight: 600;
  user-select: text;
}
</style>
