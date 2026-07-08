<script setup lang="ts">
import AppTitleBarBreadcrumbChevronDropdown from './AppTitleBarBreadcrumbChevronDropdown.vue'
import type { BreadcrumbSegment, TocBreadcrumbSegment, PdfBreadcrumbSegment } from './useAppTitleBarTocBreadcrumb'
import { isTocBreadcrumbSegment } from './useAppTitleBarTocBreadcrumb'
import type { TocEntry } from '@/features/book-view/toc/useBookViewToc'
import type { PdfOutlineEntry } from '@/stores/bookViewStore'

const props = defineProps<{
  bookTitle: string
  segments: BreadcrumbSegment[]
  /** First-tier TOC entries for the book-title dropdown (book-view). Empty = no dropdown. */
  rootTocEntries: TocEntry[]
  /** First-tier PDF outline entries for the book-title dropdown (pdf-view). Empty = no dropdown. */
  rootPdfEntries: PdfOutlineEntry[]
}>()

const emit = defineEmits<{
  navigateToTocEntry: [entry: TocEntry]
  navigateToPdfEntry: [entry: PdfOutlineEntry]
}>()

function siblingItems(segment: BreadcrumbSegment) {
  if (isTocBreadcrumbSegment(segment)) return segment.siblings
  return (segment as PdfBreadcrumbSegment).siblings
}

function childItems(segment: BreadcrumbSegment) {
  if (isTocBreadcrumbSegment(segment)) return segment.children
  return (segment as PdfBreadcrumbSegment).children
}

function activeSiblingId(segment: BreadcrumbSegment): number | null {
  if (isTocBreadcrumbSegment(segment)) return segment.tocEntry?.id ?? null
  return (segment as PdfBreadcrumbSegment).outlineEntry?.id ?? null
}

function onSelect(segment: BreadcrumbSegment, item: { id: number; text: string }, fromChildren = false) {
  if (isTocBreadcrumbSegment(segment)) {
    const pool = fromChildren ? segment.children : segment.siblings
    const entry = pool.find((s) => s.id === item.id)
    if (entry) emit('navigateToTocEntry', entry)
  } else {
    const pool = fromChildren
      ? (segment as PdfBreadcrumbSegment).children
      : (segment as PdfBreadcrumbSegment).siblings
    const entry = pool.find((s) => s.id === item.id)
    if (entry) emit('navigateToPdfEntry', entry)
  }
}

/**
 * The active root entry id = the matched entry of the first breadcrumb segment,
 * so the dropdown highlights the currently open top-level section.
 */
function activeRootEntryId(): number | null {
  if (!props.segments.length) return null
  const first = props.segments[0]!
  if (isTocBreadcrumbSegment(first)) return first.tocEntry?.id ?? null
  return (first as PdfBreadcrumbSegment).outlineEntry?.id ?? null
}

function onSelectRootTocEntry(item: { id: number; text: string }) {
  const entry = props.rootTocEntries.find((e) => e.id === item.id)
  if (entry) emit('navigateToTocEntry', entry)
}

function onSelectRootPdfEntry(item: { id: number; text: string }) {
  const entry = props.rootPdfEntries.find((e) => e.id === item.id)
  if (entry) emit('navigateToPdfEntry', entry)
}
</script>

<template>
  <span class="toc-breadcrumb" dir="rtl">
    <span class="breadcrumb-title-name">{{ bookTitle }}</span>

    <!-- Dropdown on the book title showing all first-tier TOC entries.
         Only rendered when the bridge is registered and TOC has entries. -->
    <AppTitleBarBreadcrumbChevronDropdown
      v-if="rootTocEntries.length > 0"
      :siblings="rootTocEntries"
      :active-sibling-id="activeRootEntryId()"
      @select="onSelectRootTocEntry"
    />
    <AppTitleBarBreadcrumbChevronDropdown
      v-else-if="rootPdfEntries.length > 0"
      :siblings="rootPdfEntries"
      :active-sibling-id="activeRootEntryId()"
      @select="onSelectRootPdfEntry"
    />

    <template v-if="segments.length > 0">
      <template v-for="(segment, index) in segments" :key="index">
        <!-- Chevron before every segment listing that segment's siblings.
             Skip index 0 when the title already has a root dropdown — they list
             the same entries and showing both creates a duplicate chevron. -->
        <AppTitleBarBreadcrumbChevronDropdown
          v-if="index > 0 || (rootTocEntries.length === 0 && rootPdfEntries.length === 0)"
          :siblings="siblingItems(segment)"
          :active-sibling-id="activeSiblingId(segment)"
          @select="onSelect(segment, $event)"
        />

        <span class="breadcrumb-segment" :class="{ active: segment.isActive }">
          {{ segment.label }}
        </span>

        <!-- Trailing chevron on the active segment when it has children -->
        <AppTitleBarBreadcrumbChevronDropdown
          v-if="segment.isActive && childItems(segment).length > 0"
          :siblings="childItems(segment)"
          :active-sibling-id="null"
          @select="onSelect(segment, $event, true)"
        />
      </template>
    </template>
  </span>
</template>

<style scoped>
.toc-breadcrumb {
  font-weight: 400;
  font-size: 0.82rem;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: contents;
}

.breadcrumb-title-name {
  unicode-bidi: isolate;
  direction: rtl;
  flex-shrink: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.breadcrumb-segment {
  flex-shrink: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  /* direction: ltr makes ellipsis appear at the start (right side in RTL).
     unicode-bidi: plaintext prevents bidi reordering so Hebrew + punctuation
     renders in natural order rather than being forced LTR. */
  direction: ltr;
  unicode-bidi: plaintext;
  opacity: 0.7;
  white-space: nowrap;
}

.breadcrumb-segment.active {
  opacity: 0.7;
}
</style>
