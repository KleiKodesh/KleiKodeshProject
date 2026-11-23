import type { DailyLearning } from './hebrewCalendarLearning'

export interface CalendarZmanim {
  chatzotNight: string | null
  alot: string | null
  misheyakir: string | null
  sunrise: string | null
  sofShmaGra: string | null
  sofShmaMga: string | null
  sofTfillaGra: string | null
  sofTfillaMga: string | null
  chatzot: string | null
  minchaGedola: string | null
  minchaKetana: string | null
  plag: string | null
  sunset: string | null
  tzeit: string | null
}

export interface CalendarDay {
  date: Date
  dayOfWeek: number // 0=Sun … 6=Sat
  gregDay: number
  hebGem: string // e.g. "כה"
  hebDayName: string // e.g. "שבת"
  isToday: boolean
  isShabbat: boolean
  isFriday: boolean
  // events
  holidays: string[]
  parasha: string | null
  candleLighting: string | null
  havdalah: string | null
  omer: string | null
  chanukahCandles: string | null
  shabbatMevarchim: string | null
  molad: string | null
  yomKippurKatan: string | null
  fastStart: string | null
  fastEnd: string | null
  // learning
  learning: DailyLearning
  // zmanim
  zmanim: CalendarZmanim
}

export interface CalendarWeek {
  hebrewLabel: string
  gregLabel: string
  days: CalendarDay[]
}

/** Stable ASCII id of a city (camelCase of its English name). */
export type CityKey = string

export interface City {
  /** Stable ASCII id: keys the city's i18n name. */
  key: CityKey
  name: string
  lat: number
  lng: number
  elevation: number
  tzid: string
}

/**
 * The selectable city list (GeoNames, CC BY 4.0). Kept here (pure data, no @hebcal imports) so
 * consumers that only need the coordinates — e.g. the home bar's deferred
 * zmanim loader — can import it without dragging in the hebcal engine.
 */
