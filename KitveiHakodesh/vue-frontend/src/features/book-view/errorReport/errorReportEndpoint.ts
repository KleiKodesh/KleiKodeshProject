/**
 * Where a "דיווח על טעות" report is POSTed.
 *
 * זהו השרת של אוצריא, לא שלנו — הדיווחים נוחתים אצל המתחזקים שלהם.
 *
 * הספריות משותפות: אנחנו קוראים את אותם מסדי seforim.db, ולכן source_folder,
 * book_id, he_ref ו-library_version מזהים את אותה שורה שהם עצמם היו מזהים.
 * מי שמפנה את הקבוע הזה למקום אחר צריך שרת שמבין את אותה סכימה.
 *
 * One constant, one place: everything else imports from here, so pointing this
 * elsewhere is a single edit and `grep ERROR_REPORT_ENDPOINT` finds every consumer.
 */
export const ERROR_REPORT_ENDPOINT = 'https://otzaria.org/api/reportingerrors'

/** Milliseconds before a send is abandoned and the report re-queued as pending. */
export const ERROR_REPORT_TIMEOUT_MS = 10_000

/** Is there an endpoint to send to at all? Guards every send path. */
export function hasErrorReportEndpoint(): boolean {
  return ERROR_REPORT_ENDPOINT.trim() !== ''
}
