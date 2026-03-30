import { ref, watch, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'

export function useSettingsPage() {
  const settings = useSettingsStore()
  const tabStore = useTabStore()

  const {
    censorDivineNames,
    headerFont,
    textFont,
    fontSize,
    linePadding,
    commentaryHeaderFont,
    commentaryTextFont,
    commentaryFontSize,
    commentaryLinePadding,
    useSeparateCommentarySettings,
    appZoom,
    newTabPage,
    resumeLastRead,
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
      // Culmus — first, most suitable for Hebrew reading
      'Frank Ruehl CLM',
      'Taamey Frank CLM',
      'David CLM',
      'Taamey David CLM',
      'Miriam CLM',
      'Miriam Mono CLM',
      'Nachlieli CLM',
      'Hadasim CLM',
      'Keter YG',
      'Taamey Keter YG',
      'Ktav Yad CLM',
      'Shofar',
      'Simple CLM',
      'Aharoni CLM',
      'Aharoni',
      'Drugulin CLM',
      'Ellinia CLM',
      'Rod CLM',
      'Yehuda CLM',
      'Stam Ashkenaz CLM',
      'Stam Sefarad CLM',
      'Taamey Ashkenaz',
      'Caladings CLM',
      // Guttman suite
      'Guttman Vilna',
      'Guttman Vilna Bold',
      'Guttman Frank',
      'Guttman Frank Bold',
      'Guttman Frnew',
      'Guttman Aharoni',
      'Guttman-Aharoni Bold',
      'Guttman-Aram',
      'Guttman Haim',
      'Guttman Haim-Condensed',
      'Guttman Rashi',
      'Guttman Rashi Bold',
      'Guttman Stam',
      'Guttman Stam1',
      'Guttman Yad',
      'Guttman Yad-Brush',
      'Guttman Yad-Light',
      'Guttman Mantova',
      'Guttman Mantova Bold',
      'Guttman Mantova-Decor',
      'Guttman Drogolin',
      'Guttman Hatzvi',
      'Guttman Kav',
      'Guttman Kav-Light',
      'Guttman Miryam Bold',
      'Guttman Miryam Light',
      'Guttman-CourMir',
      'Guttman Myamfix',
      // Windows built-in Hebrew
      'David',
      'FrankRuehl',
      'Miriam',
      'Miriam Fixed',
      'Narkisim',
      'Gisha',
      'Levenim MT',
      'Rod',
      'Hadassah Friedlaender',
      // General / UI
      'Arial',
      'Arial Unicode MS',
      'Times New Roman',
      'Courier New',
      'Tahoma',
      'Verdana',
      'Segoe UI',
      'Calibri',
      'Cambria',
      'Georgia',
      // Google Fonts Hebrew
      'Heebo',
      'Rubik',
      'Assistant',
      'Frank Ruhl Libre',
      'Miriam Libre',
      'David Libre',
      'Alef',
      'Noto Sans Hebrew',
      'Noto Serif Hebrew',
      'Noto Rashi Hebrew',
      'Suez One',
      'Secular One',
      'Varela Round',
      'Bellefair',
      'Amatic SC',
      'Rubik Mono One',
      'Rubik Dirt',
      'Rubik Bubbles',
      'Rubik Glitch',
      'Rubik Iso',
      'Rubik Puddles',
      'Rubik Storm',
      'Rubik Vinyl',
      'Rubik Wet Paint',
      // Scholarly
      'Ezra SIL',
      'SBL Hebrew',
      'SBL BibLit',
      'Cardo',
      'Gentium Plus',
    ]
    const canvas = document.createElement('canvas')
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    const baseFonts = ['monospace', 'sans-serif', 'serif']
    const test = 'אבגדהוזחטיכלמנסעפצקרשת'
    const baseWidths: Record<string, number> = {}
    for (const b of baseFonts) {
      ctx.font = `72px ${b}`
      baseWidths[b] = ctx.measureText(test).width
    }
    availableFonts.value = candidates.filter((font) =>
      baseFonts.some((b) => {
        ctx.font = `72px '${font}', ${b}`
        return ctx.measureText(test).width !== baseWidths[b]
      }),
    )
  }

  async function resetAll() {
    await tabStore.resetAll()
    if (typeof window.__webviewAction === 'function')
      await window.__webviewAction('DeleteBloomIndex', {}).catch(() => {})
    window.location.reload()
  }

  function resetSettings() {
    settings.reset()
  }

  onMounted(detectFonts)

  return {
    availableFonts,
    censorDivineNames,
    headerFont,
    textFont,
    fontSize,
    linePadding,
    commentaryHeaderFont,
    commentaryTextFont,
    commentaryFontSize,
    commentaryLinePadding,
    useSeparateCommentarySettings,
    appZoom,
    newTabPage,
    resumeLastRead,
    resetSettings,
    resetAll,
  }
}
