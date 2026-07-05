<script setup lang="ts">
import { IconChevronLeft20Regular, IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { SettingsNavEntry } from './useSettingsSearch'

const props = defineProps<{
  tree: SettingsNavEntry[]
  expandedSections: Set<string>
}>()

const emit = defineEmits<{
  (event: 'navigate', sectionId: string): void
  (event: 'toggle-section', sectionId: string): void
}>()
</script>

<template>
  <nav class="settings-side-nav">
    <ul class="side-nav-list">
      <li v-for="entry in props.tree" :key="entry.id" class="side-nav-section">
        <button
          class="side-nav-section-btn"
          @click="entry.children.length > 0 ? emit('toggle-section', entry.id) : emit('navigate', entry.id)"
        >
          <span class="side-nav-section-label" @click.stop="emit('navigate', entry.id)">
            {{ entry.label }}
          </span>
          <component
            v-if="entry.children.length > 0"
            :is="props.expandedSections.has(entry.id) ? IconChevronDown20Regular : IconChevronLeft20Regular"
            class="side-nav-chevron"
          />
        </button>
        <ul
          v-if="entry.children.length > 0 && props.expandedSections.has(entry.id)"
          class="side-nav-children"
        >
          <li v-for="child in entry.children" :key="child.id">
            <button class="side-nav-child-btn" @click="emit('navigate', child.id)">
              {{ child.label }}
            </button>
          </li>
        </ul>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.settings-side-nav {
  display: none;
  flex-shrink: 0;
  width: 200px;
  height: 100%;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 12px 0 40px;
  background: var(--bg-primary);
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.side-nav-list {
  list-style: none;
  margin: 0;
  padding: 0 8px;
}

.side-nav-section {
  margin-bottom: 2px;
}

.side-nav-section-btn {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  height: 36px;
  padding: 0 12px;
  background: transparent;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  color: var(--text-primary);
  font-size: 13px;
  font-weight: 500;
  text-align: right;
  gap: 4px;
  transition: background 100ms, transform 80ms;
}

.side-nav-section-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.side-nav-section-label {
  flex: 1;
  text-align: right;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.side-nav-chevron {
  flex-shrink: 0;
  color: var(--text-secondary);
}

.side-nav-children {
  list-style: none;
  margin: 2px 0 4px 0;
  padding: 0 0 0 12px;
}

.side-nav-child-btn {
  display: flex;
  align-items: center;
  width: 100%;
  height: 32px;
  padding: 0 12px;
  background: transparent;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  color: var(--text-secondary);
  font-size: 12px;
  text-align: right;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  transition: background 100ms, color 100ms, transform 80ms;
}

.side-nav-child-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
  color: var(--text-primary);
}

@media (min-width: 900px) {
  .settings-side-nav {
    display: block;
  }
}
</style>
