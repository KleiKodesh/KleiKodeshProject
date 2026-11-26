<script setup lang="ts">
/**
 * "Details" dialog: a real modal listing labelled facts about whatever is open (a book, a
 * Word document, a PDF). It knows nothing about any of them: each viewer hands it the rows,
 * already labelled and formatted. Drawn in the window it was opened from (hostWindow.ts).
 *
 * Close with the X button, Escape, Enter or a click on the backdrop (`close` event).
 */
import { useEventListener } from '@vueuse/core'
import { IconDismiss20Regular, IconFolderOpen20Regular } from '@iconify-prerendered/vue-fluent'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { openUrlInSystemBrowser, revealFileInFolder } from '@/webview-host/bridge'
import { useI18n } from 'vue-i18n'

/** One web address shown as a link: the text to show, and the raw address to open. */
export interface DetailsLink {
  text: string
  href: string
}

export interface DetailsRow {
  label: string
  /** Plain text; may hold several lines. Empty on a row that only has links. */
  value: string
  /** One link per line, opened in the system browser. */
  links?: DetailsLink[]
  /** A file path: shown left to right, with a button that reveals it in the file manager. */
  revealPath?: string
  /** Shown in the subdued "nothing recorded" colour. */
  muted?: boolean
}

export interface DetailsSection {
  /** Heading of the section; leave out when the dialog has just one. */
  title?: string
  rows: DetailsRow[]
}

defineProps<{
  title: string
  sections: DetailsSection[]
  /** Shown instead of the sections while the details are still being read. */
  loading?: boolean
  /** Shown instead of the sections when there is nothing to list or reading failed. */
  message?: string
}>()
const emit = defineEmits<{ close: [] }>()

const { t } = useI18n()
const hostWindow = useHostWindow()

useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
  if (e.code === 'Escape' || e.code === 'Enter') {
    e.preventDefault()
    emit('close')
  }
})
</script>

<template>
  <Teleport :to="hostWindow.document.body">
    <div class="details-backdrop" @click.self="emit('close')">
      <div class="details-dialog" role="dialog" aria-modal="true" :aria-label="title">
        <div class="details-header">
          <h3 class="details-title">{{ title }}</h3>
          <button class="details-close-btn" autofocus :title="t('components.close')" :aria-label="t('components.close')" @click="emit('close')">
            <IconDismiss20Regular />
          </button>
        </div>
        <div v-if="loading" class="details-message">{{ t('components.loading') }}</div>
        <div v-else-if="message" class="details-message">{{ message }}</div>
        <section
          v-for="(section, n) in sections"
          v-else
          :key="section.title ?? n"
          class="details-section"
          :class="{ 'details-section-next': n > 0 }"
        >
          <h4 v-if="section.title" class="details-section-title">{{ section.title }}</h4>
          <dl class="details-list">
            <template v-for="row in section.rows" :key="row.label">
              <dt>{{ row.label }}</dt>
              <dd :class="{ 'details-muted': row.muted }">
                <span v-if="row.value" class="details-value" :class="{ 'details-path': !!row.revealPath }" :title="row.revealPath ? row.value : undefined">{{ row.value }}</span>
                <!-- A real href so it reads as a link; the click goes to the system browser through the host. -->
                <a
                  v-for="link in row.links"
                  :key="link.href"
                  class="details-link"
                  :href="link.href"
                  :title="link.text"
                  @click.prevent="openUrlInSystemBrowser(link.href)"
                >{{ link.text }}</a>
                <button
                  v-if="row.revealPath"
                  class="details-reveal-btn"
                  :title="t('components.openFileLocation')"
                  @click="revealFileInFolder(row.revealPath)"
                >
                  <IconFolderOpen20Regular />
                </button>
              </dd>
            </template>
          </dl>
        </section>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* The app's dialog look (components/AlertDialog.vue). The thin scrollbar comes from main.css ([role='dialog']). */
.details-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10000;
}
.details-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 12px 14px 10px;
  /* Its own width when there is room; on a narrow or short window it shrinks and gives up its
     margins down to the window's edges rather than cutting anything off. */
  width: 380px;
  max-width: 100vw;
  max-height: 100vh;
  box-sizing: border-box;
  min-width: 0;
  overflow: auto;
  display: flex;
  flex-direction: column;
  gap: 8px;
  text-align: start;
}
/* The app sets user-select: none on every element (main.css): everything here can be selected and copied. */
.details-dialog,
.details-dialog :is(h3, h4, section, dl, dt, dd, span, a) {
  user-select: text;
}
.details-title {
  margin: 0;
  font-size: 14px;
  color: var(--text-primary);
}
.details-message {
  padding: 4px 0;
  font-size: 12px;
  color: var(--text-secondary);
}
.details-section-next {
  margin-top: 4px;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}
.details-section-title {
  margin: 0 0 6px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
.details-list {
  margin: 0;
  display: grid;
  /* a fixed label column, so every value starts on one line */
  grid-template-columns: minmax(56px, 104px) minmax(0, 1fr);
  gap: 6px 12px;
  font-size: 12px;
  color: var(--text-primary);
}
.details-list dt {
  opacity: 0.7;
}
.details-list dd {
  margin: 0;
  min-width: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  cursor: text;
}
.details-muted {
  color: var(--text-secondary);
}
.details-path {
  /* inside itself a path reads left to right */
  direction: ltr;
  unicode-bidi: isolate;
}
.details-link {
  display: block;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--accent-color);
  cursor: pointer;
}
.details-link:hover {
  text-decoration: underline;
}
.details-reveal-btn {
  display: inline-flex;
  vertical-align: middle;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  margin-inline-start: 6px;
  padding: 0;
  border-radius: 4px;
  cursor: pointer;
}
.details-reveal-btn svg {
  width: 16px;
  height: 16px;
}
.details-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.details-close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  flex-shrink: 0;
  border-radius: 4px;
  color: var(--text-primary);
}
.details-close-btn:hover {
  background: var(--control-bg-hover);
}
</style>
