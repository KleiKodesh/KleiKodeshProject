/**
 * A line whose text is all bold is a heading in all but tag (measured 2026-11-03: about
 * 8.5k lines), so in flow mode it stands as a block, like a TOC line. A line that only
 * OPENS with bold words — the usual start of a comment — still flows.
 *
 * All bold = at least one <b>/<strong> run and no text outside the bold runs. Wrapper
 * and spacing markup around them doesn't count: Talmud closes each perek with
 * `<br><br><big><strong>הדרן עלך מאימתי</strong></big>`.
 */
export function lineIsWhollyBold(content: string): boolean {
  if (!BOLD_OPEN.test(content)) return false
  const outside = content.replace(BOLD_RUN, '').replace(TAG, '').replace(NBSP, ' ')
  return outside.trim() === ''
}

const BOLD_OPEN = /<(?:b|strong)\b/i
const BOLD_RUN = /<(b|strong)\b[^>]*>[\s\S]*?<\/\1\s*>/gi
const TAG = /<[^>]+>/g
const NBSP = /&nbsp;/gi
