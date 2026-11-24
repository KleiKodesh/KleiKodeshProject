<script setup lang="ts">
import { computed } from 'vue'
import { IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { FilterTreeRow } from './fullTextSearchFilterTree'

/**
 * One row of the FTS filter tree - a category, a book or a computer file - drawn by
 * TreeView (in the list, or as a sticky ancestor copy). Totals and check states are
 * worked out once for the whole tree by the panel and handed in.
 */
const props = defineProps<{
  row: FilterTreeRow
  expanded: boolean
  /** The keyboard's row. */
  focused: boolean
  /** Results: a category's over its subtree, a book's own. */
  count: number
  checked: boolean
  /** Category only: some of its items checked, not all. */
  indet?: boolean
}>()

const emit = defineEmits<{
  toggle: []
  select: [event: MouseEvent]
  check: []
}>()

// Depth is shown by type weight/size/colour rather than indentation, so the
// title keeps the full panel width. The catalogue is ~91% L0-L4 (L5+ is 2.6%),
// so the ladder spends its distinguishable rungs there and clamps the sparse
// deep tail to the last rung — past that, extra rungs would not be legible.
const LADDER_MAX_RUNG = 4
const rung = computed(() => Math.min(props.row.level, LADDER_MAX_RUNG))
</script>

<template>
  <div
    v-if="row.item.kind === 'category'"
    class="row cat-row"
    data-nav-item
    :class="{ checked, indet, expanded, 'is-focused': focused }"
    :data-rung="rung"
  >
    <button
      v-if="row.hasChildren"
      class="expander"
      :class="{ open: expanded }"
      @click.stop="emit('toggle')"
    >
      <span class="expander-icon"><IconChevronDown20Regular /></span>
    </button>
    <span v-else class="expander-placeholder" />
    <button class="row-title" :title="$t('fullTextSearch.filter.scrollToFirst')" @click.stop="emit('select', $event)">
      {{ row.text }}
      <span v-if="count > 0" class="count">({{ count }})</span>
    </button>
    <button class="checkbox-col" @click.stop="emit('check')">
      <span class="check-mark">✓</span>
      <span class="dash-mark">–</span>
    </button>
  </div>
  <div
    v-else-if="row.item.kind === 'book'"
    class="row book-row"
    data-nav-item
    :class="{ checked, 'is-focused': focused }"
  >
    <button
      class="row-title"
      :class="{ dimmed: !checked }"
      :title="$t('fullTextSearch.filter.scrollToFirst')"
      @click.stop="emit('select', $event)"
    >
      {{ row.text }}
      <span v-if="count" class="count">({{ count }})</span>
    </button>
    <button class="checkbox-col" @click.stop="emit('check')">
      <span class="check-mark">✓</span>
    </button>
  </div>
  <div
    v-else
    class="row book-row"
    data-nav-item
    :class="{ checked, 'is-focused': focused }"
  >
    <span class="row-title" :class="{ dimmed: !checked }" :title="row.item.file.path">
      {{ row.text }}
    </span>
    <button class="checkbox-col" @click.stop="emit('check')">
      <span class="check-mark">✓</span>
    </button>
  </div>
</template>

<style scoped>
.row {
  display: flex;
  flex-direction: row-reverse;
  align-items: stretch;
  height: 26px;
  white-space: nowrap;
  user-select: none;
  /* Hierarchy is shown by a TYPE LADDER — weight, size and colour step down
     with depth — plus the tint on expanded categories. Deliberately no
     indentation: the panel is only 180-300px wide with 26px + 28px already
     spent on the expander and checkbox columns, and indenting 8 levels would
     leave ~58px for the title at depth. The ladder keeps the full width.

     Rungs are clamped at 4 (see LADDER_MAX_RUNG): the catalogue is ~91%
     L0-L4, L5+ is only 2.6%, and more than ~5 weight steps stop being
     tellable apart anyway. */
  --expanded-row-bg: color-mix(in srgb, var(--active-bg) 55%, transparent);
  --expanded-row-hover-bg: color-mix(in srgb, var(--active-bg) 65%, var(--hover-bg));
}
.cat-row {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}

/* ── The type ladder ───────────────────────────────────────────────────────
   Roots read as headings, mid levels as subheadings, deep levels recede
   toward the secondary colour. Book rows stay the lightest thing in the tree.

   Only 700 / 600 / 400 are used, and no in-between weights: the UI font is
   Segoe UI Variable, but the renderer snaps intermediate weights to three
   buckets (measured: 500/550/600 render identically, as do 650/700), so a
   ladder built on 650-vs-600 would be invisible. Size and colour do the
   fine-grained stepping instead — both are continuous and always render.

   The three dimensions are deliberately staggered so no two adjacent rungs
   differ on one axis alone: weight breaks between 0 and 1, size carries 1-3,
   and colour carries 2-4. 11px is the floor — it matches the book rows, and
   nothing in the tree renders smaller than that. */
.cat-row[data-rung="0"] .row-title {
  font-size: 12.5px;
  font-weight: 700;
  letter-spacing: 0.01em;
  color: var(--text-primary);
}
.cat-row[data-rung="1"] .row-title {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}
.cat-row[data-rung="2"] .row-title {
  font-size: 11.5px;
  font-weight: 600;
  color: color-mix(in srgb, var(--text-primary) 72%, var(--text-secondary));
}
.cat-row[data-rung="3"] .row-title {
  font-size: 11px;
  font-weight: 600;
  color: color-mix(in srgb, var(--text-primary) 36%, var(--text-secondary));
}
.cat-row[data-rung="4"] .row-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
}
.cat-row.expanded {
  background: var(--expanded-row-bg);
}
.cat-row.expanded:hover {
  background: var(--expanded-row-hover-bg);
}
.book-row {
  font-size: 11px;
  /* Pinned, not inherited: books are the ladder's lightest rung. */
  font-weight: 400;
  color: var(--text-secondary);
}

/* Title button — fills the middle, clicking navigates for books or toggles for categories */
.row-title {
  flex: 1;
  min-width: 0;
  text-align: start;
  padding-inline-end: 8px;
  padding-inline-start: 8px;
  color: inherit;
  background: none;
  border: none;
  cursor: pointer;
  font-size: inherit;
  font-weight: inherit;
  font-family: inherit;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  border-radius: 0;
}
.row-title:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.dimmed { opacity: 0.4; }

/* Checkbox button — right side in RTL (first in DOM, last visually due to row-reverse) */
.checkbox-col {
  width: 28px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
  padding: 0;
  background: none;
  border: none;
  cursor: pointer;
  border-radius: 0;
}
.checkbox-col:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.checkbox-col:active { transform: none !important; }

.check-mark { display: none; }
.dash-mark  { display: none; }
.cat-row.checked  .checkbox-col .check-mark { display: block; }
.cat-row.indet    .checkbox-col .dash-mark  { display: block; }
.book-row.checked .checkbox-col .check-mark { display: block; }

.count {
  font-size: 10px;
  color: var(--text-secondary);
  margin-inline-start: 3px;
}

/* Expander — left side in RTL (last in DOM, first visually due to row-reverse) */
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
.expander:active { transform: none !important; }
.expander-icon {
  display: flex;
  transition: transform 200ms ease;
}
.expander.open .expander-icon { transform: rotate(180deg); }
.expander :deep(svg) { width: 12px; height: 12px; }
.expander-placeholder {
  width: 26px;
  flex-shrink: 0;
}

:global(:root.dark) .row {
  --expanded-row-bg: var(--active-bg);
  --expanded-row-hover-bg: color-mix(in srgb, var(--active-bg) 70%, var(--hover-bg));
}
</style>
