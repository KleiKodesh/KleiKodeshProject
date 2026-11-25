<script setup lang="ts">
/** Settings -> About: one button that opens the About page (version, credits and licenses). */
import { usePaneNavigation } from '@/composables/usePaneNavigation'

const pane = usePaneNavigation()
</script>

<template>
  <div data-section="section-about" :data-section-label="$t('settings.about.section')">
    <div id="section-about" class="section-label">{{ $t('settings.about.section') }}</div>
    <div class="about-row">
      <button class="settings-btn" data-testid="open-about" @click="pane.navigateToDestination('/about')">
        {{ $t('settings.about.open') }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.about-row {
  padding: 4px 16px 16px;
}
</style>
