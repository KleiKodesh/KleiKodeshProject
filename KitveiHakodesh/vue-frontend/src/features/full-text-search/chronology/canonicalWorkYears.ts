/**
 * Traditional composition dates for the canonical anonymous works.
 *
 * These are works the chronological sort cannot date from an author, because they have no
 * single author - the Written Torah, Mishnah, Tosefta, both Talmuds, the midrashim, the
 * Targumim, the minor tractates, the Second Temple books and the Geonic responsa. Their ERA,
 * however, is not in doubt, so leaving them undated (they previously all sorted last) threw
 * away the best-known chronology in the entire corpus.
 *
 * Dates follow TRADITIONAL attribution, not critical scholarship - this is a traditional
 * library. So: the Torah is dated to Sinai (2448 AM = -1312), each book of the Prophets and
 * Writings to the era of the author named in Bava Batra 14b-15a, Targum Yonatan on the
 * Prophets to Yonatan ben Uziel the student of Hillel (Megillah 3a, hence ~50 CE rather than
 * the 4th-5th century of critical dating), the Mishnah to Rebbi (189), the Bavli to Rav Ashi
 * and Ravina (500).
 *
 * The table is written out one work at a time on purpose. A pattern-matching approach over
 * category paths was rejected: the corpus stores the Talmud under both a nested and a
 * flattened root (so a naive matcher silently loses half of each), and a substring match on
 * the Talmud's name catches hundreds of later commentary volumes that must NOT inherit an
 * ancient date. Per-work entries also let works that a single group date would misplace be
 * dated correctly - Midrash Rabbah runs from Bereshit (c. 400) to Bemidbar (c. 1100), and
 * Yalkut Shimoni is a 13th-century anthology OF midrash, not midrash.
 *
 * Keys are book titles exactly as stored in the seforim DB.
 *
 * DELIBERATELY ABSENT (these keep sorting last, which is the honest answer):
 *   - Siddurim, machzorim, Haggadot, selichot and other liturgy. A printed prayer book is a
 *     stratified artifact - a Second Temple core, Geonic fixation, medieval piyyutim, and a
 *     19th-century rite edition all in one volume. No single year describes it.
 *   - Batei Midrashot: a modern anthology of dozens of independent short midrashim spanning
 *     roughly 300-1200. The container has no composition date.
 *   - Ein Yaakov: a named author (ibn Habib) in a modern named-editor edition.
 *   - Two entries whose stored titles are garbled beyond confident identification.
 */
