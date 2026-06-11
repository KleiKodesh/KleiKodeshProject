/**
 * All SQL strings for the user settings database (user_highlights, user_notes).
 * No inline SQL anywhere else — import from here.
 */

export const USER_SETTINGS_SQL = {
  // ── highlights ──────────────────────────────────────────────────────────

  GET_HIGHLIGHTS_FOR_BOOK: `
    SELECT id, bookId, lineId, startOffset, endOffset, colorArgb, createdAt
    FROM user_highlights
    WHERE bookId = ?
    ORDER BY lineId, startOffset
  `,

  INSERT_HIGHLIGHT: `
    INSERT INTO user_highlights (bookId, lineId, startOffset, endOffset, colorArgb, createdAt)
    VALUES (?, ?, ?, ?, ?, ?)
  `,

  UPDATE_HIGHLIGHT: `
    UPDATE user_highlights
    SET startOffset = ?, endOffset = ?, colorArgb = ?
    WHERE id = ?
  `,

  DELETE_HIGHLIGHT: `
    DELETE FROM user_highlights WHERE id = ?
  `,

  // ── notes ───────────────────────────────────────────────────────────────

  GET_NOTES_FOR_BOOK: `
    SELECT id, bookId, lineId, startOffset, endOffset, note, quote, createdAt, updatedAt
    FROM user_notes
    WHERE bookId = ?
    ORDER BY lineId, startOffset
  `,

  INSERT_NOTE: `
    INSERT INTO user_notes (bookId, lineId, startOffset, endOffset, note, quote, createdAt, updatedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
  `,

  UPDATE_NOTE: `
    UPDATE user_notes
    SET note = ?, updatedAt = ?
    WHERE id = ?
  `,

  DELETE_NOTE: `
    DELETE FROM user_notes WHERE id = ?
  `,
} as const
