<script setup lang="ts">
const props = defineProps<{
  entries: { id: string; label: string }[]
}>()

const emit = defineEmits<{
  (event: 'navigate', sectionId: string): void
}>()
</script>

<template>
  <nav class="settings-side-nav">
    <ul class="side-nav-list">
      <li v-for="entry in props.entries" :key="entry.id" class="side-nav-section">
        <button class="side-nav-section-btn" @click="emit('navigate', entry.id)">
          <span class="side-nav-section-label">
            {{ entry.label }}
          </span>
        </button>
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
  width: 100%;
  min-height: 36px;
  padding: 8px 12px;
  background: transparent;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  color: var(--text-primary);
  font-size: 13px;
  font-weight: 500;
  text-align: right;
  transition: background 100ms, transform 80ms;
}

.side-nav-section-btn:hover {
  background: var(--hover-bg);
}

.side-nav-section-label {
  flex: 1;
  text-align: right;
  white-space: normal;
  overflow-wrap: break-word;
}

@container (min-width: 900px) {
  .settings-side-nav {
    display: block;
  }
}
</style>