export const CITIES: City[] = [
  { name: 'ירושלים', key: 'jerusalem', lat: 31.76904, lng: 35.21633, elevation: 786, tzid: 'Asia/Jerusalem' },
  { name: 'תל אביב-יפו', key: 'telAviv', lat: 32.08088, lng: 34.78057, elevation: 15, tzid: 'Asia/Jerusalem' },
  { name: 'חיפה', key: 'haifa', lat: 32.81303, lng: 34.99928, elevation: 101, tzid: 'Asia/Jerusalem' },
  { name: 'באר שבע', key: 'beerSheva', lat: 31.25181, lng: 34.7913, elevation: 285, tzid: 'Asia/Jerusalem' },
  { name: 'אשדוד', key: 'ashdod', lat: 31.79213, lng: 34.64966, elevation: 27, tzid: 'Asia/Jerusalem' },
  { name: 'נתניה', key: 'netanya', lat: 32.33294, lng: 34.85917, elevation: 38, tzid: 'Asia/Jerusalem' },
  { name: 'פתח תקווה', key: 'petachTikva', lat: 32.0888, lng: 34.88666, elevation: 60, tzid: 'Asia/Jerusalem' },
  { name: 'ראשון לציון', key: 'rishonLezion', lat: 31.97102, lng: 34.78939, elevation: 56, tzid: 'Asia/Jerusalem' },
  { name: 'בני ברק', key: 'bneiBrak', lat: 32.08074, lng: 34.8338, elevation: 51, tzid: 'Asia/Jerusalem' },
  { name: 'רמת גן', key: 'ramatGan', lat: 32.08227, lng: 34.81065, elevation: 57, tzid: 'Asia/Jerusalem' },
  { name: 'הרצליה', key: 'herzliya', lat: 32.16627, lng: 34.82536, elevation: 28, tzid: 'Asia/Jerusalem' },
  { name: 'רחובות', key: 'rechovot', lat: 31.89415, lng: 34.8095, elevation: 46, tzid: 'Asia/Jerusalem' },
  { name: 'מודיעין', key: 'modiin', lat: 31.89385, lng: 35.01504, elevation: 276, tzid: 'Asia/Jerusalem' },
  { name: 'אילת', key: 'eilat', lat: 29.55805, lng: 34.94821, elevation: 63, tzid: 'Asia/Jerusalem' },
  { name: 'צפת', key: 'tzfat', lat: 32.96465, lng: 35.496, elevation: 779, tzid: 'Asia/Jerusalem' },
  { name: 'טבריה', key: 'tiberias', lat: 32.79396, lng: 35.53152, elevation: -39, tzid: 'Asia/Jerusalem' },
  { name: 'ניו יורק', key: 'newYork', lat: 40.71427, lng: -74.00597, elevation: 10, tzid: 'America/New_York' },
  { name: 'לונדון', key: 'london', lat: 51.50853, lng: -0.12574, elevation: 25, tzid: 'Europe/London' },
  { name: 'אנטוורפן', key: 'antwerp', lat: 51.22047, lng: 4.40026, elevation: 13, tzid: 'Europe/Brussels' },
  { name: 'מונטריאול', key: 'montreal', lat: 45.50884, lng: -73.58781, elevation: 216, tzid: 'America/Toronto' },
  { name: 'אומן', key: 'uman', lat: 48.7501, lng: 30.21944, elevation: 197, tzid: 'Europe/Kyiv' },
  { name: 'אוסטין', key: 'austinTexas', lat: 30.26715, lng: -97.74306, elevation: 149, tzid: 'America/Chicago' },
  { name: 'אופקים', key: 'ofakim', lat: 31.3141, lng: 34.62025, elevation: 137, tzid: 'Asia/Jerusalem' },
  { name: 'אור הגנוז', key: 'orHaganuz', lat: 33.00508, lng: 35.44651, elevation: 728, tzid: 'Asia/Jerusalem' },
  { name: 'איתמר', key: 'itamar', lat: 32.17474, lng: 35.30941, elevation: 621, tzid: 'Asia/Hebron' },
  { name: 'אלון מורה', key: 'elonMorehShechem', lat: 32.23419, lng: 35.33298, elevation: 748, tzid: 'Asia/Hebron' },
  { name: 'אלון שבות', key: 'alonShvut', lat: 31.65483, lng: 35.12587, elevation: 960, tzid: 'Asia/Jerusalem' },
  { name: 'אמירים', key: 'amirim', lat: 32.93792, lng: 35.45187, elevation: 557, tzid: 'Asia/Jerusalem' },
  { name: 'אמסטרדם', key: 'amsterdam', lat: 52.37403, lng: 4.88969, elevation: 13, tzid: 'Europe/Amsterdam' },
  { name: 'אפרת', key: 'efrat', lat: 31.65305, lng: 35.15013, elevation: 942, tzid: 'Asia/Jerusalem' },
  { name: 'אריאל', key: 'ariel', lat: 32.1065, lng: 35.18449, elevation: 560, tzid: 'Asia/Jerusalem' },
  { name: 'אשקלון', key: 'ashkelon', lat: 31.66926, lng: 34.57149, elevation: 42, tzid: 'Asia/Jerusalem' },
  { name: 'בולטימור', key: 'baltimore', lat: 39.29038, lng: -76.61219, elevation: 10, tzid: 'America/New_York' },
  { name: 'באר יעקב', key: 'beerYaakov', lat: 31.94433, lng: 34.83902, elevation: 92, tzid: 'Asia/Jerusalem' },
  { name: 'בואנוס איירס', key: 'buenosAires', lat: -34.61315, lng: -58.37723, elevation: 31, tzid: 'America/Argentina/Buenos_Aires' },
  { name: 'Beit El', key: 'beitEl', lat: 31.94153, lng: 35.22241, elevation: 866, tzid: 'Asia/Hebron' },
  { name: 'בית זית', key: 'beitZayit', lat: 31.78233, lng: 35.16161, elevation: 604, tzid: 'Asia/Jerusalem' },
  { name: 'Bet H̱ilqiyya', key: 'beitHilkia', lat: 31.79281, lng: 34.81048, elevation: 73, tzid: 'Asia/Jerusalem' },
  { name: 'בית מאיר', key: 'beitMeir', lat: 31.79393, lng: 35.03689, elevation: 583, tzid: 'Asia/Jerusalem' },
  { name: 'בית שאן', key: 'beitShean', lat: 32.49658, lng: 35.4965, elevation: -121, tzid: 'Asia/Jerusalem' },
  { name: 'בית שמש', key: 'beitShemesh', lat: 31.73072, lng: 34.99293, elevation: 282, tzid: 'Asia/Jerusalem' },
  { name: 'ביתר עילית', key: 'beitarIllit', lat: 31.7, lng: 35.11667, elevation: 675, tzid: 'Asia/Jerusalem' },
  { name: 'ברוקלין', key: 'brooklyn', lat: 40.6501, lng: -73.94958, elevation: 15, tzid: 'America/New_York' },
  { name: 'בת ים', key: 'batYam', lat: 32.01798, lng: 34.75003, elevation: 52, tzid: 'Asia/Jerusalem' },
  { name: 'בת עין', key: 'batAyin', lat: 31.65756, lng: 35.10345, elevation: 857, tzid: 'Asia/Jerusalem' },
  { name: 'גבע בנימין', key: 'gevaBinyaminAdam', lat: 31.85031, lng: 35.27405, elevation: 644, tzid: 'Asia/Jerusalem' },
  { name: 'בית רבן', key: 'givatWashington', lat: 31.81677, lng: 34.72937, elevation: 62, tzid: 'Asia/Jerusalem' },
  { name: 'גבעת זאב', key: 'givatZeev', lat: 31.86145, lng: 35.16861, elevation: 770, tzid: 'Asia/Hebron' },
  { name: 'גדרה', key: 'gedera', lat: 31.81456, lng: 34.77998, elevation: 64, tzid: 'Asia/Jerusalem' },
  { name: 'גייטסהד', key: 'gateshead', lat: 54.96209, lng: -1.60168, elevation: 53, tzid: 'Europe/London' },
  { name: 'גמזו', key: 'gimzo', lat: 31.92776, lng: 34.94045, elevation: 92, tzid: 'Asia/Jerusalem' },
  { name: 'גן יבנה', key: 'ganYavne', lat: 31.78737, lng: 34.70659, elevation: 52, tzid: 'Asia/Jerusalem' },
  { name: 'Great Neck', key: 'greatNeck', lat: 40.80066, lng: -73.72846, elevation: 33, tzid: 'America/New_York' },
  { name: 'דאלאס', key: 'dallas', lat: 32.78306, lng: -96.80667, elevation: 128, tzid: 'America/Chicago' },
  { name: 'דימונה', key: 'dimona', lat: 31.07079, lng: 35.03269, elevation: 551, tzid: 'Asia/Jerusalem' },
  { name: 'The Bronx', key: 'theBronx', lat: 40.84985, lng: -73.86641, elevation: 23, tzid: 'America/New_York' },
  { name: 'Woodland Hills', key: 'woodlandHills', lat: 34.16834, lng: -118.60592, elevation: 273, tzid: 'America/Los_Angeles' },
  { name: 'ווטרברי', key: 'waterburyConnecticut', lat: 41.55815, lng: -73.0515, elevation: 82, tzid: 'America/New_York' },
  { name: 'Washington, D.C.', key: 'washingtonDC', lat: 38.89511, lng: -77.03637, elevation: 7, tzid: 'America/New_York' },
  { name: 'זכרון יעקב', key: 'zikhronYaakov', lat: 32.56883, lng: 34.95427, elevation: 190, tzid: 'Asia/Jerusalem' },
  { name: 'חדרה', key: 'hadera', lat: 32.44192, lng: 34.9039, elevation: 13, tzid: 'Asia/Jerusalem' },
  { name: 'חולון', key: 'holon', lat: 32.0122, lng: 34.77826, elevation: 24, tzid: 'Asia/Jerusalem' },
  { name: 'חצור הגלילית', key: 'hatzorHaglilit', lat: 32.98234, lng: 35.54494, elevation: 379, tzid: 'Asia/Jerusalem' },
  { name: 'חריש', key: 'harish', lat: 32.46182, lng: 35.04555, elevation: 140, tzid: 'Asia/Jerusalem' },
  { name: 'טורונטו', key: 'torontoCanada', lat: 43.65333, lng: -79.38389, elevation: 92, tzid: 'America/Toronto' },
  { name: 'טירת כרמל', key: 'tiratCarmel', lat: 32.76025, lng: 34.96874, elevation: 34, tzid: 'Asia/Jerusalem' },
  { name: 'יבנאל', key: 'yavneel', lat: 32.70598, lng: 35.50409, elevation: -18, tzid: 'Asia/Jerusalem' },
  { name: 'יוהנסבורג', key: 'johannesburg', lat: -26.20227, lng: 28.04363, elevation: 1767, tzid: 'Africa/Johannesburg' },
  { name: 'יוסטון', key: 'houston', lat: 29.76328, lng: -95.36327, elevation: 12, tzid: 'America/Chicago' },
  { name: 'יסודות', key: 'yesodot', lat: 31.81479, lng: 34.86559, elevation: 116, tzid: 'Asia/Jerusalem' },
  { name: 'יצהר', key: 'yitzhar', lat: 32.16744, lng: 35.23875, elevation: 790, tzid: 'Asia/Jerusalem' },
  { name: 'ירוחם', key: 'yeruham', lat: 30.98822, lng: 34.93176, elevation: 499, tzid: 'Asia/Jerusalem' },
  { name: 'כוכב השחר', key: 'kochavHashachar', lat: 31.96028, lng: 35.34923, elevation: 584, tzid: 'Asia/Jerusalem' },
  { name: 'כפר סבא', key: 'kfarSaba', lat: 32.175, lng: 34.90694, elevation: 57, tzid: 'Asia/Jerusalem' },
  { name: 'כפר שמאי', key: 'kfarShamai', lat: 32.9573, lng: 35.45711, elevation: 618, tzid: 'Asia/Jerusalem' },
  { name: 'כרמיאל', key: 'karmiel', lat: 32.90951, lng: 35.29768, elevation: 314, tzid: 'Asia/Jerusalem' },
  { name: 'לאס וגאס', key: 'lasVegas', lat: 36.17497, lng: -115.13722, elevation: 610, tzid: 'America/Los_Angeles' },
  { name: 'Aẖisamakh', key: 'lodAhisamakh', lat: 31.93545, lng: 34.90844, elevation: 96, tzid: 'Asia/Jerusalem' },
  { name: 'לוס אנגלס', key: 'losAngeles', lat: 34.05223, lng: -118.24368, elevation: 89, tzid: 'America/Los_Angeles' },
  { name: 'לייקווד', key: 'lakewood', lat: 40.09789, lng: -74.21764, elevation: 23, tzid: 'America/New_York' },
  { name: 'מונסי', key: 'monsey', lat: 41.11121, lng: -74.06848, elevation: 167, tzid: 'America/New_York' },
  { name: 'מבוא חורון', key: 'mevoHoron', lat: 31.84923, lng: 35.0359, elevation: 258, tzid: 'Asia/Jerusalem' },
  { name: 'מבשרת ציון', key: 'mevaseretZion', lat: 31.80186, lng: 35.15072, elevation: 711, tzid: 'Asia/Jerusalem' },
  { name: 'מגדל', key: 'migdal', lat: 32.83931, lng: 35.50206, elevation: -157, tzid: 'Asia/Jerusalem' },
  { name: 'מגדל העמק', key: 'migdalHaemek', lat: 32.67788, lng: 35.24158, elevation: 231, tzid: 'Asia/Jerusalem' },
  { name: 'מדריד', key: 'madrid', lat: 40.4165, lng: -3.70256, elevation: 665, tzid: 'Europe/Madrid' },
  { name: 'מיאמי', key: 'miami', lat: 25.77427, lng: -80.19366, elevation: 2, tzid: 'America/New_York' },
  { name: 'אספר', key: 'metzad', lat: 31.58661, lng: 35.18865, elevation: 944, tzid: 'Asia/Jerusalem' },
  { name: 'מירון', key: 'meron', lat: 32.98635, lng: 35.43986, elevation: 653, tzid: 'Asia/Jerusalem' },
  { name: 'מלבורן', key: 'melbourne', lat: -37.814, lng: 144.96332, elevation: 25, tzid: 'Australia/Melbourne' },
  { name: "מנצ'סטר", key: 'manchester', lat: 53.48095, lng: -2.23743, elevation: 51, tzid: 'Europe/London' },
  { name: 'מעלה אדומים', key: 'maaleAdumim', lat: 31.7774, lng: 35.29875, elevation: 498, tzid: 'Asia/Jerusalem' },
  { name: 'מעלה עמוס', key: 'maaleAmos', lat: 31.59608, lng: 35.22981, elevation: 716, tzid: 'Asia/Jerusalem' },
  { name: 'מעלות תרשיחא', key: 'maalot', lat: 33.01667, lng: 35.26667, elevation: 484, tzid: 'Asia/Jerusalem' },
  { name: 'מצפה יריחו', key: 'mitzpeYericho', lat: 31.81672, lng: 35.39405, elevation: 178, tzid: 'Asia/Jerusalem' },
  { name: 'מצפה רמון', key: 'mitzpeRamon', lat: 30.60944, lng: 34.80111, elevation: 858, tzid: 'Asia/Jerusalem' },
  { name: 'Mexico - Cuernavaca', key: 'mexicoCuernavaca', lat: 18.9261, lng: -99.23075, elevation: 1540, tzid: 'America/Mexico_City' },
  { name: 'מרסיי', key: 'marseille', lat: 43.29695, lng: 5.38107, elevation: 28, tzid: 'Europe/Paris' },
  { name: '‏נהריה', key: 'nahariya', lat: 33.00892, lng: 35.09814, elevation: 15, tzid: 'Asia/Jerusalem' },
  { name: 'נוף איילון', key: 'nofAyalonShaalvim', lat: 31.87111, lng: 34.99081, elevation: 195, tzid: 'Asia/Jerusalem' },
  { name: 'נוף הגליל', key: 'nofHagalil', lat: 32.70816, lng: 35.32469, elevation: 506, tzid: 'Asia/Jerusalem' },
  { name: 'נחליאל', key: 'nahliel', lat: 31.97415, lng: 35.13994, elevation: 590, tzid: 'Asia/Jerusalem' },
  { name: 'ניר עציון', key: 'nirEtzion', lat: 32.69854, lng: 34.99128, elevation: 117, tzid: 'Asia/Jerusalem' },
  { name: 'נתיבות', key: 'netivot', lat: 31.42305, lng: 34.58911, elevation: 151, tzid: 'Asia/Jerusalem' },
  { name: 'סאו פאולו', key: 'saoPaulo', lat: -23.5475, lng: -46.63611, elevation: 769, tzid: 'America/Sao_Paulo' },
  { name: 'Staten Island', key: 'statenIsland', lat: 40.56233, lng: -74.13986, elevation: 21, tzid: 'America/New_York' },
  { name: 'סן אנטוניו', key: 'sanAntonio', lat: 29.42412, lng: -98.49363, elevation: 198, tzid: 'America/Chicago' },
  { name: 'סן לואי (צרפת)', key: 'stLouis', lat: 47.59206, lng: 7.55923, elevation: 248, tzid: 'Europe/Paris' },
  { name: 'סאסא', key: 'sasa', lat: 33.02704, lng: 35.39369, elevation: 870, tzid: 'Asia/Jerusalem' },
  { name: 'עזה', key: 'gaza', lat: 31.50161, lng: 34.46672, elevation: 38, tzid: 'Asia/Gaza' },
  { name: 'עכו', key: 'acre', lat: 32.92814, lng: 35.07647, elevation: 8, tzid: 'Asia/Jerusalem' },
  { name: 'עלי', key: 'eli', lat: 32.06999, lng: 35.26371, elevation: 700, tzid: 'Asia/Jerusalem' },
  { name: 'עמנואל', key: 'emmanuel', lat: 32.16075, lng: 35.13611, elevation: 404, tzid: 'Asia/Jerusalem' },
  { name: 'עפולה', key: 'afula', lat: 32.60907, lng: 35.2892, elevation: 62, tzid: 'Asia/Jerusalem' },
  { name: 'אקס-לה-בן', key: 'aixLesBains', lat: 45.69173, lng: 5.90863, elevation: 246, tzid: 'Europe/Paris' },
  { name: 'ערד', key: 'arad', lat: 31.25882, lng: 35.21282, elevation: 613, tzid: 'Asia/Jerusalem' },
  { name: 'פאלסבורג', key: 'fallsburg', lat: 41.73204, lng: -74.60127, elevation: 368, tzid: 'America/New_York' },
  { name: 'פינסק', key: 'pinskBelarus', lat: 52.12153, lng: 26.06726, elevation: 148, tzid: 'Europe/Minsk' },
  { name: 'פנמה סיטי', key: 'panamaCity', lat: 8.9936, lng: -79.51973, elevation: 17, tzid: 'America/Panama' },
  { name: 'Passaic, New Jersey', key: 'passaicNewJersey', lat: 40.85677, lng: -74.12848, elevation: 34, tzid: 'America/New_York' },
  { name: 'פריז', key: 'paris', lat: 48.85341, lng: 2.3488, elevation: 42, tzid: 'Europe/Paris' },
  { name: 'ציריך', key: 'zurich', lat: 47.36667, lng: 8.55, elevation: 429, tzid: 'Europe/Zurich' },
  { name: 'Queens', key: 'queens', lat: 40.68149, lng: -73.83652, elevation: 12, tzid: 'America/New_York' },
  { name: 'קוממיות', key: 'komemiyut', lat: 31.66259, lng: 34.72976, elevation: 80, tzid: 'Asia/Jerusalem' },
  { name: 'קיסריה', key: 'caesarea', lat: 32.51888, lng: 34.90459, elevation: 16, tzid: 'Asia/Jerusalem' },
  { name: 'קליבלנד', key: 'cleveland', lat: 41.4995, lng: -81.69541, elevation: 199, tzid: 'America/New_York' },
  { name: 'קריית ארבע', key: 'kiryatArba', lat: 31.52838, lng: 35.11897, elevation: 956, tzid: 'Asia/Hebron' },
  { name: 'קריית אתא', key: 'kiryatAta', lat: 32.81149, lng: 35.11323, elevation: 46, tzid: 'Asia/Jerusalem' },
  { name: 'קריית גת', key: 'kiryatGat', lat: 31.60998, lng: 34.76422, elevation: 133, tzid: 'Asia/Jerusalem' },
  { name: 'קרית טבעון', key: 'kiryatTivon', lat: 32.71217, lng: 35.12505, elevation: 122, tzid: 'Asia/Jerusalem' },
  { name: 'קריית יואל', key: 'kiryasJoelNewYork', lat: 41.34204, lng: -74.16792, elevation: 209, tzid: 'America/New_York' },
  { name: 'קרית יערים', key: 'kiryatYearimTelzStone', lat: 31.803, lng: 35.102, elevation: 754, tzid: 'Asia/Jerusalem' },
  { name: 'קריית מלאכי', key: 'kiryatMalakhi', lat: 31.72888, lng: 34.74632, elevation: 68, tzid: 'Asia/Jerusalem' },
  { name: 'קריית שמונה', key: 'kiryatShmona', lat: 33.21204, lng: 35.57044, elevation: 122, tzid: 'Asia/Jerusalem' },
  { name: 'ראש העין', key: 'roshHaayin', lat: 32.09547, lng: 34.95444, elevation: 30, tzid: 'Asia/Jerusalem' },
  { name: 'רבבה', key: 'revava', lat: 32.11926, lng: 35.12896, elevation: 420, tzid: 'Asia/Jerusalem' },
  { name: 'ריוורדייל', key: 'riverdale', lat: 40.90056, lng: -73.90639, elevation: 55, tzid: 'America/New_York' },
  { name: 'רכסים', key: 'rekhasim', lat: 32.74958, lng: 35.10057, elevation: 105, tzid: 'Asia/Jerusalem' },
  { name: 'רמלה', key: 'ramla', lat: 31.92624, lng: 34.8635, elevation: 100, tzid: 'Asia/Jerusalem' },
  { name: 'רמת השרון', key: 'ramatHasharon', lat: 32.1459, lng: 34.83859, elevation: 68, tzid: 'Asia/Jerusalem' },
  { name: 'רמת מגשימים', key: 'ramatMagshimim', lat: 32.84463, lng: 35.80679, elevation: 428, tzid: 'Asia/Jerusalem' },
  { name: 'רנסי', key: 'leRaincy', lat: 48.89916, lng: 2.52298, elevation: 86, tzid: 'Europe/Paris' },
  { name: 'רעננה', key: 'raanana', lat: 32.1836, lng: 34.87386, elevation: 49, tzid: 'Asia/Jerusalem' },
  { name: 'שדרות', key: 'sderot', lat: 31.525, lng: 34.59693, elevation: 90, tzid: 'Asia/Jerusalem' },
  { name: 'שטרסבורג', key: 'strasbourg', lat: 48.58392, lng: 7.74553, elevation: 147, tzid: 'Europe/Paris' },
  { name: 'שיקגו', key: 'chicago', lat: 41.85003, lng: -87.65005, elevation: 179, tzid: 'America/Chicago' },
  { name: 'שלומי', key: 'shlomi', lat: 33.07186, lng: 35.14527, elevation: 82, tzid: 'Asia/Jerusalem' },
  { name: 'Tifraẖ', key: 'tifrah', lat: 31.32657, lng: 34.67689, elevation: 156, tzid: 'Asia/Jerusalem' },
]
