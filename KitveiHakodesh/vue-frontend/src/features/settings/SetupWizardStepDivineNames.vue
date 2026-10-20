<script setup lang="ts">
import SettingsPageCensorDivineNamesSection from './SettingsPageCensorDivineNamesSection.vue'
</script>

<template>
  <!-- The settings-page section, reused as-is. Its own section label is hidden: the
       wizard header already carries the title. -->
  <div class="divine-names-step">
    <SettingsPageCensorDivineNamesSection />
  </div>
</template>

<style scoped>
.divine-names-step :deep(.section-label) {
  display: none;
}
</style>
