import { ref, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/data/stores/settingsStore'
import type FontSelector from '@/components/settings/FontSelector.vue'

export function useReadingSettingsTab() {
    const settingsStore = useSettingsStore()
    const {
        headerFont,
        textFont,
        fontSize,
        linePadding,
        commentaryHeaderFont,
        commentaryTextFont,
        commentaryFontSize,
        commentaryLinePadding
    } = storeToRefs(settingsStore)

    const availableFonts = ref<string[]>([])

    const headerFontRef = ref<InstanceType<typeof FontSelector> | null>(null)
    const textFontRef = ref<InstanceType<typeof FontSelector> | null>(null)
    const commentaryHeaderFontRef = ref<InstanceType<typeof FontSelector> | null>(null)
    const commentaryTextFontRef = ref<InstanceType<typeof FontSelector> | null>(null)

    const closeOtherDropdowns = (except: string) => {
        if (except !== 'header' && headerFontRef.value) {
            headerFontRef.value.isOpen = false
        }
        if (except !== 'text' && textFontRef.value) {
            textFontRef.value.isOpen = false
        }
        if (except !== 'commentaryHeader' && commentaryHeaderFontRef.value) {
            commentaryHeaderFontRef.value.isOpen = false
        }
        if (except !== 'commentaryText' && commentaryTextFontRef.value) {
            commentaryTextFontRef.value.isOpen = false
        }
    }

    const detectFonts = async () => {
        const fonts = [
            'Arial',
            'Times New Roman',
            'Courier New',
            'Georgia',
            'Verdana',
            'Tahoma',
            'Trebuchet MS',
            'Comic Sans MS',
            'Impact',
            'Lucida Console',
            'Segoe UI',
            'Calibri',
            'Cambria',
            'Candara',
            'Consolas',
            'Constantia',
            'Corbel',
            'David',
            'Frank Ruehl',
            'Gisha',
            'Leelawadee',
            'Levenim MT',
            'Miriam',
            'Narkisim',
            'Rod',
            'Keter YG',
            'Shofar',
            'Simple CLM',
            'Ezra SIL',
            'SBL Hebrew',
            'Cardo',
            'Taamey David CLM',
            'Taamey Frank CLM',
            'Taamey Ashkenaz',
            'Keter YG',
            'Shofar',
            'Hadasim CLM',
            'Drugulin CLM',
            'Aharoni',
            'Miriam Fixed',
            'Miriam Mono CLM'
        ]

        const canvas = document.createElement('canvas')
        const context = canvas.getContext('2d')
        if (!context) return

        const baseFonts = ['monospace', 'sans-serif', 'serif']
        const testString = 'mmmmmmmmmmlli'
        const testSize = '72px'

        const baseWidths: Record<string, number> = {}
        for (const baseFont of baseFonts) {
            context.font = `${testSize} ${baseFont}`
            baseWidths[baseFont] = context.measureText(testString).width
        }

        const detected: string[] = []
        for (const font of fonts) {
            let isDetected = false
            for (const baseFont of baseFonts) {
                context.font = `${testSize} '${font}', ${baseFont}`
                const width = context.measureText(testString).width
                if (width !== baseWidths[baseFont]) {
                    isDetected = true
                    break
                }
            }
            if (isDetected) {
                detected.push(font)
            }
        }

        availableFonts.value = detected
    }

    onMounted(() => {
        detectFonts()
    })

    return {
        headerFont,
        textFont,
        fontSize,
        linePadding,
        commentaryHeaderFont,
        commentaryTextFont,
        commentaryFontSize,
        commentaryLinePadding,
        availableFonts,
        headerFontRef,
        textFontRef,
        commentaryHeaderFontRef,
        commentaryTextFontRef,
        closeOtherDropdowns
    }
}
