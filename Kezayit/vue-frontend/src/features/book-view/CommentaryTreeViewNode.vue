<script setup lang="ts">
import { ref, computed } from 'vue'
import { IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import {
  getLegacyCommentaryBookKey,
  isCommentaryGroupHidden,
} from './useCommentary'
import type { CommentaryGroup, CommentaryTreeNode } from './useCommentary'

const props = defineProps<{
  node: CommentaryTreeNode
  hiddenBookIds?: Set<string>
  depth?: number
}>()
const emit = defineEmits<{
  'update:hiddenBookIds': [value: Set<string>]
}>()

const expanded = ref(false)

type LeafNode = Pick<CommentaryGroup, 'filterKey' | 'bookId'>

function collectLeafNodes(node: CommentaryTreeNode): LeafNode[] {
  if (node.type === 'book' && node.bookId != null && node.filterKey)
    return [{ filterKey: node.filterKey, bookId: node.bookId }]
  return node.children.flatMap(collectLeafNodes)
}

const hiddenKeys = computed(() => props.hiddenBookIds ?? new Set<string>())
const leafNodes = computed(() => collectLeafNodes(props.node))

const isChecked = computed(
  () =>
    props.node.type !== 'book' ||
    props.node.bookId == null ||
    !isCommentaryGroupHidden(hiddenKeys.value, {
      filterKey: props.node.filterKey ?? '',
      bookId: props.node.bookId,
    }),
)

const sectionState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  if (props.node.type !== 'section') return 'checked'
  if (!leafNodes.value.length) return 'checked'
  const hidden = leafNodes.value.filter((node) => isCommentaryGroupHidden(hiddenKeys.value, node)).length
  if (hidden === 0) return 'checked'
  if (hidden === leafNodes.value.length) return 'unchecked'
  return 'indeterminate'
})

function toggleCheck(e: MouseEvent) {
  e.stopPropagation()
  const next = new Set(props.hiddenBookIds ?? [])
  if (props.node.type === 'section') {
    if (sectionState.value === 'checked') {
      leafNodes.value.forEach((node) => {
        next.add(node.filterKey)
        next.delete(getLegacyCommentaryBookKey(node.bookId))
      })
    } else {
      leafNodes.value.forEach((node) => {
        next.delete(node.filterKey)
        next.delete(getLegacyCommentaryBookKey(node.bookId))
      })
    }
  } else if (props.node.bookId != null && props.node.filterKey) {
    const legacyKey = getLegacyCommentaryBookKey(props.node.bookId)
    const hidden = isCommentaryGroupHidden(hiddenKeys.value, {
      filterKey: props.node.filterKey,
      bookId: props.node.bookId,
    })
    next.delete(legacyKey)
    if (hidden) next.delete(props.node.filterKey)
    else next.add(props.node.filterKey)
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
        :key="child.filterKey ?? child.bookId ?? child.label"
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
  --expanded-row-bg: color-mix(in srgb, var(--active-bg) 55%, transparent);
  --expanded-row-hover-bg: color-mix(in srgb, var(--active-bg) 65%, var(--hover-bg));
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
  background: var(--expanded-row-bg);
}
.section-row.expanded .row-body:hover,
.section-row.expanded .expander:hover {
  background: var(--expanded-row-hover-bg);
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

:global(:root.dark) .row {
  --expanded-row-bg: var(--active-bg);
  --expanded-row-hover-bg: color-mix(in srgb, var(--active-bg) 70%, var(--hover-bg));
}
</style>
