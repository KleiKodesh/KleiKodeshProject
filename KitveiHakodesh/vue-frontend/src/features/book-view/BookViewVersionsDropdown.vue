<script setup lang="ts">
/**
 * "נוסחים" dropdown in the BookView toolbar — switches the text between the book's
 * merged text and its alternate versions.
 *
 * A version is an OVERLAY over the same line ids, not a separate book, so switching
 * one in swaps only the text: the TOC, commentary panels, highlights and the scroll
 * position all stay exactly where they were. That is why this replaces the text in
 * place rather than opening a tab, unlike BookViewRelatedBooksDropdown next to it.
 *
 * The button renders only when the book actually has versions (most do not), which
 * the toolbar decides — see `hasVersions` there.
 */
import { ref, computed } from 'vue'
import { storeToRefs } from 'pinia'
import IconBranchingArrows from '@/components/IconBranchingArrows.vue'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { BookVersionRow } from '@/webview-host/queries.types'

const props = defineProps<{
  versions: BookVersionRow[]
  activeVersionId: number | null
}>()

const emit = defineEmits<{ 'open-change': [isOpen: boolean]; select: [versionId: number | null] }>()

const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

// ── Open / close ──────────────────────────────────────────────────────────────

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

function setOpen(value: boolean) {
  isOpen.value = value
  emit('open-change', value)
}

const { justClosed } = useDropdownClose(dropdownRef, () => setOpen(false), {
  toggleButton: toggleButtonRef,
})

function toggleOpen() {
  if (justClosed.value) return
  setOpen(!isOpen.value)
}

const dropdownPositionClass = computed(() => `dropdown-${toolbarPosition.value}`)

// ── Labels ────────────────────────────────────────────────────────────────────

// heVersionTitle is the display name but is frequently absent, in which case the
// upstream key (usually English) is all there is to show.
function labelFor(version: BookVersionRow): string {
  return version.heVersionTitle?.trim() || version.versionTitle
}

const activeVersion = computed(() =>
  props.versions.find((v) => v.id === props.activeVersionId) ?? null,
)

const buttonTitle = computed(() =>
  activeVersion.value ? `נוסח: ${labelFor(activeVersion.value)}` : 'נוסחים',
)

function onSelect(versionId: number | null) {
  setOpen(false)
  if (versionId === props.activeVersionId) return
  emit('select', versionId)
}
</script>

<template>
  <div class="versions-wrapper">
    <button
      ref="toggleButtonRef"
      :class="{ active: isOpen || activeVersionId != null }"
      :title="buttonTitle"
      @click="toggleOpen"
    >
      <IconBranchingArrows />
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="versions-dropdown" :class="dropdownPositionClass">
      <button
        class="version-row"
        :class="{ selected: activeVersionId == null }"
        @click="onSelect(null)"
      >
        נוסח ברירת המחדל
      </button>
      <button
        v-for="version in versions"
        :key="version.id"
        class="version-row"
        :class="{ selected: activeVersionId === version.id }"
        :title="version.heVersionNotes || version.versionNotes || version.versionSource || undefined"
        @click="onSelect(version.id)"
      >
        {{ labelFor(version) }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.versions-wrapper {
  position: relative;
}

/* ── Toggle button — inherits toolbar button styles from main.css ── */
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
button.active {
  color: var(--accent-color);
}

/* ── Dropdown panel ── */
.versions-dropdown {
  position: absolute;
  min-width: 180px;
  max-width: 320px;
  max-height: 320px;
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  z-index: 100;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* ── Positioning based on toolbar position ── */
.dropdown-top {
  top: calc(100% + 4px);
  right: 0;
}

.dropdown-bottom {
  bottom: calc(100% + 4px);
  right: 0;
}

.dropdown-left {
  top: 0;
  left: calc(100% + 4px);
}

.dropdown-right {
  top: 0;
  right: calc(100% + 4px);
}

/* ── Version rows ── */
.version-row {
  display: block;
  width: 100%;
  min-height: 32px;
  padding: 6px 12px;
  text-align: right;
  font-size: 12px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  border-radius: 0;
}
.version-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.version-row:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}
.version-row.selected {
  color: var(--accent-color);
}
</style>
