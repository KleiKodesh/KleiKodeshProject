/**
 * Author death years — the primary key of the chronological sort.
 *
 * Maps a NORMALIZED author name to an approximate Gregorian death year. Keys must already
 * be normalized (nikud/te'amim, quote glyphs and directional marks stripped, whitespace
 * collapsed, lowercased) because the runtime lookup normalizes the DB's author string and
 * matches it directly — a key left in raw form is silently unreachable.
 *
 * A DEATH year, not a birth year: we are ordering *works*, and output clusters near the end
 * of a life. Dating by birth would place a long-lived author a full generation before the
 * contemporaries he was actually writing alongside.
 *
 * 373 entries were curated earlier from HaMichlol / chronological charts. The rest were
 * web-sourced per author against HaMichlol, Wikidata, and NLI / HebrewBooks authority
 * records, under a strict rule: a year is recorded only when a retrieved source states it.
 * Nothing is inferred from a century, a print date, or an author's era — names that could
 * not be resolved to one identifiable person with a documented year were left out entirely
 * and sort last, which is the honest outcome. Comments give the English name and the source
 * host; "medium" marks entries where identification required judgement (a shared name, or a
 * patronymic-only match) or where sources disagreed on the year.
 */
export const AUTHOR_YEARS: Record<string, number> = {
  "בן סירא": -180,
  "יוסף בן מתתיהו": 100,
  "אונקלוס": 120,
  "אחאי גאון": 752,
  "סעדיה גאון": 942,
  "מנחם אבן סרוק": 970,
  "גרשום בן יהודה": 1028,
  "ר משה הדרשן": 1050,
  "רבנו חננאל": 1055,
  "שלמה בן יהודה אבן גבירול": 1058,
  "ריף": 1103,
  "רשי": 1105,
  "שמחה בן שמואל מויטרי": 1105,
  "שמעיה השושני": 1105,
  "רבי נתן בן יחיאל": 1106,
  "טוביה בן אליעזר": 1108,
  "רבינו אפרים": 1110,
  "בחיי אבן פקודה": 1120,
  "רבי יהודה בן ברזילי הברצלוני": 1130,
  "יהודה הלוי": 1141,
  "יוסף אבן מיגאש": 1141,
  "מנחם בן שלמה": 1150,
  "רשבם": 1158,
  "אבן עזרא": 1167,
  "אברהם אבן עזרא": 1167, // Abraham ibn Ezra — en.wikipedia.org — medium
  "יוסף קמחי": 1170,
  "יעקב בן מאיר תאמים": 1171,
  "יוסף בכור שור": 1175,
  "זרחיה הלוי": 1186,
  "משה קמחי": 1190,
  "יצחק בן אבא מרי": 1193, // Yitzchak ben Abba Mari of Marseille (author of Sefer HaIttur) — hamichlol.org.il — medium
  "ראבד": 1198,
  "יצחק בן שמואל הזקן מדמפייר": 1200,
  "מאיר בן יצחק שליח צבור": 1200,
  "להרמבם זל": 1204, // Moses Maimonides (Rambam) — wikidata.org — medium
  "רמבם": 1204,
  "יהונתן מלוניל": 1210,
  "ברוך בן יצחק מוורמיזא": 1211,
  "רבי יהודה בן שמואל החסיד": 1217,
  "רבינו יהודה החסיד": 1217, // Judah ben Samuel the Pious of Regensburg (Sefer Chasidim) — en.wikipedia.org
  "אלעזר מגרמייזא": 1230, // Eleazar of Worms (author of Sefer HaRokeach) — hamichlol.org.il — medium
  "שמשון בן אברהם משאנץ": 1230,
  "רבנו דוד בר יוסף קמחי": 1235, // David Kimchi (Radak), son of Yosef Kimchi — hamichlol.org.il
  "רדק": 1235,
  "אברהם בן הרמבם": 1237,
  "אליעזר ממיץ": 1237,
  "אלעזר רוקח": 1238,
  "עזרא בן שלמה מגרונה": 1238,
  "עזריאל בן מנחם": 1238,
  "יעקב הלוי ממרויש": 1244,
  "מאיר הלוי אבולעפיה": 1244,
  "יהודה בן אליעזר": 1250,
  "יצחק בן משה מווינה": 1250,
  "ישעיה דטראני": 1250,
  "נתן בן יהודה": 1250,
  "תוספות": 1250,
  "יעקב אנטולי": 1256, // Yaakov Anatoli (Jacob Anatoli, author of Malmad HaTalmidim) — nli.org.il — medium
  "חזקוני": 1260,
  "משה בן יעקב מקוצי": 1260,
  "יונה גירונדי": 1263, // Yonah Gerondi (Rabbenu Yonah of Girona) — hamichlol.org.il
  "רבינו יונה": 1263,
  "רמבן": 1270,
  "יצחק בן יוסף מקורביל": 1280,
  "ר צדקיה בן ר אברהם הרופא": 1280,
  "זרחיה בן שאלתיאל חן (גרסיאן)": 1290,
  "רבי יחיאל בן יקותיאל הרופא": 1290,
  "אברהם אבולעפיה": 1291,
  "מהרם מרוטנבורג": 1293,
  "פרץ בן אליהו מקורביל": 1297,
  "מרדכי": 1298,
  "אהרן הלוי": 1300,
  "רבי יצחק בן יהודה הלוי": 1300,
  "רבינו מנוח": 1300,
  "משה די ליאון זל": 1305, // Moshe de Leon — hamichlol.org.il — medium
  "מנחם ריקנטי": 1310,
  "רשבא": 1310,
  "שמשון בן צדוק": 1312,
  "המאירי": 1315,
  "מנחם בר שלמה לבית מאיר": 1316, // Menachem ben Shlomo of the house of Meir (the Meiri, Beit HaBechirah) — nli.org.il
  "יוסף בן אברהם אבן גקטילה": 1325,
  "יוסף בן שלום אשכנזי": 1325,
  "אשר בן יחיאל": 1327, // Asher ben Yechiel (Rosh) — hamichlol.org.il
  "ראש": 1327,
  "יום טוב בן אברהם אשבילי": 1330, // Yom Tov ben Avraham Asevilli (Ritva) — hamichlol.org.il — medium
  "יעקב בן חננאל סקילי": 1330,
  "יצחק בן משה הלוי": 1330,
  "ריטבא": 1330,
  "שם טוב בן אברהם אבן גאון": 1330,
  "אבא מארי בר משה מלוניל": 1337,
  "אבודרהם": 1340,
  "בחיי בן אשר": 1340,
  "ידעיה הפניני": 1340,
  "יעקב בן אשר": 1340,
  "רלבג": 1344,
  "יוסף אבן כספי": 1345,
  "יהודה בן הראש": 1349, // Judah ben Asher (son of the Rosh) — hamichlol.org.il
  "ישראל בן יוסף אלנאקוה": 1350,
  "רבנו ירוחם": 1350,
  "וידאל די טולושא": 1360,
  "הנרבוני": 1362,
  "משה חלאווה": 1370, // Moshe Halawa (Maharam Halawa) — nli.org.il — medium
  "נסים בן ראובן גירונדי": 1376, // Nissim ben Reuven Gerondi (the Ran) of Barcelona — hamichlol.org.il
  "רן": 1376,
  "אשר בן אברהם קרשקש": 1400,
  "יצחק בר ששת": 1408,
  "חסדאי בן אברהם קרשקש": 1410,
  "יוסף חביבא": 1420,
  "יעקב הלוי בן משה מולין": 1427, // Yaakov ben Moshe HaLevi Moelin (the Maharil) — en.wikipedia.org
  "יעקב לוי מולין": 1427,
  "שמעון בן צמח דוראן": 1444,
  "יעקב וייל": 1456,
  "ישראל איסרלן": 1460,
  "מהרח אור זרוע": 1460,
  "יצחק קנפנטון": 1463,
  "שלמה בן שמעון דוראן": 1467,
  "יוסף קולון": 1480,
  "ישראל בן חיים ברונא": 1480,
  "יצחק בר יקותיאל זלמן": 1490,
  "שם טוב בן יוסף אבן שם טוב": 1490,
  "יצחק בן משה עראמה": 1494,
  "יעקב לנדא (האגור)": 1500,
  "ישעיה מיאנוב": 1500,
  "אברבנאל": 1508,
  "אברהם סבע": 1508,
  "יהודה בן אליעזר מינץ": 1508, // Judah Mintz (Mahara"i Mintz) of Padua — hamichlol.org.il — medium
  "יצחק אברבנאל": 1508, // Isaac Abarbanel (Abravanel) — wikidata.org
  "עובדיה מברטנורא": 1515,
  "יעקב בן שלמה אבן חביב": 1516,
  "אליהו בן אברהם מזרחי": 1526,
  "אליהו מזרחי": 1526, // Elijah Mizrachi (the Re'em) — hamichlol.org.il
  "יצחק בן יוסף קארו": 1535,
  "יוסף אבן יחייא": 1539,
  "אבן גבאי, מאיר בן יחזקאל": 1540, // Meir ibn Gabbai, author of Avodat HaKodesh and Tolaat Yaakov — nli.org.il — medium
  "יוסף קורקוס": 1540,
  "מאיר בן יחזקאל אבן גבאי": 1540,
  "לוי בן חביב": 1545,
  "אליהו בחור": 1549,
  "יצחק ליאון אבן צור": 1550,
  "ספורנו": 1550,
  "שלמה סיריליו": 1554,
  "יהושע בועז": 1557,
  "יוחנן בן יוסף טריוויש": 1560,
  "מהרם פדובה": 1565,
  "משה קורדובירו": 1570,
  "משה בן ישראל איסרליש": 1572,
  "דוד בן שלמה אבן אבי זמרא": 1573,
  "דוד בן שלמה אבן זמרא": 1573, // David ben Shlomo ibn Zimra (Radbaz) — hamichlol.org.il
  "מהרשל": 1573,
  "יוסף קארו": 1575, // Joseph Caro — nli.org.il
  "משה טראני": 1580, // Moshe di Trani (the Mabit) — he.wikipedia.org
  "משה מטראני": 1580,
  "רבי משה אלמושנינו": 1580,
  "שלמה הלוי אלקבץ": 1584,
  "שמעון לביא": 1585,
  "אליהו די וידאש": 1587,
  "חיים בן בצלאל": 1588,
  "שמואל די מדינה": 1589,
  "יצחק בן מרדכי גרשון": 1590,
  "יוסף כץ": 1591, // Yosef Katz of Krakow (author of She'erit Yosef) — hamichlol.org.il — medium
  "אלשיך": 1593,
  "בצלאל אשכנזי": 1594, // Bezalel Ashkenazi (Shita Mekubetzet) — hamichlol.org.il — medium
  "בצלאל בן אברהם אשכנזי": 1594,
  "שמואל יפה אשכנזי": 1595,
  "אברהם חיון": 1600,
  "אלעזר בן משה אזכרי": 1600,
  "יעקב משה אשכנזי": 1600,
  "שמואל די אוזידא": 1604,
  "אברהם די בוטון": 1605,
  "משה מאט": 1606, // Moshe Mat of Przemysl (author of Mateh Moshe) — hamichlol.org.il
  "מהרל": 1609,
  "מרדכי כהן": 1610,
  "משה בן מכיר": 1610,
  "הרב מרדכי יפה": 1612,
  "יהושע פלק בן אלכסנדר הכהן": 1614,
  "יהושע פלק בן יצחק אייזיק מליסא": 1614,
  "מהרם לובלין": 1616,
  "שלמה אפרים מלונטשיץ": 1619,
  "בנימין בן שאול קצנלנבוגן": 1620,
  "חיים ויטאל": 1620,
  "יששכר בער": 1623,
  "יששכר בער איילינברג": 1623, // Issachar Ber Eilenburg (Be'er Sheva, Tzeidah la-Derech) — hebrewbooks.org
  "יעקב בן יצחק אשכנזי": 1625,
  "ידידיה שלמה רפאל נורצי": 1626,
  "מנחם בן יהודה די לונזאנו (הרמדל)": 1626, // Menachem ben Yehuda di Lonzano (Ramdal) — hamichlol.org.il — medium
  "אברהם בן יצחק צהלון": 1630,
  "ישעיהו הלוי הורוויץ": 1630,
  "שלמה עדני": 1630,
  "מהרשא": 1631,
  "אברהם חיים שור": 1632, // Avraham Chaim Shor ben Naftali Tzvi Hirsch (author of Toras Chaim, Tzon Kodashim), av beis din of Belz — nli.org.il
  "נתן נטע שפירא": 1633,
  "אהרן ברכיה ממודנה": 1639,
  "יוסף מטראני": 1639,
  "יואל בן שמואל סירקיש": 1640,
  "מאיר הכהן שיף": 1641,
  "אברהם אזולאי": 1643,
  "בנימין בן אברהם מוטל": 1650,
  "אברהם אליגרי": 1652,
  "יום-טוב ליפמן הלר": 1654,
  "משה בן יצחק יהודה": 1656, // Moshe ben Yitzchak Yehuda Lima — hamichlol.org.il
  "מנשה בן ישראל": 1657,
  "אליעזר פואה": 1659,
  "מנחם מנדעל בן אברהם קרועמל": 1661, // Menachem Mendel Krochmal ben Avraham (author of Tzemach Tzedek) — hamichlol.org.il
  "כץ, שבתי בן מאיר הכהן": 1662, // Shabbatai ben Meir HaKohen (the Shach, author of Siftei Kohen) — en.wikipedia.org
  "העשיל מקראקא, אברהם יהושע העשל מקראקא": 1663,
  "פינחס בן פילטא": 1663, // Pinchas ben Pilta — hebrewbooks.org
  "שבתי בן מאיר הכהן": 1663,
  "שבתי כהן": 1663, // Shabtai ben Meir HaKohen (the Shach) — hamichlol.org.il — medium
  "דוד הלוי סגל": 1667,
  "משה בן נפתלי הירש רבקש": 1671,
  "ישראל יעקב בן שמואל חגיז": 1674,
  "אהרון שמואל קוידנובר": 1676, // Aharon Shmuel Kaidanover (Maharshak), author of Birkat HaZevach — nli.org.il
  "אהרן שמואל קאידנוור": 1676,
  "אברהם אבלי הלוי גומבינר": 1682,
  "מנחם מנדל אוירבך": 1689,
  "דוד קונפורטי": 1690, // David Konforti (Conforte), author of Kore HaDorot — nli.org.il
  "בצלאל בן שלמה מקוברין": 1691, // Betzalel ben Shlomo of Kobryn — hebrewbooks.org
  "גרשון בן יצחק אשכנזי": 1693, // Gershon Ashkenazi (author of Avodat ha-Gershuni) — hamichlol.org.il
  "חזקיה די סילוה": 1695,
  "משה בן רבי שלמה אבן חביב": 1696, // Moshe ben Shlomo ibn Chaviv of Jerusalem (author of Get Pashut, Ezras Nashim) — nli.org.il
  "יאיר חיים בכרך": 1702,
  "יאיר חיים בן משה שמשון בכרך": 1702, // Yair Chaim Bacharach (author of Chavot Yair) — wikidata.org
  "זכריה מנדל בן אריה לייב": 1706,
  "שמואל בן ר אורי שרגא פייבוש": 1706,
  "בנימין עוזר הכהן": 1710, // Binyamin Ozer HaKohen ben Meir (author of Even HaOzer), av beis din of Klimontow — hamichlol.org.il
  "אליהו שפירא": 1712,
  "יונה לנדסופר": 1712, // Yonah Landsofer (author of Meil Tzedakah / Kanfei Yonah) — nli.org.il
  "צבי הירש קאיידנובר": 1712,
  "צבי אשכנזי": 1718,
  "שבתי בס": 1718,
  "יהודה רוזאניס": 1727,
  "דוד ניטו": 1728,
  "אליהו הכהן האיתמרי": 1729,
  "יוסף בן עמנואל אירגס": 1730,
  "יעקב פראגי": 1730, // Yaakov Fraji (Mahari"f) of Alexandria — hamichlol.org.il — medium
  "נפתלי בן שמעון הריץ": 1730,
  "יעקב ריישר": 1733,
  "אפרים נבון": 1735,
  "אלכסנדר סנדר שור": 1737,
  "חיים בן עטר": 1743,
  "יהודה בן שמעון אשכנזי": 1743,
  "עמנואל חי בן אברהם ריקי": 1743,
  "ריקי, רפאל עמנואל חי בן אברהם": 1743, // Raphael Immanuel Chai Ricchi, author of Mishnat Chassidim — nli.org.il
  "מאיר איזנשטאט": 1744, // Meir Eisenstadt (Panim Meirot) — nli.org.il
  "יחיאל הלפרין": 1746,
  "לרבנו הרמחל זצללהה": 1746, // Moshe Chaim Luzzatto (Ramchal) — hamichlol.org.il
  "משה חיים לוצאטו": 1746,
  "משה חיים לוצאטו (הרמחל)": 1746, // Moshe Chaim Luzzatto (Ramchal), author of Mesillas Yesharim — hamichlol.org.il
  "משה חיים לוצטו": 1746, // Moshe Chaim Luzzatto (Ramchal) — he.wikipedia.org
  "רבנו הרמחל זצללהה": 1746, // Moshe Chaim Luzzatto (Ramchal) — hamichlol.org.il
  "משה בן יעקב ישראל חגיז": 1750, // Moshe Chagiz — hamichlol.org.il
  "אלגאזי, ישראל יעקב בן יום טוב": 1756, // Yisrael Yaakov Algazi (Shalmei Tzibur, Rishon LeZion of Jerusalem) — he.wikipedia.org — medium
  "יעקב יהושע פלק": 1756,
  "ישראל יעקב אלגזי": 1756, // Israel Yaakov Algazi — hamichlol.org.il — medium
  "בעל שם טוב": 1760,
  "דוד פרנקל": 1762,
  "אייבשיץ, יהונתן בן נתן נטע": 1764, // Yonatan Eybeschutz — hamichlol.org.il
  "יהונתן אייבשיץ": 1764,
  "מסעוד חי רקח": 1768,
  "ווייל, יעקב נתנאל בן נפתלי צבי הירש": 1769, // Netanel Weil (Korban Netanel) — nli.org.il — medium
  "נתנאל וייל": 1769,
  "רבי דוד אלטשולר": 1769,
  "מנחם מנדל מפרמישלן": 1771,
  "דב בער ממזריטש": 1772,
  "ישראל בן משה הלוי זמושץ": 1772,
  "צדקה בן סעדיה חוצין": 1772, // Tzedaka Hutzin the First ben Saadia (author of Tzedaka u-Mishpat) — hamichlol.org.il
  "יצחק בן משה נוניש בילמונטי": 1774,
  "יעקב עמדין": 1776,
  "אלחנן אשכנזי": 1780, // Elchanan Ashkenazi (Sidrei Tahara) — hamichlol.org.il
  "אליעזר יהודה מפינטשוב": 1780,
  "אריה ליב זיטל סגל הורוביץ": 1780,
  "שלום בוזגלו": 1780,
  "משה מרגלית": 1781,
  "שלמה בן יחיאל שלם": 1781,
  "יעקב יוסף מפולנאה": 1783,
  "אריה ליב גינזבורג": 1785, // Aryeh Leib Ginzburg (author of Shaagat Aryeh) — hamichlol.org.il
  "אריה לייב בר אשר גינצבורג": 1785, // Aryeh Leib ben Asher Gunzberg (Shaagat Aryeh) — hamichlol.org.il
  "אריה לייב גינצבורג": 1785,
  "מייזלש, עוזיאל בן צבי": 1785, // Uziel Meisels (Tiferet Uziel) — hamichlol.org.il
  "אלימלך וייסבלום מליזנסק": 1787,
  "חיים חייקל מאמדור": 1787,
  "מנחם מנדל מוויטבסק": 1788,
  "דוד פארדו": 1790,
  "פינחס בן אברהם אבא שפירא": 1790,
  "רפאל בן זכריה מנדל": 1790,
  "יוסף בן מאיר תאומים": 1792, // Yosef ben Meir Teomim (Pri Megadim) — nli.org.il
  "יוסף בר מאיר תאומים": 1792, // Yosef ben Meir Teomim, author of Pri Megadim — nli.org.il
  "יחזקאל לנדא": 1793,
  "אלכסנדר זיסקינד מהוראדנא, בעל יסוד ושורש העבודה": 1794,
  "חיים מודעי": 1794, // Chaim Modai (Moda'i) — hamichlol.org.il
  "משולם פייבוש הלר מזברז": 1794, // Meshullam Feivush Heller of Zbarazh — hamichlol.org.il
  "אליקים בן יצחק גאטינייו": 1795,
  "אליהו בן שלמה זלמן מווילנה": 1797,
  "מנחם נחום מטשרנובל": 1797,
  "רבנו אליהו מווילנא זצללהה": 1797, // Elijah ben Solomon Zalman, the Vilna Gaon — hamichlol.org.il
  "זאב וולף מזיטומיר": 1798,
  "ישעיה פיק ברלין": 1799,
  "דוב בער בן שמואל מלינץ": 1800,
  "משה חיים אפרים מסדילקוב": 1800,
  "ר נתן אדלר": 1800,
  "משה משולם בן שמשון איגרא": 1801, // Moshe Meshulam Igra (Meshulam Igra of Tysmenitz) — hamichlol.org.il
  "אלכסנדר סנדר מרגליות": 1802, // Alexander Sender Margaliot — hamichlol.org.il
  "צבי הירש מנדבורנא": 1802, // Tzvi Hirsch of Nadvorna — hamichlol.org.il — medium
  "שלמה בן אליעזר ליפמן הכהן מליסא": 1802,
  "חיים אברהם כץ": 1804,
  "חנניה קזיס": 1804,
  "יעקב קרנץ": 1804,
  "קראנץ, יעקב בן זאב (המגיד מדובנא)": 1804, // Yaakov Kranz, the Maggid of Dubno (Dubner Maggid) — en.wikipedia.org
  "הורביץ, פינחס בן צבי הירש": 1805, // Pinchas Horowitz (Baal HaHafla'ah) — hamichlol.org.il
  "ידידיה טיאה ווייל": 1805,
  "פנחס הלוי איש הורוויץ": 1805,
  "חיים דוד אזולאי": 1806,
  "חיים יוסף דוד אזולאי": 1806, // Chaim Yosef David Azulai, the Chida — hamichlol.org.il
  "שמואל קעלין": 1806,
  "דניאל בן יעקב מהורודנה": 1807, // Daniel ben Yaakov of Grodno (author of Chamudei Daniel) — nli.org.il
  "מאיר פוזנר": 1807, // Meir Pozner (Beit Meir) — hamichlol.org.il
  "לוי יצחק מברדיצב": 1809,
  "משולם בן יואל כץ": 1809,
  "שאול ישועה אביטבול": 1809, // Shaul Yeshua Abitbol, av beit din of Sefrou — he.wikipedia.org
  "יצחק מאיו": 1810,
  "נחמן מברסלב": 1810,
  "יעקב (יאקב) נאומבורג": 1811,
  "אריה ליב בן יוסף הכהן (בעל הקצות)": 1812, // Aryeh Leib HaKohen Heller (author of Ketzot HaChoshen) — nli.org.il
  "אריה לייב הכהן": 1812, // Aryeh Leib HaKohen Heller, author of Ketzot HaChoshen — hamichlol.org.il — medium
  "שניאור זלמן מליאדי": 1812,
  "אריה לייב בן יוסף הכהן הלר": 1813,
  "דוד שלמה אייבשיץ": 1813,
  "סעדיה בן נתן נטע": 1813,
  "ישראל הופשטיין מקוזניץ": 1814,
  "יעקב יצחק הורוביץ": 1815,
  "מנחם מנדל מרימנוב": 1815, // Menachem Mendel of Rymanow — hamichlol.org.il
  "חיים בן שלמה טירר מטשטרנוביץ": 1817,
  "בנימין זאב וולף בוסקוביץ": 1818,
  "זאב בן אריה": 1818,
  "י-הודה הלר": 1819, // Yehuda Kahana Heller of Sighet — hamichlol.org.il
  "אברהם דנציג": 1820,
  "אברהם טיקטין": 1820, // Avraham Tiktin, rabbi of Breslau (author of Petach HaBayis) — hamichlol.org.il — medium
  "טעבעלה באנדי": 1820,
  "חיים איצקוביץ": 1821,
  "חיים בן יצחק מוולוזין": 1821, // Chaim of Volozhin (Chaim ben Yitzchak) — hamichlol.org.il
  "רפאל בן מרדכי ברדוגו": 1821, // Refael Berdugo of Meknes ben Mordechai — nli.org.il
  "יהושע צייטלין": 1822,
  "חיים מרדכי מרגליות": 1823,
  "קלונימוס קלמן אפשטיין": 1823,
  "אברהם יהושע השיל": 1825,
  "אלעזר פלקלס": 1826,
  "דב בער שניאורי": 1827,
  "נפתלי צבי הורוביץ": 1827,
  "אהרון הלוי הורוביץ": 1828,
  "אליעזר פאפו": 1828,
  "אפרים זלמן מרגליות": 1828,
  "ברוך פרנקל תאומים": 1828, // Baruch Frankel-Teomim (Baruch Taam) — hamichlol.org.il
  "ברוך תאומים-פרנקל": 1828,
  "משה אליקים בריעה": 1828, // Moshe Elyakim Beriah Hopstein of Kozhnitz (author of Be'er Moshe, Da'at Moshe) — hamichlol.org.il — medium
  "משה זאב וולף מרגליות": 1829, // Moshe Zev (Velvel) Margoliot — hamichlol.org.il
  "מאיר הלוי רוטנברג מאפטא": 1831,
  "יעקב בן יעקב משה לורברבוים": 1832, // Yaakov ben Yaakov Moshe Lorberbaum of Lissa (Netivot HaMishpat) — he.wikipedia.org
  "יעקב בן יעקב משה לורברבוים מליסא": 1832, // Yaakov Lorberbaum of Lissa (author of Nesivos HaMishpat, Chavas Daas) — hamichlol.org.il
  "יעקב בן יעקב משה מליסא": 1832,
  "אריה לייב צינץ": 1833, // Aryeh Leib Zinz (Maharal Zinz) of Plotzk — hamichlol.org.il
  "צינץ, אריה ליב בן משה": 1833, // Aryeh Leib Zinz (Maharal Zinz of Plock) — nli.org.il
  "עקיבא איגר": 1837,
  "הלל ריבלין": 1838,
  "שלמה חכים": 1838,
  "יעקב משולם אורנשטיין": 1839, // Yaakov Meshulam Ornstein of Lvov (author of Yeshuot Yaakov) — hamichlol.org.il
  "ישראל משקלוב": 1839,
  "לונשטם, אברהם בן אריה ליב": 1839, // Avraham Lonshtam (Lowenstamm) ben Aryeh Leib — beta.hebrewbooks.org — medium
  "משה סופר": 1839,
  "אברהם דב מאבריטש": 1840,
  "אברהם דוד וואהרמן": 1840,
  "יעקב בן יקותיאל בירדוגו": 1841, // Yaakov ben Yekutiel Birdugo (Shufrieh d'Yaakov) — hamichlol.org.il — medium
  "משה טייטלבוים מאוהעל": 1841,
  "צבי אלימלך שפירא מדינוב": 1841,
  "שפירא, צבי אלימלך בן פסח, מדינוב,": 1841, // Tzvi Elimelech Shapira of Dynow (author of Bnei Yissachar) — en.wikipedia.org — medium
  "אפרים יצחק מפשמיש": 1842, // Ephraim Yitzchak of Przemysl (Premishla) — hamichlol.org.il
  "נחום טרייביטש": 1842,
  "נתן שטרנהרץ": 1844,
  "יחזקאל פנט": 1845,
  "ארי לייבוש ליפשיץ": 1846, // Aryeh Leibush Lipschitz of Vishnitza, author of Aryeh D'Bei Ilai — hamichlol.org.il
  "י-הודה שמואל אשכנזי": 1849, // Yehuda Shmuel Ashkenazi (author of Bet Oved) — hamichlol.org.il — medium
  "ישראל פרידמן מרוזין": 1850, // Israel Friedman of Ruzhin — nli.org.il
  "יצחק מינקובסקי": 1851, // Yitzchak Minkowski of Karlin (author of Keren Orah) — hamichlol.org.il
  "לוו, בנימין וולף בן אלעזר בן אריה ליב": 1851, // Benjamin Wolf Low (Shaarei Torah) — hamichlol.org.il
  "יצחק אייזיק חבר": 1852,
  "יצחק אייזק חבר": 1852, // Yitzchak Eizik Chaber (Wildman) — hamichlol.org.il
  "מאיר אייזנשטטר (אייזנשטט)": 1852, // Meir Eisenstetter (Maharam Ash) — hamichlol.org.il — medium
  "נחמיה הלוי בירך גינזבורג מדוברובנה": 1852, // Nechemiah HaLevi Beirach Ginzburg of Dubrovna (Divrei Nechemiah) — he.wikipedia.org
  "אליעזר יצחק פריד": 1853, // Eliezer Yitzchak Fried — hamichlol.org.il
  "יעקב מאיר פדואה": 1854, // Yaakov Meir ben Chaim Padua of Brisk — hamichlol.org.il
  "מרדכי ליינר": 1854,
  "דוד לוריא": 1855,
  "דוד לוריא (רדל)": 1855, // David Luria (RaDaL) — hamichlol.org.il
  "צבי הירש חיות": 1855,
  "אברהם בן יצחק ענתיבי": 1858, // Abraham ben Isaac Antebi — hamichlol.org.il
  "חנוך זונדל בן יוסף": 1859,
  "ישראל ליפשיץ": 1860,
  "דוד טבל בן משה, ממינסק": 1861, // David Tevel ben Moshe of Minsk (Nachalat David) — he.wikipedia.org
  "רובין, דוד טבל בן משה": 1861, // David Tevel Rubin of Minsk (author of Nachalat David) — hamichlol.org.il
  "זאב וולף איינהורן": 1862,
  "אברהם שמחה אבד סטיסלאוו": 1864, // Avraham Simcha of Amtchislav (Mstislavl), av beit din — hamichlol.org.il — medium
  "יצחקי, אברהם בן יצחק חי הכהן": 1864, // Avraham HaKohen Yitzchaki of Tunis (author of Mishmarot Kehunah) — hamichlol.org.il — medium
  "יעקב צבי מקלנבורג": 1865,
  "משה יהודה לייב זילברבגר": 1865, // Moshe Yehuda Leib Zilberberg of Kutno (Zayit Raanan) — hamichlol.org.il
  "יצחק מאיר אלתר (רוטנברג)": 1866, // Yitzchak Meir Alter (Rothenberg), Chiddushei HaRim of Ger — he.wikipedia.org
  "יצחק מאיר רוטנברג (אלתר)": 1866,
  "שלמה הכהן רבינוביץ": 1866,
  "אברהם צבי הירש בן יעקב אייזנשטאט": 1868,
  "חיים פלאגי": 1868,
  "קלוגר, שלמה בן יהודה אהרן": 1869, // Shlomo Kluger (Maharshak, the Maggid of Brody) — nli.org.il
  "שלמה קלוגר": 1869,
  "יעקב אטלינגר": 1871,
  "יעקב יוקב אטלינגר": 1871, // Jacob Ettlinger (Aruch la-Ner) — hamichlol.org.il
  "יעקב עטלינגר": 1871, // Jacob Ettlinger, author of Aruch LaNer — hamichlol.org.il
  "אהרן פרלוב מקרלין": 1872,
  "שלמה דרימר": 1872, // Shlomo Drimer of Skala, author of Beit Shlomo — hamichlol.org.il — medium
  "שמואל שטראשון": 1872,
  "יהושע יצחק (אייזיק) שפירא מסלונים": 1873, // Yehoshua Yitzchak (Aizik) Shapira of Slonim — hamichlol.org.il
  "יהושע יצחק שפירא": 1873, // Yehoshua Yitzchak Shapira (Reb Aizel Charif) of Slonim — hamichlol.org.il
  "יהושע יצחק שפירא (רבי אייזל חריף)": 1873, // Yehoshua Yitzchak Shapira (R' Aizel Charif) of Slonim — hamichlol.org.il
  "שפירא, יהושע איזיק בן יחיאל": 1873, // Yehoshua Isaac Shapira ben Yechiel (Emek Yehoshua, rabbi of Slonim) — he.wikipedia.org
  "אברהם חיים בן מסעוד חי אדאדי": 1874, // Avraham Chaim ben Masoud Chai Adadi of Tripoli (author of Vayikra Avraham) — hamichlol.org.il
  "יוסף בן משה באבד": 1874,
  "יצחק אייזיק ספרין": 1874,
  "ירמיה לעוו": 1874,
  "אברהם לנדא מצכנוב": 1875, // Avraham Landau of Ciechanow (author of Zichron Avraham) — nli.org.il
  "יוסף שאול נתנזון": 1875,
  "אברהם דיין": 1876, // Avraham Dayan of Aleppo — hamichlol.org.il
  "דוד מועטי": 1876, // David ben Shmuel Moatti — hamichlol.org.il
  "צוובנר, אברהם בן יהודה ליב שאג": 1876, // Avraham Shag-Zwebner, rabbi of Kobersdorf and later Jerusalem — hamichlol.org.il
  "יעקב גזונדהייט": 1878, // Jacob Gesundheit (Tiferet Yaakov) — nli.org.il
  "יעקב ליינר מאיזביץ": 1878,
  "מאיר אוירבך": 1878, // Meir Auerbach of Kalisz and Jerusalem (author of Imrei Binah) — nli.org.il
  "מאיר לייבוש מלבים": 1879,
  "מלבים": 1879,
  "משה שיק": 1879, // Moshe Schick (Maharam Schick), av beis din of Chust — hamichlol.org.il
  "רפאל יום טוב ליפמן היילפרין": 1879, // Raphael Yom Tov Lipman Halperin (Oneg Yom Tov) — nli.org.il
  "יהושע העליר": 1880, // Yehoshua Heller of Telz/Vilna, author of Chosen Yehoshua — hamichlol.org.il
  "אליהו בן הרוש": 1883,
  "ישראל סלנטר": 1883,
  "סופר, שמעון בן אברהם שמואל בנימין": 1883, // Shimon Sofer of Krakow (Michtav Sofer) — hamichlol.org.il
  "אלכסנדר סנדר הכהן קפלן": 1884, // Alexander Sender HaKohen Kaplan (Shalmei Nedarim) — hamichlol.org.il
  "ברוך מרדכי ליבשיץ": 1885, // Baruch Mordechai Lifshitz, author of Brit Yaakov — hamichlol.org.il — medium
  "רבי שלמה בן יוסף גאנצפריד": 1886,
  "אורנשטיין, צבי הירש בן מרדכי זאב": 1888, // Tzvi Hirsch Ornstein, rabbi of Lvov — hamichlol.org.il
  "שמשון רפאל הירש": 1888,
  "אהרן משה פאדווא מקרלין": 1890,
  "גרשון חנוך העניך ליינר": 1890,
  "יצחק אהרן איטינגא": 1891, // Yitzchak Aharon Ettinger of Lvov (Maharya HaLevi) — hamichlol.org.il — medium
  "מאיר יונה ברנצקי": 1891,
  "יוסף דוב הלוי סולובייציק": 1892,
  "יעקב משה": 1893, // Yaakov Moshe ben Avraham of David-Horodok — nli.org.il
  "ישראל יהושע בן דוד טרונק": 1893, // Yisrael Yehoshua Trunk of Kutno — hamichlol.org.il
  "נפתלי צבי יהודה ברלין": 1893,
  "חזקיה פיבל פלויט": 1894, // Chizkiyah Feivel Plaut (Likutei Chaver ben Chaim) — hamichlol.org.il
  "יצחק אלחנן בן ישראל ספקטור": 1896, // Yitzchak Elchanan Spektor of Kovno — hamichlol.org.il
  "דוד ועקנין": 1897, // David Vaaknin (Ouaknin) of Tiberias — hamichlol.org.il — medium
  "צדוק הכהן": 1900, // Tzadok HaKohen Rabinowitz of Lublin — hamichlol.org.il — medium
  "צדוק הכהן רבינוביץ": 1900,
  "צדוק מלובלין": 1900, // Tzadok HaKohen Rabinowitz of Lublin — hamichlol.org.il
  "שניאורסון, שלמה זלמן": 1900, // Shlomo Zalman Schneersohn of Kopust — nli.org.il
  "טריווש, הילל דוד בן עוזר הכהן": 1901, // Hillel David HaKohen Trivush (Trivash) — hamichlol.org.il
  "אריה לייב ליפקין": 1902,
  "שניאור זלמן פרדקין": 1902, // Shneur Zalman Fradkin of Lublin (Torat Chesed) — nli.org.il
  "שטרן, יוסף זכריה בן נתן": 1903, // Yosef Zecharia Stern ben Natan (Zecher Yehosef of Shavel/Siauliai) — he.wikipedia.org
  "אברהם משכיל לאיתן": 1904,
  "חיים חזקיהו מדיני": 1904,
  "אברהם אבא שיף ממינסק": 1905,
  "אליהו דוד רבינוביץ תאומים": 1905,
  "י-הודה ליב אלתר מגור": 1905, // Yehudah Aryeh Leib Alter of Gur, author of Sfas Emes — hamichlol.org.il
  "יהודה אריה ליב אלתר": 1905,
  "יצחק שמלקיש": 1905, // Yitzchak Yehudah Shmelkes, rabbi of Lvov (author of Beis Yitzchak) — hebrewbooks.org
  "ישראל איזנשטיין": 1905,
  "שלמה הכהן": 1905,
  "שלמה הכהן מווילנה": 1905, // Shlomo HaKohen of Vilna (Cheshek Shlomo / Binyan Shlomo) — hamichlol.org.il
  "שלמה בובר": 1906,
  "שמחה בונים סופר": 1906, // Simcha Bunim Sofer (Shevet Sofer) — hamichlol.org.il
  "יצחק בלזר": 1907, // Yitzchak Blazer (Itzele Peterburger) — hamichlol.org.il
  "עמרם בלום": 1907, // Amram Blum (Beit Shearim) — hamichlol.org.il
  "שלמה י-הודה טבק": 1907, // Shlomo Yehuda Tabak of Sighet (Teshurat Shai) — hamichlol.org.il
  "אליהו בכור חזן": 1908, // Eliyahu Bechor Hazan (Ta'alumot Lev, chief rabbi of Alexandria) — he.wikipedia.org
  "הרב יחיאל מיכל הלוי אפשטין": 1908,
  "יוסף חיים": 1909,
  "אברהם בורנשטיין": 1910, // Avraham Bornstein of Sochaczew (author of Avnei Nezer, Eglei Tal) — hamichlol.org.il
  "מלכיאל צבי טננבוים": 1910, // Malkiel Tzvi Tenenbaum, rabbi of Lomza (author of Divrei Malkiel) — hamichlol.org.il
  "מרקוס הורוביץ": 1910,
  "משה דנושבסקי": 1910, // Moshe Danishevsky (Danushevsky) — hamichlol.org.il — medium
  "שלום מרדכי בן משה הכהן שבדרון": 1911, // Shalom Mordechai ben Moshe HaKohen Shvadron (the Maharsham) of Berezhany — nli.org.il
  "שלום מרדכי בן משה הכהן שוודרן": 1911,
  "שלום מרדכי שבדרון": 1911, // Shalom Mordechai Schwadron (Maharsham) of Berezhany — hebrewbooks.org
  "דוד גרינהוט": 1913,
  "יעקב דוד וילובסקי": 1913,
  "יצחק יעקב ריינעס": 1915, // Yitzchak Yaakov Reines (founder of Mizrachi) — hamichlol.org.il
  "קלוגר, אברהם בנימין בן שלמה": 1915, // Avraham Binyamin Kluger — hamichlol.org.il
  "יוסף זונדל בן חיים הוטנר": 1919, // Yosef Zundel Hutner — hamichlol.org.il
  "יוסף יוזל הורוביץ": 1919,
  "יוסף ענגיל": 1919, // Yosef Engel of Krakow (author of Gilyonei HaShas, Beis HaOtzar) — wikidata.org
  "יהודה לייב קרינסקי": 1920,
  "עזרא בן רבי אליהו הכהן טראב מסלתון": 1920, // Ezra HaKohen Tarab Maslaton of Damascus — hamichlol.org.il
  "ענגיל, יוסף בן יהודה": 1920, // Yosef Engel — hamichlol.org.il — medium
  "רבינוביץ, מרדכי יצחק איזיק": 1920, // Mordechai Yitzchak Isaac Rabinowitz, Lithuanian rabbi and preacher — hamichlol.org.il — medium
  "אברהם יצחק שפרלינג": 1921,
  "דוד צבי הופמן": 1921,
  "פראגי אלוש": 1921, // Fraji Alush (Allouche), rabbi of Gabes, Tunisia — hamichlol.org.il — medium
  "גלזנר, משה שמואל בן אברהם": 1924, // Moshe Shmuel Glasner (Dor Revi'i) — hebrewbooks.org
  "דוד פיפאנו": 1924, // David Pipano — hamichlol.org.il
  "משה שמואל גלזנר": 1924,
  "זאב וואלף רבינוביץ": 1925,
  "אליעזר דון יחיא": 1926, // Eliezer Don Yechia of Lutsin — hamichlol.org.il
  "ירוחם מאיר ליינר": 1926,
  "רבי מאיר שמחה הכהן מדווינסק": 1926,
  "שמואל בורנשטיין": 1926, // Shmuel Bornstein of Sochatchov — hamichlol.org.il
  "נתן צבי פינקל": 1927,
  "מאיר דן רפאל פלוצקי": 1928,
  "רפאל אהרן בן שמעון": 1928,
  "רפאל באהרן בן שמעון": 1928, // Refael Aharon ben Shimon — nli.org.il
  "אליהו ילוז": 1929, // Eliyahu Yeloz — hamichlol.org.il
  "בצלאל זאב שפרן": 1929, // Betzalel Zev Shafran — hamichlol.org.il — medium
  "מנחם קרקובסקי": 1929,
  "מרדכי יוסף אלעזר ליינר": 1929,
  "שלמה בן משה אבן דנאן": 1929, // Shlomo ibn Danan, chief rabbi of Fez, author of Bikesh Shlomo — hamichlol.org.il — medium
  "יודלביץ, אברהם אהרן בן בנימין בונם": 1930, // Avraham Aharon Yudelovitz — hamichlol.org.il
  "ישעיהו זילברשטיין": 1930,
  "מנחם מאנדל קרנגל": 1930,
  "שלמה אליעזר אלפנדרי": 1930, // Shlomo Eliezer Alfandari (the Saba Kadisha) — hamichlol.org.il
  "משה סוקולובסקי": 1931, // Moshe Sokolovsky (author of Imrei Moshe) — hamichlol.org.il
  "אליהו פוסק": 1932, // Eliyahu Posek — hamichlol.org.il
  "אליהו קלצקין": 1932, // Eliyahu Klatzkin, rabbi of Lublin — wikidata.org
  "ליבשיץ, יחזקאל בן הילל אריה ליב": 1932, // Yechezkel Libshitz ben Hillel Aryeh Leib, rabbi of Kalisz (author of HaMidrash VeHaMaaseh) — hamichlol.org.il
  "קלאצקין, אליהו בן נפתלי הירץ": 1932, // Eliyahu Klatzkin, rabbi of Lublin — hebrewbooks.org
  "יהודה מאיר שפירא": 1933, // Yehuda Meir Shapira of Lublin (originator of Daf Yomi) — hamichlol.org.il
  "יחיאל מיכל לייטר": 1933, // Yechiel Michel Leiter, author of Darkei Shalom — hamichlol.org.il
  "יעקב הלוי קופשטיין": 1933, // Yaakov HaLevi Kupshtein — hamichlol.org.il — medium
  "ישראל מאיר הכה": 1933, // Yisrael Meir HaKohen (Kagan), the Chafetz Chaim — hamichlol.org.il
  "משה מרדכי אפשטיין": 1933, // Moshe Mordechai Epstein (rosh yeshiva of Slabodka and Hebron, author of Levush Mordechai) — nli.org.il
  "רבי ישראל מאיר הכהן": 1933,
  "ירוחם פישל פרלא": 1934,
  "מנשה איכנשטין": 1934, // Menashe Eichenstein (Admor of Zidichov) — hamichlol.org.il — medium
  "אברהם יצחק הכהן קוק": 1935, // Abraham Isaac HaKohen Kook (Rav Kook) — hamichlol.org.il
  "חנוך צבי הכהן לוין": 1935,
  "יהודה יודל רוזנברג": 1935,
  "יצחק אייזיק אפשטיין": 1935,
  "רפאל אנקאווא": 1935, // Raphael Encaoua (Ankawa) of Morocco — hamichlol.org.il
  "שמואל אנגל": 1935, // Shmuel Engel (Maharash Engel) of Radomysl and Kashau — hamichlol.org.il
  "יוסף רוזין": 1936,
  "נחום אש": 1936,
  "שטרן, גרשון בן משה": 1936, // Gershon Stern (author of Yalkut HaGershoni) — nli.org.il
  "עזרא אלטשולר": 1938, // Ezra Altshuler (Takanat Ezra) — he.wikipedia.org
  "יעקב חיים סופר": 1939,
  "נחום וידנפלד": 1939, // Nachum Weidenfeld of Dabrowa (author of Chazon Nachum) — hamichlol.org.il — medium
  "סופר, יעקב חיים בן יצחק ברוך אליהו": 1939, // Yaakov Chaim Sofer (Kaf HaChaim) — hamichlol.org.il — medium
  "שמעון יהודה הכהן שקופ": 1939, // Shimon Yehuda HaKohen Shkop — hamichlol.org.il
  "חיים עוזר גרודזינסקי": 1940, // Chaim Ozer Grodzinski — hamichlol.org.il
  "אהרן לוין": 1941, // Aharon Levin of Rzeszow (author of ha-Drash ve-ha-Iyun) — hamichlol.org.il
  "אלחנן בונים וסרמן": 1941, // Elchanan Bunim Wasserman (Kovetz Shiurim) — hamichlol.org.il
  "דוד רפפורט": 1941, // David HaKohen Rapoport, rosh mesivta of Ohel Torah Baranovich (author of Mikdash David) — nli.org.il — medium
  "הרב ברוך הלוי אפשטיין": 1941,
  "יחיאל מיכל רבינוביץ": 1941, // Yechiel Michal Rabinowitz (author of Afikei Yam) — wikidata.org — medium
  "אלישוב, אברהם בן משה": 1942, // Avraham Elyashuv ben Moshe (author of Bikkurei Avraham) — hebrewbooks.org — medium
  "וואלקין, אהרן בן יעקב צבי": 1942, // Aharon Walkin ben Yaakov Tzvi (Zekan Aharon, rabbi of Pinsk) — he.wikipedia.org
  "חיים פישל אפשטיין": 1942, // Chaim Fishel Epstein (Teshuvah Shleimah) — he.wikipedia.org
  "יוסף פצנובסקי": 1942,
  "אריה צבי פרומר": 1943, // Aryeh Tzvi Frommer, the Koziglover Rav — hamichlol.org.il
  "קלונימוס קלמן שפירא": 1943,
  "יוסף קאנוויץ": 1944, // Yosef Konvitz (Konowitz) — hamichlol.org.il
  "מייזלש, דוד דוב בן אהרן אריה יהודה יעקב": 1944, // David Dov Meislish, last rabbi of Ujhel — hamichlol.org.il — medium
  "ישכר שלמה טייכטאל": 1945, // Yissachar Shlomo Teichtal (author of Em ha-Banim Semechah) — hamichlol.org.il
  "יוסף פרבר": 1946,
  "סקלי, דוד בן משה הכהן": 1948, // David HaKohen Skali, av beit din of Oran — hebrewbooks.org
  "כלפון משה הכהן": 1950, // Kalfon Moshe HaKohen of Djerba (Brit Kehuna) — hamichlol.org.il
  "אברהם ישעיהו קרליץ": 1953, // Avraham Yeshaya Karelitz, the Chazon Ish — hebrewbooks.org
  "איסר זלמן מלצר": 1953,
  "בנגיס, זליג ראובן בן צבי הירש": 1953, // Zelig Reuven Bengis ben Tzvi Hirsch (Lifligot Reuven) — he.wikipedia.org
  "שמואל יצחק הילמן": 1953, // Shmuel Yitzchak Hillman (author of Or HaYashar; av beis din of London, later Jerusalem) — hamichlol.org.il
  "יהודה לייב הלוי אשלג": 1954,
  "ירוחם ליינער": 1954,
  "יהודה דוד אייזנשטיין": 1956,
  "יצחק אייזיק קראסילשציקוב": 1965, // Yitzchak Isaac Krasilshchikov (author of Tevunah on the Jerusalem Talmud) — hamichlol.org.il
  "ירוחם אשר ורהפטיג": 1965, // Yerucham Asher Warhaftig — hamichlol.org.il
  "פנחס אפשטיין": 1969, // Pinchas Epstein, Av Beit Din of the Edah HaChareidis — hamichlol.org.il — medium
  "משה שמואל הורוויץ": 1972,
  "זאב וואלף לייטער": 1974,
  "יואל טייטלבוים, האדמור מסאטמר": 1979, // Yoel Teitelbaum, the Satmar Rebbe — hamichlol.org.il
  "משה פיינשטיין": 1986, // Moshe Feinstein — hamichlol.org.il
  "יעקב יצחק רודרמן": 1987,
  "רב יוסף דוב הלוי סולובייצק": 1993,
  "מנחם מנדל שניאורסון": 1994,
  "משה קטן": 1995,
  "בנימין דוד רבינוביץ": 2002,
  "יצחק יוסף זילבר זל": 2004, // Yitzchak Yosef Zilber — hamichlol.org.il
  "משה דוד טנדלר": 2021,
}
