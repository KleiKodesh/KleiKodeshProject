import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'

export interface SectionNavResult { id: number; lineIndex: number }

export async function findNextCommentarySection(
  mainBookId: number,
  commentaryBookId: number,
  currentLineIndex: number,
): Promise<SectionNavResult | null> {
  const rows = await query<SectionNavResult>(
    SQL.GET_NEXT_SECTION_WITH_COMMENTARY,
    [mainBookId, commentaryBookId, currentLineIndex],
  )
  return rows[0] ?? null
}

export async function findPrevCommentarySection(
  mainBookId: number,
  commentaryBookId: number,
  currentLineIndex: number,
): Promise<SectionNavResult | null> {
  const rows = await query<SectionNavResult>(
    SQL.GET_PREV_SECTION_WITH_COMMENTARY,
    [mainBookId, commentaryBookId, currentLineIndex],
  )
  return rows[0] ?? null
}
