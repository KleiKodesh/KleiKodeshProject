<script setup lang="ts">
import { ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import FontDisplaySettings from './FontDisplaySettings.vue'

const settings = useSettingsStore()
const {
  headerFont,
  textFont,
  fontSize,
  linePadding,
  commentaryHeaderFont,
  commentaryTextFont,
  commentaryFontSize,
  commentaryLinePadding,
  useSeparateCommentarySettings,
} = storeToRefs(settings)

const bookDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
const commentaryDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
</script>

<template>
  <div class="step-content">
    <div class="step-header">
      <h2 class="step-title">תצוגת הספרים</h2>
      <p class="step-desc">בחר גופנים ומרווחים לתצוגת הספרים והפירושים.</p>
    </div>
    <div class="step-scroll">
      <div class="step-card">
        <!-- Reading preview — inside the card so it matches the card width -->
        <div
          class="reading-preview"
          :style="{
            fontFamily: textFont,
            fontSize: fontSize * 0.14 + 'px',
            lineHeight: linePadding,
          }"
        >
          <div class="reading-preview-header" :style="{ fontFamily: headerFont }">אבות פרק א</div>
          <div class="reading-preview-body">
            משה קיבל תורה מסיני ומסרה ליהושע ויהושע לזקנים וזקנים לנביאים ונביאים מסרוה לאנשי
            כנסת הגדולה. הם אמרו שלשה דברים הוו מתונים בדין והעמידו תלמידים הרבה ועשו סייג לתורה.
            שמעון הצדיק היה משירי כנסת הגדולה הוא היה אומר על שלשה דברים העולם עומד על התורה ועל
            העבודה ועל גמילות חסדים.
          </div>
        </div>

        <div class="card-divider" />

        <FontDisplaySettings
          ref="bookDisplayRef"
          v-model:header-font="headerFont"
          v-model:text-font="textFont"
          v-model:font-size="fontSize"
          v-model:line-padding="linePadding"
          @close-other="commentaryDisplayRef?.closeDropdowns()"
        />
        <SettingRow label="תצוגת פירושים">
          <ToggleGroup
            v-model="useSeparateCommentarySettings"
            :options="[
              { label: 'זהה לתצוגת ספר', value: false },
              { label: 'הגדרות נפרדות לפירושים', value: true },
            ]"
          />
        </SettingRow>
        <FontDisplaySettings
          v-if="useSeparateCommentarySettings"
          ref="commentaryDisplayRef"
          v-model:header-font="commentaryHeaderFont"
          v-model:text-font="commentaryTextFont"
          v-model:font-size="commentaryFontSize"
          v-model:line-padding="commentaryLinePadding"
          @close-other="bookDisplayRef?.closeDropdowns()"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.step-content {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.step-header {
  flex-shrink: 0;
  max-width: 560px;
  width: 100%;
  margin: 0 auto;
  padding: 28px 16px 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  box-sizing: border-box;
}

.step-title {
  margin: 0;
  font-size: 20px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
  animation: fade-up 0.25s ease both;
}

.step-desc {
  margin: 0;
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.6;
  animation: fade-up 0.25s 0.05s ease both;
}

.step-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 0 16px 24px;
}

.step-card {
  max-width: 560px;
  margin: 0 auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 16px 20px;
  animation: fade-up 0.25s 0.1s ease both;
}

/* Reading preview — full width of the card */
.reading-preview {
  padding: 10px 14px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-primary);
  color: var(--text-primary);
  direction: rtl;
  text-align: justify;
  overflow: hidden;
  margin-bottom: 4px;
}

.reading-preview-header {
  font-size: 1.15em;
  font-weight: 600;
  margin-bottom: 4px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.reading-preview-body {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.card-divider {
  height: 1px;
  background: var(--border-color);
  margin: 12px 0;
}

@keyframes fade-up {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
