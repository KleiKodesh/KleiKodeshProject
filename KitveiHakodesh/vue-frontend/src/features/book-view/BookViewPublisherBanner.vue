<script setup lang="ts">
/**
 * The publisher's credit line above a book's text (Otzaria's `banner` / `book_banner`, read by
 * Core and carried on the catalog row). Not part of the book: it is not selectable, so a copy
 * never takes it. The only markup is `[label](url)`; an address that is not http/https/mailto
 * stays plain text, and each real line break is a line - Otzaria's parseBannerText.
 */
import { computed } from 'vue'
import { openUrlInSystemBrowser } from '@/webview-host/bridge'

const props = defineProps<{ text: string }>()

type BannerSegment = { text: string; url: string | null }

const LINK = /\[([^\]\n]+)\]\(([^)\s]+)\)/g
const ALLOWED_LINK = /^(?:https?:|mailto:)/i

const bannerLines = computed<BannerSegment[][]>(() =>
  props.text.split('\n').map((line) => {
    const segments: BannerSegment[] = []
    let position = 0
    for (const match of line.matchAll(LINK)) {
      if (match.index > position) segments.push({ text: line.slice(position, match.index), url: null })
      const url = ALLOWED_LINK.test(match[2]!) ? match[2]! : null
      segments.push({ text: url ? match[1]! : match[0], url })
      position = match.index + match[0].length
    }
    if (position < line.length || segments.length === 0) segments.push({ text: line.slice(position), url: null })
    return segments
  }),
)
</script>

<template>
  <div class="publisher-banner" dir="rtl">
    <div v-for="(line, lineIndex) in bannerLines" :key="lineIndex">
      <template v-for="(segment, segmentIndex) in line" :key="segmentIndex">
        <a
          v-if="segment.url"
          :href="segment.url"
          @click.prevent="openUrlInSystemBrowser(segment.url)"
        >{{ segment.text }}</a>
        <template v-else>{{ segment.text }}</template>
      </template>
    </div>
  </div>
</template>

<style scoped>
.publisher-banner {
  flex: none;
  margin: 4px 8px 0;
  padding: 4px 16px;
  border-radius: 6px;
  background: var(--bg-secondary);
  color: var(--text-secondary);
  font-size: 0.8rem;
  line-height: 1.3;
  text-align: center;
  user-select: none;
}
.publisher-banner a {
  color: var(--accent-color);
  text-decoration: underline;
}
</style>
