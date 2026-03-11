<template>
  <div class="tab-pane">
    <!-- Section: BookLineView Settings -->
    <div class="section-header">הגדרות תצוגת ספר</div>

    <FontSelector ref="headerFontRef"
                  label="גופן כותרות"
                  v-model="headerFont"
                  :available-fonts="availableFonts"
                  font-type="sans-serif"
                  @toggle="closeOtherDropdowns('header')" />

    <FontSelector ref="textFontRef"
                  label="גופן טקסט"
                  v-model="textFont"
                  :available-fonts="availableFonts"
                  font-type="serif"
                  @toggle="closeOtherDropdowns('text')" />

    <SliderSetting label="גודל גופן"
                   v-model="fontSize"
                   :min="50"
                   :max="200"
                   :step="5"
                   suffix="%" />

    <SliderSetting label="ריווח שורות"
                   v-model="linePadding"
                   :min="1.2"
                   :max="3.0"
                   :step="0.1" />

    <!-- Section: Commentary Settings -->
    <div class="section-header">הגדרות תצוגת פירושים</div>

    <div class="setting-group">
      <div class="button-group flex-row">
        <button :class="['toggle-btn c-pointer', { active: !useSeparateCommentarySettings }]"
                @click="useSeparateCommentarySettings = false">
          זהה לתצוגת ספר
        </button>
        <button :class="['toggle-btn c-pointer', { active: useSeparateCommentarySettings }]"
                @click="useSeparateCommentarySettings = true">
          הגדרות נפרדות
        </button>
      </div>
    </div>

    <template v-if="useSeparateCommentarySettings">
      <FontSelector ref="commentaryHeaderFontRef"
                    label="גופן כותרות"
                    v-model="commentaryHeaderFont"
                    :available-fonts="availableFonts"
                    font-type="sans-serif"
                    @toggle="closeOtherDropdowns('commentaryHeader')" />

      <FontSelector ref="commentaryTextFontRef"
                    label="גופן טקסט"
                    v-model="commentaryTextFont"
                    :available-fonts="availableFonts"
                    font-type="serif"
                    @toggle="closeOtherDropdowns('commentaryText')" />

      <SliderSetting label="גודל גופן"
                     v-model="commentaryFontSize"
                     :min="50"
                     :max="200"
                     :step="5"
                     suffix="%" />

      <SliderSetting label="ריווח שורות"
                     v-model="commentaryLinePadding"
                     :min="1.2"
                     :max="3.0"
                     :step="0.1" />
    </template>
  </div>
</template>

<script setup lang="ts">
import FontSelector from '@/components/settings/FontSelector.vue'
import SliderSetting from '@/components/settings/SliderSetting.vue'
import { useReadingSettingsTab } from '@/components/settings/useReadingSettingsTab'

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
  availableFonts,
  headerFontRef,
  textFontRef,
  commentaryHeaderFontRef,
  commentaryTextFontRef,
  closeOtherDropdowns
} = useReadingSettingsTab()
</script>

<style scoped>
.tab-pane {
  direction: rtl;
  padding-bottom: 2rem;
}

.toggle-btn {
  flex: 1;
  padding: 8px 12px;
  border: 1px solid var(--border-color);
  background: var(--bg-primary);
  color: var(--text-primary);
  cursor: pointer;
  transition: all 0.2s;
}

.toggle-btn:hover {
  background: var(--hover-bg);
  border-color: var(--accent-color);
}

.toggle-btn.active {
  background: var(--accent-color);
  color: white;
  border-color: var(--accent-color);
}

.button-group {
  gap: 0;
}

.button-group .toggle-btn:first-child {
  border-radius: 4px 0 0 4px;
}

.button-group .toggle-btn:last-child {
  border-radius: 0 4px 4px 0;
}

.button-group .toggle-btn:not(:last-child) {
  border-left: none;
}
</style>
