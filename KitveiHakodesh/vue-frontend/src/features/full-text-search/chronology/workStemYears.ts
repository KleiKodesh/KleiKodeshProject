/**
 * Multi-volume works, dated by the work rather than the volume.
 *
 * These are single works split one volume per tractate or per section. Each volume carries
 * a DIFFERENT title (the tractate name), so a title lookup can never match them - but the
 * category path names the work itself. Matching on the path stem dates the whole set from
 * one fact.
 *
 * Sourced individually; the year is the author's death year. Two are deliberately absent:
 *
 *   - Yad David (~72 volumes on the Mishneh Torah). Its author could not be identified. The
 *     title collides with at least two unrelated works that are Talmud commentaries by
 *     different authors, and borrowing either one's year would be a fabrication. It stays
 *     unknown until someone identifies the right person.
 *
 * Hagahot YP"M (~22 volumes) is also absent: the attribution rests on expanding an acronym,
 * and no retrieved source ties that author's glosses to the Yerushalmi.
 */
/**
 * Path stems for multi-volume works we deliberately do NOT date, listed so they stop before
 * the canonical-title table rather than falling into it. Their volumes are titled with bare
 * tractate names, so without this a volume titled "Berakhot" would match the Bavli tractate
 * "Berakhot" and be stamped 500 CE — dating a 19th-century commentary to the Amoraim.
 */
export const WORK_STEM_UNDATED: string[] = [
  'יד דוד', // Yad David on the Mishneh Torah — author unidentified; title collides with two unrelated Talmud commentaries
  'הגהות יפ״מ', // Hagahot YP"M — attribution rests on expanding an acronym; unconfirmed against the Yerushalmi
]

export const WORK_STEM_YEARS: [string, number][] = [
  ['נועם ירושלמי', 1873], // Noam Yerushalmi - Yehoshua Yitzchak Shapira of Slonim, d. 1873
  ['חידושי חתם סופר', 1839], // Chidushei Chatam Sofer - Moshe Sofer, d. 1839
  ['הגהות מהר״ם די לונזאנו', 1626], // Hagahot Maharam di Lonzano - Menachem di Lonzano; sources hedge 1623-1626, see note
  ['מרכבת המשנה', 1781], // Merkevet HaMishneh - Shlomo of Chelm, d. 1781
  ['קבלה / רמח״ל', 1746], // Ramchal's kabbalistic writings - Moshe Chaim Luzzatto, d. 1746
  ['קבלה / רמ״ק', 1570], // Ramak's writings - Moshe Cordovero, d. 1570
  ['שדי חמד', 1904], // Sdei Chemed - Chaim Chizkiyahu Medini, d. 1904
  // DELIBERATE EXCEPTION: this year is the PUBLISHER's, not the author's. The Chida
  // (d. 1806) printed these Yerushalmi glosses and attributed them to an unnamed earlier
  // author, so 1806 dates the edition rather than the composition — the glosses themselves
  // are older by an unknown margin. Kept because a publisher's year still places the work
  // far closer than leaving ~40 volumes unsorted at the end of the list.
  ['ככר לאדן', 1806], // Kikar LaAden - published by Chaim Yosef David Azulai (Chida), d. 1806
]
