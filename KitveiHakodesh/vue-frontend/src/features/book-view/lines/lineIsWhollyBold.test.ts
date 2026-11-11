import { describe, expect, it } from 'vitest'
import { lineIsWhollyBold } from './lineIsWhollyBold'

describe('lineIsWhollyBold', () => {
  it('takes a bold-only line, wrappers and line breaks aside', () => {
    expect(lineIsWhollyBold('<b>הלכות ציצית</b>')).toBe(true)
    expect(lineIsWhollyBold('<br><br><big><strong>הדרן עלך מאימתי</strong></big>')).toBe(true)
    expect(lineIsWhollyBold('<strong>סימן א</strong>&nbsp;<br>')).toBe(true)
  })

  it('lets a line that only opens with bold words flow', () => {
    expect(lineIsWhollyBold('<big><strong>מֵאֵימָתַי</strong></big> קוֹרִין אֶת שְׁמַע')).toBe(false)
    expect(lineIsWhollyBold('מַתְנִי׳ <big><strong>הָיָה קוֹרֵא</strong></big> בַּתּוֹרָה')).toBe(false)
    expect(lineIsWhollyBold('טקסט רגיל')).toBe(false)
  })
})
