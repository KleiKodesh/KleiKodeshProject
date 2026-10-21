import { t } from '@/i18n'
import { SECTION_LABEL_TO_CONNECTION_TYPE } from './commentaryConnectionTypes'

/**
 * Section and category labels stay Hebrew in the model: they are matched, persisted and
 * reverse-looked-up by their Hebrew text. Only the rendered heading goes through i18n.
 * A DB category with no key (anything outside the fixed list) shows as-is.
 */
const CATEGORY_KEYS: Record<string, string> = {
  'תנ"ך': 'tanakh',
  משנה: 'mishnah',
  תוספתא: 'tosefta',
  תלמוד: 'talmud',
  מדרש: 'midrash',
  גאונים: 'geonim',
  ראשונים: 'rishonim',
  אחרונים: 'acharonim',
  אחר: 'other',
}

export function displayCommentaryLabel(label: string): string {
  const type = SECTION_LABEL_TO_CONNECTION_TYPE[label]
  if (type) return t(`bookView.connectionType.${type}`)
  const category = CATEGORY_KEYS[label]
  return category ? t(`bookView.category.${category}`) : label
}
