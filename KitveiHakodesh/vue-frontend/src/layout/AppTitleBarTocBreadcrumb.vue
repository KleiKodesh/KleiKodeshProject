<script setup lang="ts">
import AppTitleBarBreadcrumbChevronDropdown from './AppTitleBarBreadcrumbChevronDropdown.vue'
import type { BreadcrumbSegment, TocBreadcrumbSegment, PdfBreadcrumbSegment } from './useAppTitleBarTocBreadcrumb'
import { isTocBreadcrumbSegment } from './useAppTitleBarTocBreadcrumb'
import type { TocEntry } from '@/features/book-view/toc/useBookViewToc'
import type { PdfOutlineEntry } from '@/stores/bookViewStore'

defineProps<{
  bookTitle: string
  segments: BreadcrumbSegment[]
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
    const pool = fromChildren ? (segment as PdfBreadcrumbSegment).children : (segment as PdfBreadcrumbSegment).siblings
    const entry = pool.find((s) => s.id === item.id)
    if (entry) emit('navigateToPdfEntry', entry)
  }
}
</script>

<template>
  <span class="toc-breadcrumb" dir="rtl">
    <span class="breadcrumb-title-name">{{ bookTitle }}</span>

    <template v-if="segments.length > 0">
      <template v-for="(segment, index) in segments" :key="index">
        <!-- Chevron before every segment listing that segment's siblings -->
        <AppTitleBarBreadcrumbChevronDropdown
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
  opacity: 0.7;
  white-space: nowrap;
}

.breadcrumb-segment.active {
  opacity: 0.7;
}
</style>
