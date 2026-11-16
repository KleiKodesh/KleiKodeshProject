<template>
  <div data-section="section-shortcuts" :data-section-label="$t('settings.shortcuts.section')">
    <div id="section-shortcuts" class="section-label">{{ $t('settings.shortcuts.section') }}</div>
    <ShortcutsReferenceList editable />
  </div>
</template>

<script setup lang="ts">
import ShortcutsReferenceList from './ShortcutsReferenceList.vue';
</script>