export const CANONICAL_WORK_YEARS: Record<string, number> = {
  // ── Torah — given at Sinai (traditional 2448 AM = -1312) ──────
  'דברים': -1312, // Deuteronomy
  'שמות': -1312, // Exodus
  'בראשית': -1312, // Genesis
  'ויקרא': -1312, // Leviticus
  'במדבר': -1312, // Numbers
  // ── Former Prophets — era of the author named in Bava Batra 14b-15a 
  'יהושע': -1245, // Joshua
  'שופטים': -1000, // Judges
  'שמואל א': -931, // Samuel I
  'שמואל ב': -931, // Samuel II
  'מלכים א': -561, // Kings I
  'מלכים ב': -561, // Kings II
  // ── Latter Prophets — the prophet's own era ───────────────────
  'יואל': -800, // Joel
  'יונה': -780, // Jonah
  'עמוס': -765, // Amos
  'הושע': -750, // Hosea
  'מיכה': -737, // Micah
  'ישעיהו': -698, // Isaiah
  'נחום': -680, // Nahum
  'חבקוק': -640, // Habakkuk
  'צפניה': -630, // Zephaniah
  'ירמיהו': -586, // Jeremiah
  'עובדיה': -586, // Obadiah
  'יחזקאל': -571, // Ezekiel
  'חגי': -520, // Haggai
  'זכריה': -520, // Zechariah
  'מלאכי': -518, // Malachi
  // ── Writings — era of the ascribed author ─────────────────────
  'איוב': -1312, // Job
  'תהילים': -931, // Psalms
  'רות': -931, // Ruth
  'קהלת': -928, // Ecclesiastes
  'משלי': -928, // Proverbs
  'שיר השירים': -928, // Song of Songs
  'איכה': -586, // Lamentations
  'דניאל': -536, // Daniel
  'אסתר': -355, // Esther
  'דברי הימים א': -348, // Chronicles I
  'דברי הימים ב': -348, // Chronicles II
  'עזרא': -348, // Ezra
  'נחמיה': -348, // Nehemiah
  // ── Second Temple / external books ────────────────────────────
  'תוספות למגלת אסתר': -150, // Additions to Esther
  'ספר יהודית': -150, // Judith
  'ספר מקבים א': -100, // Maccabees I
  'ספר מקבים א (תרגום כהנא)': -100, // Maccabees I (Kahana translation)
  'ספר שושנה': -100, // Susanna (Sefer Shoshanah)
  'חכמת שלמה': -100, // Wisdom of Solomon
  // ── Chronicles and related ────────────────────────────────────
  'מגילת תענית': -50, // Megillat Taanit
  'סדר עולם רבה': 160, // Seder Olam Rabbah
  'סדר עולם זוטא': 800, // Seder Olam Zuta
  // ── Mishnah — Rebbi's redaction, 189 CE ───────────────────────
  'משנה ערכין': 189, // Mishnah Arakhin
  'משנה עבודה זרה': 189, // Mishnah Avodah Zarah
  'משנה אבות': 189, // Mishnah Avot
  'משנה בבא בתרא': 189, // Mishnah Bava Batra
  'משנה בבא קמא': 189, // Mishnah Bava Kamma
  'משנה בבא מציעא': 189, // Mishnah Bava Metzia
  'משנה ביצה': 189, // Mishnah Beitzah
  'משנה בכורות': 189, // Mishnah Bekhorot
  'משנה ברכות': 189, // Mishnah Berakhot
  'משנה ביכורים': 189, // Mishnah Bikkurim
  'משנה חגיגה': 189, // Mishnah Chagigah
  'משנה חלה': 189, // Mishnah Challah
  'משנה חולין': 189, // Mishnah Chullin
  'משנה דמאי': 189, // Mishnah Demai
  'משנה עדיות': 189, // Mishnah Eduyot
  'משנה עירובין': 189, // Mishnah Eruvin
  'משנה גיטין': 189, // Mishnah Gittin
  'משנה הוריות': 189, // Mishnah Horayot
  'משנה כלים': 189, // Mishnah Kelim
  'משנה כריתות': 189, // Mishnah Keritot
  'משנה כתובות': 189, // Mishnah Ketubot
  'משנה קידושין': 189, // Mishnah Kiddushin
  'משנה כלאים': 189, // Mishnah Kilayim
  'משנה קינים': 189, // Mishnah Kinnim
  'משנה מעשר שני': 189, // Mishnah Maaser Sheni
  'משנה מעשרות': 189, // Mishnah Maasrot
  'משנה מכשירין': 189, // Mishnah Makhshirin
  'משנה מכות': 189, // Mishnah Makkot
  'משנה מגילה': 189, // Mishnah Megillah
  'משנה מעילה': 189, // Mishnah Meilah
  'משנה מנחות': 189, // Mishnah Menachot
  'משנה מדות': 189, // Mishnah Middot
  'משנה מקואות': 189, // Mishnah Mikvaot
  'משנה מועד קטן': 189, // Mishnah Moed Katan
  'משנה נזיר': 189, // Mishnah Nazir
  'משנה נדרים': 189, // Mishnah Nedarim
  'משנה נגעים': 189, // Mishnah Negaim
  'משנה נדה': 189, // Mishnah Niddah
  'משנה אהלות': 189, // Mishnah Oholot
  'משנה ערלה': 189, // Mishnah Orlah
  'משנה פרה': 189, // Mishnah Parah
  'משנה פאה': 189, // Mishnah Peah
  'משנה פסחים': 189, // Mishnah Pesachim
  'משנה ראש השנה': 189, // Mishnah Rosh Hashanah
  'משנה סנהדרין': 189, // Mishnah Sanhedrin
  'משנה שבת': 189, // Mishnah Shabbat
  'משנה שקלים': 189, // Mishnah Shekalim
  'משנה שביעית': 189, // Mishnah Sheviit
  'משנה שבועות': 189, // Mishnah Shevuot
  'משנה סוטה': 189, // Mishnah Sotah
  'משנה סוכה': 189, // Mishnah Sukkah
  'משנה תענית': 189, // Mishnah Taanit
  'משנה תמיד': 189, // Mishnah Tamid
  'משנה תמורה': 189, // Mishnah Temurah
  'משנה תרומות': 189, // Mishnah Terumot
  'משנה טבול יום': 189, // Mishnah Tevul Yom
  'משנה טהרות': 189, // Mishnah Tohorot
  'משנה עוקצים': 189, // Mishnah Uktzin
  'משנה ידים': 189, // Mishnah Yadayim
  'משנה יבמות': 189, // Mishnah Yevamot
  'משנה יומא': 189, // Mishnah Yoma
  'משנה זבים': 189, // Mishnah Zavim
  'משנה זבחים': 189, // Mishnah Zevachim
  // ── Tosefta — immediately post-Mishnaic ───────────────────────
  'תוספתא ערכין': 230, // Tosefta Arakhin
  'תוספתא עבודה זרה': 230, // Tosefta Avodah Zarah
  'תוספתא בבא בתרא': 230, // Tosefta Bava Batra
  'תוספתא בבא בתרא (ליברמן)': 230, // Tosefta Bava Batra (Lieberman)
  'תוספתא בבא קמא': 230, // Tosefta Bava Kamma
  'תוספתא בבא קמא (ליברמן)': 230, // Tosefta Bava Kamma (Lieberman)
  'תוספתא בבא מציעא': 230, // Tosefta Bava Metzia
  'תוספתא בבא מציעא (ליברמן)': 230, // Tosefta Bava Metzia (Lieberman)
  'תוספתא ביצה': 230, // Tosefta Beitzah
  'תוספתא ביצה (ליברמן)': 230, // Tosefta Beitzah (Lieberman)
  'תוספתא בכורות': 230, // Tosefta Bekhorot
  'תוספתא ברכות': 230, // Tosefta Berakhot
  'תוספתא ברכות (ליברמן)': 230, // Tosefta Berakhot (Lieberman)
  'תוספתא ביכורים': 230, // Tosefta Bikkurim
  'תוספתא ביכורים (ליברמן)': 230, // Tosefta Bikkurim (Lieberman)
  'תוספתא חגיגה': 230, // Tosefta Chagigah
  'תוספתא חגיגה (ליברמן)': 230, // Tosefta Chagigah (Lieberman)
  'תוספתא חלה': 230, // Tosefta Challah
  'תוספתא חלה (ליברמן)': 230, // Tosefta Challah (Lieberman)
  'תוספתא חולין': 230, // Tosefta Chullin
  'תוספתא דמאי': 230, // Tosefta Demai
  'תוספתא דמאי (ליברמן)': 230, // Tosefta Demai (Lieberman)
  'תוספתא עדויות': 230, // Tosefta Eduyot
  'תוספתא עירובין': 230, // Tosefta Eruvin
  'תוספתא עירובין (ליברמן)': 230, // Tosefta Eruvin (Lieberman)
  'תוספתא גיטין': 230, // Tosefta Gittin
  'תוספתא גיטין (ליברמן)': 230, // Tosefta Gittin (Lieberman)
  'תוספתא הוריות': 230, // Tosefta Horayot
  'תוספתא כלים בתרא': 230, // Tosefta Kelim Batra
  'תוספתא כלים קמא': 230, // Tosefta Kelim Kamma
  'תוספתא כלים מציעא': 230, // Tosefta Kelim Metzia
  'תוספתא כריתות': 230, // Tosefta Keritot
  'תוספתא כתובות': 230, // Tosefta Ketubot
  'תוספתא כתובות (ליברמן)': 230, // Tosefta Ketubot (Lieberman)
  'תוספתא קידושין': 230, // Tosefta Kiddushin
  'תוספתא קידושין (ליברמן)': 230, // Tosefta Kiddushin (Lieberman)
  'תוספתא כלאים': 230, // Tosefta Kilayim
  'תוספתא כלאים (ליברמן)': 230, // Tosefta Kilayim (Lieberman)
  'תוספתא מעשר שני': 230, // Tosefta Maaser Sheni
  'תוספתא מעשר שני (ליברמן)': 230, // Tosefta Maaser Sheni (Lieberman)
  'תוספתא מעשרות': 230, // Tosefta Maasrot
  'תוספתא מעשרות (ליברמן)': 230, // Tosefta Maasrot (Lieberman)
  'תוספתא מכשירין': 230, // Tosefta Makhshirin
  'תוספתא מכות': 230, // Tosefta Makkot
  'תוספתא מגילה': 230, // Tosefta Megillah
  'תוספתא מגילה (ליברמן)': 230, // Tosefta Megillah (Lieberman)
  'תוספתא מעילה': 230, // Tosefta Meilah
  'תוספתא מנחות': 230, // Tosefta Menachot
  'תוספתא מקוואות': 230, // Tosefta Mikvaot
  'תוספתא מועד קטן': 230, // Tosefta Moed Katan
  'תוספתא מועד קטן (ליברמן)': 230, // Tosefta Moed Katan (Lieberman)
  'תוספתא נזיר': 230, // Tosefta Nazir
  'תוספתא נזיר (ליברמן)': 230, // Tosefta Nazir (Lieberman)
  'תוספתא נדרים': 230, // Tosefta Nedarim
  'תוספתא נדרים (ליברמן)': 230, // Tosefta Nedarim (Lieberman)
  'תוספתא נגעים': 230, // Tosefta Negaim
  'תוספתא נדה': 230, // Tosefta Niddah
  'תוספתא אהלות': 230, // Tosefta Oholot
  'תוספתא ערלה': 230, // Tosefta Orlah
  'תוספתא ערלה (ליברמן)': 230, // Tosefta Orlah (Lieberman)
  'תוספתא פרה': 230, // Tosefta Parah
  'תוספתא פאה': 230, // Tosefta Peah
  'תוספתא פאה (ליברמן)': 230, // Tosefta Peah (Lieberman)
  'תוספתא פסחים': 230, // Tosefta Pesachim
  'תוספתא פסחים (ליברמן)': 230, // Tosefta Pesachim (Lieberman)
  'תוספתא ראש השנה': 230, // Tosefta Rosh Hashanah
  'תוספתא ראש השנה (ליברמן)': 230, // Tosefta Rosh Hashanah (Lieberman)
  'תוספתא סנהדרין': 230, // Tosefta Sanhedrin
  'תוספתא שבת': 230, // Tosefta Shabbat
  'תוספתא שבת (ליברמן)': 230, // Tosefta Shabbat (Lieberman)
  'תוספתא שקלים': 230, // Tosefta Shekalim
  'תוספתא שקלים (ליברמן)': 230, // Tosefta Shekalim (Lieberman)
  'תוספתא שביעית': 230, // Tosefta Sheviit
  'תוספתא שביעית (ליברמן)': 230, // Tosefta Sheviit (Lieberman)
  'תוספתא שבועות': 230, // Tosefta Shevuot
  'תוספתא סוטה': 230, // Tosefta Sotah
  'תוספתא סוטה (ליברמן)': 230, // Tosefta Sotah (Lieberman)
  'תוספתא סוכה': 230, // Tosefta Sukkah
  'תוספתא סוכה (ליברמן)': 230, // Tosefta Sukkah (Lieberman)
  'תוספתא תענית': 230, // Tosefta Taanit
  'תוספתא תענית (ליברמן)': 230, // Tosefta Taanit (Lieberman)
  'תוספתא תמורה': 230, // Tosefta Temurah
  'תוספתא תרומות': 230, // Tosefta Terumot
  'תוספתא תרומות (ליברמן)': 230, // Tosefta Terumot (Lieberman)
  'תוספתא טבול יום': 230, // Tosefta Tevul Yom
  'תוספתא טהרות': 230, // Tosefta Tohorot
  'תוספתא עוקצין': 230, // Tosefta Uktzin
  'תוספתא ידים': 230, // Tosefta Yadayim
  'תוספתא יבמות': 230, // Tosefta Yevamot
  'תוספתא יבמות (ליברמן)': 230, // Tosefta Yevamot (Lieberman)
  'תוספתא יומא': 230, // Tosefta Yoma
  'תוספתא יומא (ליברמן)': 230, // Tosefta Yoma (Lieberman)
  'תוספתא זבים': 230, // Tosefta Zavim
  'תוספתא זבחים': 230, // Tosefta Zevachim
  // ── Targumim — Yonatan ben Uziel (Megillah 3a) for the Prophets; later for the rest 
  'תרגום יונתן על עמוס': 50, // Targum Jonathan on Amos
  'תרגום יונתן על יחזקאל': 50, // Targum Jonathan on Ezekiel
  'תרגום יונתן על חבקוק': 50, // Targum Jonathan on Habakkuk
  'תרגום יונתן על חגי': 50, // Targum Jonathan on Haggai
  'תרגום יונתן על הושע': 50, // Targum Jonathan on Hosea
  'תרגום יונתן על ישעיהו': 50, // Targum Jonathan on Isaiah
  'תרגום יונתן על ירמיהו': 50, // Targum Jonathan on Jeremiah
  'תרגום יונתן על יואל': 50, // Targum Jonathan on Joel
  'תרגום יונתן על יונה': 50, // Targum Jonathan on Jonah
  'תרגום יונתן על יהושע': 50, // Targum Jonathan on Joshua
  'תרגום יונתן על שופטים': 50, // Targum Jonathan on Judges
  'תרגום יונתן על מלכים א': 50, // Targum Jonathan on Kings I
  'תרגום יונתן על מלכים ב': 50, // Targum Jonathan on Kings II
  'תרגום יונתן על מלאכי': 50, // Targum Jonathan on Malachi
  'תרגום יונתן על מיכה': 50, // Targum Jonathan on Micah
  'תרגום יונתן על נחום': 50, // Targum Jonathan on Nahum
  'תרגום יונתן על עובדיה': 50, // Targum Jonathan on Obadiah
  'תרגום יונתן על שמואל א': 50, // Targum Jonathan on Samuel I
  'תרגום יונתן על שמואל ב': 50, // Targum Jonathan on Samuel II
  'תרגום יונתן על זכריה': 50, // Targum Jonathan on Zechariah
  'תרגום יונתן על צפניה': 50, // Targum Jonathan on Zephaniah
  'תרגום דברי הימים א': 400, // Targum on Chronicles I
  'תרגום דברי הימים ב': 400, // Targum on Chronicles II
  'תרגום קהלת': 400, // Targum on Ecclesiastes
  'תרגום אסתר': 400, // Targum on Esther (Targum Rishon)
  'תרגום איוב': 400, // Targum on Job
  'תרגום איכה': 400, // Targum on Lamentations
  'תרגום משלי': 400, // Targum on Proverbs
  'תרגום תהלים': 400, // Targum on Psalms
  'תרגום רות': 400, // Targum on Ruth
  'תרגום על שיר השירים': 400, // Targum on Song of Songs
  'תרגום ניאופיטי': 650, // Targum Neofiti (on the Torah)
  'תרגום יונתן על דברים': 650, // Targum Pseudo-Jonathan on Deuteronomy
  'תרגום יונתן על שמות': 650, // Targum Pseudo-Jonathan on Exodus
  'תרגום יונתן על בראשית': 650, // Targum Pseudo-Jonathan on Genesis
  'תרגום יונתן על ויקרא': 650, // Targum Pseudo-Jonathan on Leviticus
  'תרגום יונתן על במדבר': 650, // Targum Pseudo-Jonathan on Numbers
  'תרגום ירושלמי': 650, // Targum Yerushalmi (Fragmentary Targum on the Torah)
  'תרגום ירושלמי - כתב יד פאריס': 650, // Targum Yerushalmi (Fragmentary Targum), Paris manuscript
  'תרגום שני על אסתר': 700, // Targum Sheni on Esther
  'תרגום שני על אסתר - כתב יד פאריס': 700, // Targum Sheni on Esther, Paris manuscript
  // ── Yerushalmi — sealed in Tiberias, c. 350 ───────────────────
  'תלמוד ירושלמי עבודה זרה': 350, // Yerushalmi Avodah Zarah
  'תלמוד ירושלמי בבא בתרא': 350, // Yerushalmi Bava Batra
  'תלמוד ירושלמי בבא קמא': 350, // Yerushalmi Bava Kamma
  'תלמוד ירושלמי בבא מציעא': 350, // Yerushalmi Bava Metzia
  'תלמוד ירושלמי ביצה': 350, // Yerushalmi Beitzah
  'תלמוד ירושלמי ברכות': 350, // Yerushalmi Berakhot
  'תלמוד ירושלמי בכורים': 350, // Yerushalmi Bikkurim
  'תלמוד ירושלמי חגיגה': 350, // Yerushalmi Chagigah
  'תלמוד ירושלמי חלה': 350, // Yerushalmi Challah
  'תלמוד ירושלמי דמאי': 350, // Yerushalmi Demai
  'תלמוד ירושלמי עירובין': 350, // Yerushalmi Eruvin
  'תלמוד ירושלמי גיטין': 350, // Yerushalmi Gittin
  'תלמוד ירושלמי הוריות': 350, // Yerushalmi Horayot
  'תלמוד ירושלמי כתובות': 350, // Yerushalmi Ketubot
  'תלמוד ירושלמי קידושין': 350, // Yerushalmi Kiddushin
  'תלמוד ירושלמי כלאים': 350, // Yerushalmi Kilayim
  'תלמוד ירושלמי מעשר שני': 350, // Yerushalmi Maaser Sheni
  'תלמוד ירושלמי מעשרות': 350, // Yerushalmi Maasrot
  'תלמוד ירושלמי מכות': 350, // Yerushalmi Makkot
  'תלמוד ירושלמי מגילה': 350, // Yerushalmi Megillah
  'תלמוד ירושלמי מועד קטן': 350, // Yerushalmi Moed Katan
  'תלמוד ירושלמי נזיר': 350, // Yerushalmi Nazir
  'תלמוד ירושלמי נדרים': 350, // Yerushalmi Nedarim
  'תלמוד ירושלמי נדה': 350, // Yerushalmi Niddah
  'תלמוד ירושלמי ערלה': 350, // Yerushalmi Orlah
  'תלמוד ירושלמי פאה': 350, // Yerushalmi Peah
  'תלמוד ירושלמי פסחים': 350, // Yerushalmi Pesachim
  'תלמוד ירושלמי ראש השנה': 350, // Yerushalmi Rosh Hashanah
  'תלמוד ירושלמי סנהדרין': 350, // Yerushalmi Sanhedrin
  'תלמוד ירושלמי שבת': 350, // Yerushalmi Shabbat
  'תלמוד ירושלמי שקלים': 350, // Yerushalmi Shekalim
  'תלמוד ירושלמי שביעית': 350, // Yerushalmi Sheviit
  'תלמוד ירושלמי שבועות': 350, // Yerushalmi Shevuot
  'תלמוד ירושלמי סוטה': 350, // Yerushalmi Sotah
  'תלמוד ירושלמי סוכה': 350, // Yerushalmi Sukkah
  'תלמוד ירושלמי תענית': 350, // Yerushalmi Taanit
  'תלמוד ירושלמי תרומות': 350, // Yerushalmi Terumot
  'תלמוד ירושלמי יבמות': 350, // Yerushalmi Yevamot
  'תלמוד ירושלמי יומא': 350, // Yerushalmi Yoma
  // ── Bavli — Rav Ashi and Ravina, c. 500 ───────────────────────
  'ערכין': 500, // Bavli Arakhin
  'עבודה זרה': 500, // Bavli Avodah Zarah
  'בבא בתרא': 500, // Bavli Bava Batra
  'בבא קמא': 500, // Bavli Bava Kamma
  'בבא מציעא': 500, // Bavli Bava Metzia
  'ביצה': 500, // Bavli Beitzah
  'בכורות': 500, // Bavli Bekhorot
  'ברכות': 500, // Bavli Berakhot
  'חגיגה': 500, // Bavli Chagigah
  'חולין': 500, // Bavli Chullin
  'עירובין': 500, // Bavli Eruvin
  'גיטין': 500, // Bavli Gittin
  'הוריות': 500, // Bavli Horayot
  'כריתות': 500, // Bavli Keritot
  'כתובות': 500, // Bavli Ketubot
  'קידושין': 500, // Bavli Kiddushin
  'מכות': 500, // Bavli Makkot
  'מגילה': 500, // Bavli Megillah
  'מעילה': 500, // Bavli Meilah
  'מנחות': 500, // Bavli Menachot
  'מועד קטן': 500, // Bavli Moed Katan
  'נזיר': 500, // Bavli Nazir
  'נדרים': 500, // Bavli Nedarim
  'נדה': 500, // Bavli Niddah
  'פסחים': 500, // Bavli Pesachim
  'ראש השנה': 500, // Bavli Rosh Hashanah
  'סנהדרין': 500, // Bavli Sanhedrin
  'שבת': 500, // Bavli Shabbat
  'שבועות': 500, // Bavli Shevuot
  'סוטה': 500, // Bavli Sotah
  'סוכה': 500, // Bavli Sukkah
  'תענית': 500, // Bavli Taanit
  'תמיד': 500, // Bavli Tamid
  'תמורה': 500, // Bavli Temurah
  'יבמות': 500, // Bavli Yevamot
  'יומא': 500, // Bavli Yoma
  'זבחים': 500, // Bavli Zevachim
  // ── Minor tractates — tannaitic baraitot and geonic-era tractates 
  'אבות דרבי נתן': 210, // Avot de-Rabbi Natan (Avot de-Rabbi Natan, version A)
  'אבות דרבי נתן נוסח ב': 210, // Avot de-Rabbi Natan, version B
  'מסכת דרך ארץ רבה': 230, // Masechet Derekh Eretz Rabbah
  'מסכת דרך ארץ זוטא': 230, // Masechet Derekh Eretz Zuta
  'מסכת כלה': 230, // Masechet Kallah
  'מסכת שמחות': 230, // Masechet Semachot (Evel Rabbati / Semachot, i.e. Semachot=Semachot)
  'מסכת עריות': 650, // Masechet Arayot
  'מסכת עבדים': 650, // Masechet Avadim
  'מסכת גרים': 650, // Masechet Gerim
  'מסכת כלה רבתי': 650, // Masechet Kallah Rabbati
  'מסכת כותים': 650, // Masechet Kutim
  'מסכת מזוזה': 650, // Masechet Mezuzah
  'מסכת ספר תורה': 650, // Masechet Sefer Torah
  'מסכת סופרים': 650, // Masechet Soferim
  'מסכת תפילין': 650, // Masechet Tefillin
  'מסכת ציצית': 650, // Masechet Tzitzit
  // ── Midrash Rabbah — dated PER BOOK (Bereshit c.400 .. Bemidbar c.1100), never as one series 
  'בראשית רבה': 400, // Genesis Rabbah
  'איכה רבה': 450, // Lamentations Rabbah
  'ויקרא רבה': 450, // Leviticus Rabbah
  'אסתר רבה': 500, // Esther Rabbah
  'רות רבה': 500, // Ruth Rabbah
  'רות רבה (לרנר)': 500, // Ruth Rabbah (Lerner critical edition)
  'שיר השירים רבה': 600, // Song of Songs Rabbah
  'קוהלת רבה': 700, // Ecclesiastes Rabbah (Kohelet Rabbah)
  'דברים רבה': 800, // Deuteronomy Rabbah
  'שמות רבה': 1000, // Exodus Rabbah
  'במדבר רבה': 1100, // Numbers Rabbah
  // ── Aggadic midrashim ─────────────────────────────────────────
  'פסיקתא דרב כהנא': 450, // Pesikta de-Rav Kahana
  'מדרש תנחומא': 500, // Midrash Tanchuma (printed / Yelamdenu recension)
  'תנחומא בובר': 500, // Midrash Tanchuma, Buber recension
  'פסיקתא רבתי': 650, // Pesikta Rabbati
  'מדרש משלי': 700, // Midrash Mishlei (Midrash on Proverbs)
  'משנת רבי אליעזר': 750, // Mishnat Rabbi Eliezer (Midrash Sheloshim u-Shtayim Middot)
  'פרקי דרבי אליעזר': 750, // Pirkei de-Rabbi Eliezer (Pirkei de-Rabbi Eliezer / Pirkei de-Rabbi Eliezer ha-Gadol)
  'תנא דבי אליהו רבה': 750, // Tanna de-Vei Eliyahu Rabbah (Seder Eliyahu Rabbah)
  'תנא דבי אליהו זוטא': 750, // Tanna de-Vei Eliyahu Zuta (Seder Eliyahu Zuta)
  'מדרש שמואל': 800, // Midrash Shmuel (Midrash on Samuel)
  'מדרש תהילים': 800, // Midrash Tehillim (Midrash on Psalms, Shocher Tov)
  'מדרש זוטא': 800, // Midrash Zuta (on Song of Songs, Ruth, Lamentations, Ecclesiastes)
  'אגדת בראשית': 900, // Aggadat Bereshit
  'מסכת גיהנם': 900, // Masechet Gehinnom
  'מדרש אגדה': 1100, // Midrash Aggadah (ed. Buber)
  'ספר הישר (מדרש)': 1100, // Sefer HaYashar (the midrashic Book of Jashar)
  // ── Late anthologies OF midrash — must not sort with the classical strata 
  'ילקוט שמעוני על נ\"ך': 1250, // Yalkut Shimoni on Prophets and Writings
  'ילקוט שמעוני על התורה': 1250, // Yalkut Shimoni on the Torah
  // ── Geonic responsa ───────────────────────────────────────────
  'תשובות הגאונים': 900, // Teshuvot HaGeonim
  'תשובות הגאונים (הרכבי)': 900, // Teshuvot HaGeonim (Harkavy)
  'תשובות הגאונים – מוסאפיה': 900, // Teshuvot HaGeonim (Musafia / Lyck edition)
  'תשובות הגאונים (שערי תשובה)': 900, // Teshuvot HaGeonim (Shaarei Teshuvah)
  'תורתן של ראשונים': 900, // Toratan shel Rishonim
  'אגרת רב שרירא גאון': 987, // Iggeret Rav Sherira Gaon (Epistle of Rav Sherira Gaon)
}
