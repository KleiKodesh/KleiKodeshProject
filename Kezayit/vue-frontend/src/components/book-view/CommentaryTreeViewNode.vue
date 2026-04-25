<script setup lang="ts">
import { ref, computed } from 'vue'
import { IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { CommentaryTreeNode } from './useCommentary'

const props = defineProps<{
  node: CommentaryTreeNode
  hiddenBookIds?: Set<number>
  depth?: number
}>()
const emit = defineEmits<{
  'update:hiddenBookIds': [value: Set<number>]
}>()

const expanded = ref(false)

function collectBookIds(node: CommentaryTreeNode): number[] {
  if (node.type === 'book' && node.bookId != null) return [node.bookId]
  return node.children.flatMap(collectBookIds)
}

const leafIds = computed(() => collectBookIds(props.node))

const isChecked = computed(
  () => props.node.bookId == null || !(props.hiddenBookIds?.has(props.node.bookId) ?? false),
)

const sectionState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  if (props.node.type !== 'section') return 'checked'
  if (!leafIds.value.length) return 'checked'
  const hidden = leafIds.value.filter((id) => props.hiddenBookIds?.has(id)).length
  if (hidden === 0) return 'checked'
  if (hidden === leafIds.value.length) return 'unchecked'
  return 'indeterminate'
})

function toggleCheck(e: MouseEvent) {
  e.stopPropagation()
  const next = new Set(props.hiddenBookIds ?? [])
  if (props.node.type === 'section') {
    if (sectionState.value === 'checked') {
      leafIds.value.forEach((id) => next.add(id))
    } else {
      leafIds.value.forEach((id) => next.delete(id))
    }
  } else if (props.node.bookId != null) {
    if (next.has(props.node.bookId)) next.delete(props.node.bookId)
    else next.add(props.node.bookId)
  }
  emit('update:hiddenBookIds', next)
}
</script>

<template>
  <div style="display: contents">
    <div v-if="node.type === 'section'" class="row section-row" :class="[sectionState, { expanded }]">
      <button class="expander" :class="{ open: expanded }" @click.stop="expanded = !expanded">
        <span class="expander-icon"><IconChevronDown20Regular /></span>
      </button>
      <div class="row-body" @click="toggleCheck($event)">
        <span class="check-col">
          <span class="check-mark">✓</span>
          <span class="dash-mark">–</span>
        </span>
        <span class="row-label">{{ node.label }}</span>
      </div>
    </div>

    <template v-if="node.type === 'section' && expanded">
      <CommentaryTreeViewNode
        v-for="child in node.children"
        :key="child.bookId ?? child.label"
        :node="child"
        :depth="(depth ?? 0) + 1"
        :hidden-book-ids="hiddenBookIds"
        @update:hidden-book-ids="emit('update:hiddenBookIds', $event)"
      />
    </template>

    <div v-if="node.type === 'book'" class="row book-row" :class="{ unchecked: !isChecked }">
      <span class="expander-placeholder" />
      <div class="row-body" @click="toggleCheck($event)">
        <span class="check-col">
          <span class="check-mark">✓</span>
        </span>
        <span class="row-label" :class="{ dimmed: !isChecked }">{{ node.label }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.row {
  display: flex;
  flex-direction: row-reverse;
  align-items: stretch;
  height: 26px;
  flex-shrink: 0;
  white-space: nowrap;
  color: var(--text-primary);
}
.row-body {
  flex: 1;
  display: flex;
  align-items: center;
  cursor: pointer;
  min-width: 0;
}
.row-body:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.section-row {
  font-size: 12px;
  font-weight: 600;
}
.section-row.expanded .row-body,
.section-row.expanded .expander {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.section-row.expanded .row-body:hover,
.section-row.expanded .expander:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.book-row {
  font-size: 11px;
  color: var(--text-secondary);
}

.check-col {
  width: 28px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
}
.check-mark {
  display: none;
}
.dash-mark {
  display: none;
}
.section-row.checked .check-mark {
  display: block;
}
.section-row.indeterminate .dash-mark {
  display: block;
}
.book-row:not(.unchecked) .check-mark {
  display: block;
}

.row-label {
  flex: 1;
  padding-inline-end: 8px;
}
.dimmed {
  opacity: 0.4;
}

.expander {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  flex-shrink: 0;
  align-self: stretch;
  color: var(--text-secondary);
  padding: 0;
  margin: 0;
  border-radius: 0;
}
.expander:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.expander:active {
  transform: none !important;
}
.expander-icon {
  display: flex;
  transition: transform 200ms ease;
}
.expander.open .expander-icon {
  transform: rotate(180deg);
}
.expander :deep(svg) {
  width: 12px;
  height: 12px;
}
.expander-placeholder {
  width: 26px;
  flex-shrink: 0;
}
</style>
