import { ref, watch, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'

export function useSettingsPage() {
  const settings = useSettingsStore()

  const {
    censorDivineNames, headerFont, textFont, fontSize, linePadding,
    commentaryHeaderFont, commentaryTextFont, commentaryFontSize, commentaryLinePadding,
    useSeparateCommentarySettings, appZoom,
    newTabPage,
  } = storeToRefs(settings)

  const availableFonts = ref<string[]>([])

  watch([useSeparateCommentarySettings, headerFont, textFont, fontSize, linePadding], () => {
    if (!useSeparateCommentarySettings.value) {
      commentaryHeaderFont.value = headerFont.value
      commentaryTextFont.value = textFont.value
      commentaryFontSize.value = fontSize.value
      commentaryLinePadding.value = linePadding.value
    }
  })

  async function detectFonts() {
    const candidates = [
      'Arial', 'Times New Roman', 'Courier New', 'Georgia', 'Verdana', 'Tahoma',
      'Segoe UI', 'Calibri', 'Cambria', 'Consolas', 'David', 'Frank Ruehl', 'Gisha',
      'Levenim MT', 'Miriam', 'Narkisim', 'Rod', 'Keter YG', 'Shofar', 'Simple CLM',
      'Ezra SIL', 'SBL Hebrew', 'Cardo', 'Taamey David CLM', 'Taamey Frank CLM',
      'Taamey Ashkenaz', 'Hadasim CLM', 'Aharoni',
    ]
    const canvas = document.createElement('canvas')
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    const baseFonts = ['monospace', 'sans-serif', 'serif']
    const test = 'mmmmmmmmmmlli'
    const baseWidths: Record<string, number> = {}
    for (const b of baseFonts) { ctx.font = `72px ${b}`; baseWidths[b] = ctx.measureText(test).width }
    availableFonts.value = candidates.filter(font =>
      baseFonts.some(b => { ctx.font = `72px '${font}', ${b}`; return ctx.measureText(test).width !== baseWidths[b] })
    )
  }

  onMounted(detectFonts)

  return {
    availableFonts,
    censorDivineNames, headerFont, textFont, fontSize, linePadding,
    commentaryHeaderFont, commentaryTextFont, commentaryFontSize, commentaryLinePadding,
    useSeparateCommentarySettings, appZoom,
    newTabPage,
    reset: settings.reset,
  }
}
