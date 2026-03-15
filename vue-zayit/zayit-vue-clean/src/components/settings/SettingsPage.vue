<script setup lang="ts">
import { IconSettings24Regular } from '@iconify-prerendered/vue-fluent'
</script>

<template>
  <div class="settings-page">
    <div class="settings-header">
      <div class="settings-icon"><IconSettings24Regular /></div>
      <h1>הגדרות</h1>
    </div>
    <section class="settings-section">
      <h2>כללי</h2>
      <div class="settings-item">
        <span>גרסה</span>
        <span class="value">1.0.0</span>
      </div>
    </section>
  </div>
</template>

<style scoped>
.settings-page { padding: 24px 16px; max-width: 600px; margin: 0 auto; }

.settings-header { display: flex; align-items: center; gap: 12px; margin-bottom: 28px; }
.settings-icon {
  display: flex; align-items: center; justify-content: center;
  width: 48px; height: 48px; border-radius: 12px;
  background: color-mix(in srgb, #6c757d 15%, transparent);
  color: #6c757d; font-size: 24px;
}
h1 { font-size: 1.4rem; font-weight: 600; color: var(--text-primary); margin: 0; }

.settings-section { display: flex; flex-direction: column; gap: 4px; }
h2 {
  font-size: 0.75rem; font-weight: 600; color: var(--text-secondary);
  text-transform: uppercase; letter-spacing: 0.05em;
  margin: 0 0 8px; padding-inline-start: 4px;
}

.settings-item {
  display: flex; align-items: center; justify-content: space-between;
  padding: 12px; border-radius: 8px; background: var(--bg-secondary);
  font-size: 0.95rem; color: var(--text-primary);
}
.value { font-size: 0.9rem; color: var(--text-secondary); }
</style>
