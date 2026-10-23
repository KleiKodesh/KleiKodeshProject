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

export type CityKey =
  | 'jerusalem' | 'telAviv' | 'haifa' | 'beerSheva' | 'ashdod' | 'netanya' | 'petachTikva' | 'rishonLezion'
  | 'bneiBrak' | 'ramatGan' | 'herzliya' | 'rechovot' | 'modiin' | 'eilat' | 'tzfat' | 'tiberias'
  | 'newYork' | 'london' | 'antwerp' | 'montreal'
  /** The other Itim Labina locations, by their location id. */
  | `itimLabina${number}`

export interface City {
  /** Stable ASCII id: keys the per-city zmanim data (climate, Itim Labina luach). */
  key: CityKey
  name: string
  lat: number
  lng: number
  elevation: number
  tzid: string
}

/**
 * The selectable city list. Kept here (pure data, no @hebcal imports) so
 * consumers that only need the coordinates — e.g. the home bar's deferred
 * zmanim loader — can import it without dragging in the hebcal engine.
 */
export const CITIES: City[] = [
  { name: 'ירושלים', key: 'jerusalem', lat: 31.7884, lng: 35.2184, elevation: 798, tzid: 'Asia/Jerusalem' },
  { name: 'תל אביב', key: 'telAviv', lat: 32.0855, lng: 34.782, elevation: 19, tzid: 'Asia/Jerusalem' },
  { name: 'חיפה', key: 'haifa', lat: 32.806, lng: 35.0023, elevation: 81, tzid: 'Asia/Jerusalem' },
  { name: 'באר שבע', key: 'beerSheva', lat: 31.252, lng: 34.778, elevation: 268, tzid: 'Asia/Jerusalem' },
  { name: 'אשדוד', key: 'ashdod', lat: 31.797, lng: 34.658, elevation: 19, tzid: 'Asia/Jerusalem' },
  { name: 'נתניה', key: 'netanya', lat: 32.3433, lng: 34.856, elevation: 23, tzid: 'Asia/Jerusalem' },
  { name: 'פתח תקווה', key: 'petachTikva', lat: 32.08, lng: 34.87, elevation: 59, tzid: 'Asia/Jerusalem' },
  { name: 'ראשון לציון', key: 'rishonLezion', lat: 31.975, lng: 34.793, elevation: 44, tzid: 'Asia/Jerusalem' },
  { name: 'בני ברק', key: 'bneiBrak', lat: 32.0858, lng: 34.8349, elevation: 45, tzid: 'Asia/Jerusalem' },
  { name: 'רמת גן', key: 'ramatGan', lat: 32.061, lng: 34.833, elevation: 38, tzid: 'Asia/Jerusalem' },
  { name: 'הרצליה', key: 'herzliya', lat: 32.16, lng: 34.84, elevation: 52, tzid: 'Asia/Jerusalem' },
  { name: 'רחובות', key: 'rechovot', lat: 31.894, lng: 34.816, elevation: 53, tzid: 'Asia/Jerusalem' },
  { name: 'מודיעין', key: 'modiin', lat: 31.8969, lng: 35.0095, elevation: 300, tzid: 'Asia/Jerusalem' },
  { name: 'אילת', key: 'eilat', lat: 29.557, lng: 34.95, elevation: 47, tzid: 'Asia/Jerusalem' },
  { name: 'צפת', key: 'tzfat', lat: 32.966, lng: 35.487, elevation: 638, tzid: 'Asia/Jerusalem' },
  { name: 'טבריה', key: 'tiberias', lat: 32.782, lng: 35.516, elevation: 223, tzid: 'Asia/Jerusalem' },
  { name: 'ניו יורק', key: 'newYork', lat: 40.7128, lng: -74.006, elevation: 10, tzid: 'America/New_York' },
  { name: 'לונדון', key: 'london', lat: 51.5074, lng: -0.1278, elevation: 10, tzid: 'Europe/London' },
  { name: 'אנטוורפן', key: 'antwerp', lat: 51.2194, lng: 4.4025, elevation: 10, tzid: 'Europe/Brussels' },
  { name: 'מונטריאול', key: 'montreal', lat: 45.5017, lng: -73.5673, elevation: 30, tzid: 'America/Toronto' },
  // Every other Itim Labina location (GET https://itimlabina.co.il/api/boot/data, locationsList): its default
  // coordinates, and its default elevation or else the one their site looks up for those coordinates.
  { name: 'אומן', key: 'itimLabina123', lat: 48.7471, lng: 30.2342, elevation: 192, tzid: 'Europe/Kyiv' },
  { name: 'אוסטין טקסס', key: 'itimLabina206', lat: 30.4255, lng: -97.8605, elevation: 310, tzid: 'America/Chicago' },
  { name: 'אופקים', key: 'itimLabina41', lat: 31.307, lng: 34.625, elevation: 151, tzid: 'Asia/Jerusalem' },
  { name: 'אור הגנוז', key: 'itimLabina196', lat: 33.004, lng: 35.4475, elevation: 722, tzid: 'Asia/Jerusalem' },
  { name: 'אחיה', key: 'itimLabina151', lat: 32.0605, lng: 35.328, elevation: 851, tzid: 'Asia/Jerusalem' },
  { name: 'איתמר', key: 'itimLabina92', lat: 32.1708, lng: 35.3143, elevation: 639, tzid: 'Asia/Jerusalem' },
  { name: 'אלון מורה - שכם', key: 'itimLabina172', lat: 32.2335, lng: 35.34, elevation: 716, tzid: 'Asia/Jerusalem' },
  { name: 'אלון שבות', key: 'itimLabina234', lat: 31.6535, lng: 35.127, elevation: 955, tzid: 'Asia/Jerusalem' },
  { name: 'אלעד 100', key: 'itimLabina164', lat: 32.049, lng: 34.9485, elevation: 102, tzid: 'Asia/Jerusalem' },
  { name: 'אלעד 155', key: 'itimLabina29', lat: 32.0527, lng: 34.965, elevation: 158, tzid: 'Asia/Jerusalem' },
  { name: 'אלעד 80', key: 'itimLabina28', lat: 32.0527, lng: 34.9456, elevation: 78, tzid: 'Asia/Jerusalem' },
  { name: 'אמירים', key: 'itimLabina168', lat: 32.9375, lng: 35.45, elevation: 540, tzid: 'Asia/Jerusalem' },
  { name: 'אמסטרדם', key: 'itimLabina218', lat: 52.332, lng: 4.8615, elevation: 2, tzid: 'Europe/Brussels' },
  { name: 'אפרת', key: 'itimLabina137', lat: 31.6565, lng: 35.1485, elevation: 903, tzid: 'Asia/Jerusalem' },
  { name: 'אריאל', key: 'itimLabina97', lat: 32.1, lng: 35.17, elevation: 495, tzid: 'Asia/Jerusalem' },
  { name: 'ארץ ישראל - שדרת ההר', key: 'itimLabina199', lat: 31.779, lng: 35.226, elevation: 791, tzid: 'Asia/Jerusalem' },
  { name: 'אשקלון', key: 'itimLabina44', lat: 31.666, lng: 34.565, elevation: 70, tzid: 'Asia/Jerusalem' },
  { name: 'באלטימור', key: 'itimLabina145', lat: 39.3815, lng: -76.75, elevation: 162, tzid: 'America/New_York' },
  { name: 'באר יעקב', key: 'itimLabina99', lat: 31.942, lng: 34.836, elevation: 79, tzid: 'Asia/Jerusalem' },
  { name: 'בואנוס איירס', key: 'itimLabina165', lat: -34.6091, lng: -58.4394, elevation: 25, tzid: 'America/Argentina/Buenos_Aires' },
  { name: 'בית אל', key: 'itimLabina167', lat: 31.942, lng: 35.2225, elevation: 869, tzid: 'Asia/Jerusalem' },
  { name: 'בית זית', key: 'itimLabina210', lat: 31.7825, lng: 35.1595, elevation: 617, tzid: 'Asia/Jerusalem' },
  { name: 'בית חלקיה', key: 'itimLabina70', lat: 31.7925, lng: 34.811, elevation: 75, tzid: 'Asia/Jerusalem' },
  { name: 'בית מאיר', key: 'itimLabina118', lat: 31.7965, lng: 35.0345, elevation: 541, tzid: 'Asia/Jerusalem' },
  { name: 'בית שאן', key: 'itimLabina61', lat: 32.492, lng: 35.5, elevation: -118, tzid: 'Asia/Jerusalem' },
  { name: 'בית שמש', key: 'itimLabina48', lat: 31.7365, lng: 34.9885, elevation: 252, tzid: 'Asia/Jerusalem' },
  { name: 'בית שמש - רמה ג', key: 'itimLabina198', lat: 31.704, lng: 34.9825, elevation: 419, tzid: 'Asia/Jerusalem' },
  { name: 'ביתר עילית', key: 'itimLabina26', lat: 31.693, lng: 35.1078, elevation: 671, tzid: 'Asia/Jerusalem' },
  { name: 'ברוקלין', key: 'itimLabina75', lat: 40.626, lng: -73.984, elevation: 16, tzid: 'America/New_York' },
  { name: 'בת ים', key: 'itimLabina190', lat: 32.015, lng: 34.753, elevation: 38, tzid: 'Asia/Jerusalem' },
  { name: 'בת עין', key: 'itimLabina191', lat: 31.6595, lng: 35.0975, elevation: 808, tzid: 'Asia/Jerusalem' },
  { name: 'גבע בנימין אדם', key: 'itimLabina109', lat: 31.8505, lng: 35.273, elevation: 651, tzid: 'Asia/Jerusalem' },
  { name: 'גבעת וושינגטון', key: 'itimLabina142', lat: 31.817, lng: 34.7285, elevation: 58, tzid: 'Asia/Jerusalem' },
  { name: 'גבעת זאב', key: 'itimLabina21', lat: 31.8654, lng: 35.1535, elevation: 600, tzid: 'Asia/Jerusalem' },
  { name: 'גדרה', key: 'itimLabina213', lat: 31.812, lng: 34.7805, elevation: 77, tzid: 'Asia/Jerusalem' },
  { name: 'גייטסהד', key: 'itimLabina201', lat: 54.953, lng: -1.6105, elevation: 67, tzid: 'Europe/London' },
  { name: 'גמזו', key: 'itimLabina189', lat: 31.9275, lng: 34.942, elevation: 116, tzid: 'Asia/Jerusalem' },
  { name: 'גן יבנה', key: 'itimLabina108', lat: 31.78, lng: 34.7, elevation: 33, tzid: 'Asia/Jerusalem' },
  { name: 'גני מודיעין', key: 'itimLabina216', lat: 31.93, lng: 35.0175, elevation: 242, tzid: 'Asia/Jerusalem' },
  { name: 'גרייט נק', key: 'itimLabina146', lat: 40.8005, lng: -73.7285, elevation: 36, tzid: 'America/New_York' },
  { name: 'דאלאס', key: 'itimLabina154', lat: 32.8965, lng: -96.789, elevation: 186, tzid: 'America/Chicago' },
  { name: 'דיל', key: 'itimLabina233', lat: 40.2525, lng: -74.001, elevation: 11, tzid: 'America/New_York' },
  { name: 'דימונה', key: 'itimLabina62', lat: 31.076, lng: 35.035, elevation: 560, tzid: 'Asia/Jerusalem' },
  { name: 'הברונקס', key: 'itimLabina229', lat: 40.8465, lng: -73.8785, elevation: 17, tzid: 'America/New_York' },
  { name: 'וודלנד הילס', key: 'itimLabina215', lat: 34.1725, lng: -118.564, elevation: 261, tzid: 'America/Los_Angeles' },
  { name: 'ווטרברי קונטיקט', key: 'itimLabina200', lat: 41.5915, lng: -73.033, elevation: 212, tzid: 'America/New_York' },
  { name: 'וושינגטון די סי', key: 'itimLabina144', lat: 39.0385, lng: -77.016, elevation: 111, tzid: 'America/New_York' },
  { name: 'זכרון יעקב', key: 'itimLabina51', lat: 32.568, lng: 34.949, elevation: 136, tzid: 'Asia/Jerusalem' },
  { name: 'חדרה', key: 'itimLabina98', lat: 32.432, lng: 34.914, elevation: 30, tzid: 'Asia/Jerusalem' },
  { name: 'חוות שובאל', key: 'itimLabina220', lat: 32.0786, lng: 35.1351, elevation: 540, tzid: 'Asia/Jerusalem' },
  { name: 'חולון', key: 'itimLabina59', lat: 32.01, lng: 34.78, elevation: 33, tzid: 'Asia/Jerusalem' },
  { name: 'חיפה עילית', key: 'itimLabina195', lat: 32.7845, lng: 35.0145, elevation: 213, tzid: 'Asia/Jerusalem' },
  { name: 'חצור הגלילית', key: 'itimLabina194', lat: 32.9825, lng: 35.546, elevation: 358, tzid: 'Asia/Jerusalem' },
  { name: 'חריש', key: 'itimLabina68', lat: 32.462, lng: 35.045, elevation: 109, tzid: 'Asia/Jerusalem' },
  { name: 'חרשה', key: 'itimLabina90', lat: 31.944, lng: 35.149, elevation: 767, tzid: 'Asia/Jerusalem' },
  { name: 'טורנטו קנדה', key: 'itimLabina192', lat: 43.7155, lng: -79.4315, elevation: 178, tzid: 'America/Toronto' },
  { name: 'טירת כרמל', key: 'itimLabina185', lat: 32.7655, lng: 34.9705, elevation: 40, tzid: 'Asia/Jerusalem' },
  { name: 'יבנאל', key: 'itimLabina141', lat: 32.7025, lng: 35.504, elevation: 7, tzid: 'Asia/Jerusalem' },
  { name: 'יוהנסבורג', key: 'itimLabina120', lat: -26.151, lng: 28.0805, elevation: 1632, tzid: 'Africa/Johannesburg' },
  { name: 'יוסטון', key: 'itimLabina153', lat: 29.653, lng: -95.5125, elevation: 22, tzid: 'America/Chicago' },
  { name: 'יסודות', key: 'itimLabina139', lat: 31.816, lng: 34.865, elevation: 111, tzid: 'Asia/Jerusalem' },
  { name: 'יצהר', key: 'itimLabina116', lat: 32.166, lng: 35.24, elevation: 756, tzid: 'Asia/Jerusalem' },
  { name: 'ירוחם', key: 'itimLabina76', lat: 30.99, lng: 34.93, elevation: 493, tzid: 'Asia/Jerusalem' },
  { name: 'ישראל', key: 'itimLabina138', lat: 30.8125, lng: 34.8595, elevation: 307, tzid: 'Asia/Jerusalem' },
  { name: 'כוכב השחר', key: 'itimLabina163', lat: 31.9615, lng: 35.348, elevation: 592, tzid: 'Asia/Jerusalem' },
  { name: 'כל העולם', key: 'itimLabina14', lat: -24.7761, lng: 134.755, elevation: 275, tzid: 'Australia/Darwin' },
  { name: 'כלא 10', key: 'itimLabina236', lat: 32.334, lng: 34.928, elevation: 31, tzid: 'Asia/Jerusalem' },
  { name: 'כלנית חרמון', key: 'itimLabina221', lat: 32.872, lng: 35.446, elevation: 0, tzid: 'Asia/Jerusalem' },
  { name: 'כפר סבא', key: 'itimLabina128', lat: 32.1838, lng: 34.938, elevation: 58, tzid: 'Asia/Jerusalem' },
  { name: 'כפר שמאי', key: 'itimLabina235', lat: 32.961, lng: 35.4565, elevation: 609, tzid: 'Asia/Jerusalem' },
  { name: 'כרם רעים', key: 'itimLabina157', lat: 31.959, lng: 35.1395, elevation: 564, tzid: 'Asia/Jerusalem' },
  { name: 'כרמיאל', key: 'itimLabina71', lat: 32.917, lng: 35.305, elevation: 231, tzid: 'Asia/Jerusalem' },
  { name: 'לאס וגאס', key: 'itimLabina214', lat: 36.1585, lng: -115.252, elevation: 756, tzid: 'America/Los_Angeles' },
  { name: 'לוד אחיסמך', key: 'itimLabina33', lat: 31.949, lng: 34.8885, elevation: 60, tzid: 'Asia/Jerusalem' },
  { name: 'לוס אנג\'לס', key: 'itimLabina204', lat: 34.18, lng: -118.545, elevation: 236, tzid: 'America/Los_Angeles' },
  { name: 'לינקולן', key: 'itimLabina205', lat: 44.051, lng: -71.671, elevation: 267, tzid: 'America/New_York' },
  { name: 'ליקוואוד', key: 'itimLabina78', lat: 40.0957, lng: -74.222, elevation: 35, tzid: 'America/New_York' },
  { name: 'מאנסי', key: 'itimLabina79', lat: 41.111, lng: -74.0685, elevation: 168, tzid: 'America/New_York' },
  { name: 'מבוא דותן', key: 'itimLabina230', lat: 32.4185, lng: 35.173, elevation: 275, tzid: 'Asia/Jerusalem' },
  { name: 'מבוא חורון', key: 'itimLabina166', lat: 31.85, lng: 35.0345, elevation: 251, tzid: 'Asia/Jerusalem' },
  { name: 'מבשרת ציון', key: 'itimLabina180', lat: 31.805, lng: 35.1515, elevation: 747, tzid: 'Asia/Jerusalem' },
  { name: 'מגדל', key: 'itimLabina107', lat: 32.839, lng: 35.5, elevation: -156, tzid: 'Asia/Jerusalem' },
  { name: 'מגדל העמק', key: 'itimLabina50', lat: 32.678, lng: 35.241, elevation: 223, tzid: 'Asia/Jerusalem' },
  { name: 'מדריד', key: 'itimLabina161', lat: 40.472, lng: -3.66, elevation: 735, tzid: 'Europe/Madrid' },
  { name: 'מודיעין עילית', key: 'itimLabina45', lat: 31.932, lng: 35.046, elevation: 302, tzid: 'Asia/Jerusalem' },
  { name: 'מיאמי', key: 'itimLabina91', lat: 25.967, lng: -80.149, elevation: 6, tzid: 'America/New_York' },
  { name: 'מיצד', key: 'itimLabina81', lat: 31.586, lng: 35.188, elevation: 949, tzid: 'Asia/Jerusalem' },
  { name: 'מירון', key: 'itimLabina56', lat: 32.9808, lng: 35.4406, elevation: 725, tzid: 'Asia/Jerusalem' },
  { name: 'מלבורן', key: 'itimLabina134', lat: -37.8695, lng: 144.999, elevation: 20, tzid: 'Australia/Melbourne' },
  { name: 'מנצ\'סטר', key: 'itimLabina187', lat: 53.509, lng: -2.2585, elevation: 63, tzid: 'Europe/London' },
  { name: 'מעלה אדומים', key: 'itimLabina87', lat: 31.775, lng: 35.3, elevation: 498, tzid: 'Asia/Jerusalem' },
  { name: 'מעלה עמוס', key: 'itimLabina94', lat: 31.596, lng: 35.23, elevation: 718, tzid: 'Asia/Jerusalem' },
  { name: 'מעלות', key: 'itimLabina111', lat: 33.013, lng: 35.283, elevation: 542, tzid: 'Asia/Jerusalem' },
  { name: 'מצפה יריחו', key: 'itimLabina188', lat: 31.8185, lng: 35.394, elevation: 176, tzid: 'Asia/Jerusalem' },
  { name: 'מצפה רמון', key: 'itimLabina152', lat: 30.612, lng: 34.801, elevation: 857, tzid: 'Asia/Jerusalem' },
  { name: 'מקסיקו - אינטרלומאס', key: 'itimLabina225', lat: 19.3905, lng: -99.2865, elevation: 2546, tzid: 'America/Mexico_City' },
  { name: 'מקסיקו - בוסקס', key: 'itimLabina223', lat: 19.3932, lng: -99.265, elevation: 2554, tzid: 'America/Mexico_City' },
  { name: 'מקסיקו - טקמצ\'לקו (אמצע)', key: 'itimLabina226', lat: 19.4135, lng: -99.2445, elevation: 2422, tzid: 'America/Mexico_City' },
  { name: 'מקסיקו - טקמצ\'לקו (כניסה)', key: 'itimLabina224', lat: 19.428, lng: -99.228, elevation: 2326, tzid: 'America/Mexico_City' },
  { name: 'מקסיקו - פולנקו', key: 'itimLabina53', lat: 19.4398, lng: -99.2006, elevation: 2257, tzid: 'America/Mexico_City' },
  { name: 'מקסיקו - קוארנבקה', key: 'itimLabina125', lat: 18.9424, lng: -99.2, elevation: 1581, tzid: 'America/Mexico_City' },
  { name: 'מרסיי', key: 'itimLabina158', lat: 43.296, lng: 5.37, elevation: 0, tzid: 'Europe/Paris' },
  { name: 'נהריה', key: 'itimLabina149', lat: 33.017, lng: 35.102, elevation: 13, tzid: 'Asia/Jerusalem' },
  { name: 'נווה', key: 'itimLabina85', lat: 31.162, lng: 34.33, elevation: 94, tzid: 'Asia/Jerusalem' },
  { name: 'נוף איילון שעלבים', key: 'itimLabina112', lat: 31.87, lng: 34.985, elevation: 213, tzid: 'Asia/Jerusalem' },
  { name: 'נוף הגליל', key: 'itimLabina72', lat: 32.729, lng: 35.346, elevation: 478, tzid: 'Asia/Jerusalem' },
  { name: 'נחליאל', key: 'itimLabina106', lat: 31.974, lng: 35.1415, elevation: 596, tzid: 'Asia/Jerusalem' },
  { name: 'ניר עציון', key: 'itimLabina169', lat: 32.6975, lng: 34.9935, elevation: 229, tzid: 'Asia/Jerusalem' },
  { name: 'נתיבות', key: 'itimLabina27', lat: 31.422, lng: 34.585, elevation: 147, tzid: 'Asia/Jerusalem' },
  { name: 'סאו פאולו', key: 'itimLabina133', lat: -23.5369, lng: -46.6559, elevation: 780, tzid: 'America/Sao_Paulo' },
  { name: 'סטטן איילנד', key: 'itimLabina80', lat: 40.5835, lng: -74.15, elevation: 51, tzid: 'America/New_York' },
  { name: 'סן אנטוניו', key: 'itimLabina155', lat: 29.5685, lng: -98.515, elevation: 275, tzid: 'America/Chicago' },
  { name: 'סנה', key: 'itimLabina124', lat: 31.8437, lng: 35.305, elevation: 493, tzid: 'Asia/Jerusalem' },
  { name: 'סענט לואיס', key: 'itimLabina129', lat: 47.5879, lng: 7.566, elevation: 260, tzid: 'Europe/Paris' },
  { name: 'ספסופה', key: 'itimLabina159', lat: 33.011, lng: 35.4385, elevation: 727, tzid: 'Asia/Jerusalem' },
  { name: 'עוצם', key: 'itimLabina105', lat: 31.637, lng: 34.703, elevation: 104, tzid: 'Asia/Jerusalem' },
  { name: 'עזה', key: 'itimLabina184', lat: 31.4785, lng: 34.415, elevation: 31, tzid: 'Asia/Jerusalem' },
  { name: 'עכו', key: 'itimLabina104', lat: 32.93, lng: 35.09, elevation: 19, tzid: 'Asia/Jerusalem' },
  { name: 'עלי', key: 'itimLabina202', lat: 32.07, lng: 35.263, elevation: 702, tzid: 'Asia/Jerusalem' },
  { name: 'עמנואל', key: 'itimLabina102', lat: 32.163, lng: 35.135, elevation: 396, tzid: 'Asia/Jerusalem' },
  { name: 'עפולה', key: 'itimLabina37', lat: 32.6, lng: 35.29, elevation: 56, tzid: 'Asia/Jerusalem' },
  { name: 'עפולה גבעת המורה', key: 'itimLabina228', lat: 32.6215, lng: 35.3325, elevation: 314, tzid: 'Asia/Jerusalem' },
  { name: 'עפולה עילית', key: 'itimLabina227', lat: 32.6305, lng: 35.326, elevation: 162, tzid: 'Asia/Jerusalem' },
  { name: 'עקסלעבען', key: 'itimLabina114', lat: 45.69, lng: 5.90598, elevation: 245, tzid: 'Europe/Paris' },
  { name: 'ערד', key: 'itimLabina103', lat: 31.2565, lng: 35.211, elevation: 604, tzid: 'Asia/Jerusalem' },
  { name: 'פאלסבורג', key: 'itimLabina77', lat: 41.7302, lng: -74.6206, elevation: 409, tzid: 'America/New_York' },
  { name: 'פייב טאונס', key: 'itimLabina232', lat: 40.623, lng: -73.7245, elevation: 16, tzid: 'America/New_York' },
  { name: 'פינסק בלארוס', key: 'itimLabina173', lat: 52.1167, lng: 26.1136, elevation: 144, tzid: 'Europe/Minsk' },
  { name: 'פנמה סיטי', key: 'itimLabina162', lat: 8.975, lng: -79.528, elevation: 7, tzid: 'America/Panama' },
  { name: 'פסאיק ניו גרזי', key: 'itimLabina207', lat: 40.8565, lng: -74.1285, elevation: 42, tzid: 'America/New_York' },
  { name: 'פריז', key: 'itimLabina110', lat: 48.8666, lng: 2.33, elevation: 52, tzid: 'Europe/Paris' },
  { name: 'ציריך', key: 'itimLabina222', lat: 47.3695, lng: 8.5265, elevation: 422, tzid: 'Europe/Zurich' },
  { name: 'צפת 933', key: 'itimLabina219', lat: 32.979, lng: 35.5065, elevation: 933, tzid: 'Asia/Jerusalem' },
  { name: 'צפת רידבז', key: 'itimLabina176', lat: 32.9591, lng: 35.4985, elevation: 730, tzid: 'Asia/Jerusalem' },
  { name: 'קבר שמואל הנביא', key: 'itimLabina217', lat: 31.833, lng: 35.1805, elevation: 887, tzid: 'Asia/Jerusalem' },
  { name: 'קווינס', key: 'itimLabina208', lat: 40.7135, lng: -73.8285, elevation: 24, tzid: 'America/New_York' },
  { name: 'קוממיות', key: 'itimLabina73', lat: 31.658, lng: 34.736, elevation: 80, tzid: 'Asia/Jerusalem' },
  { name: 'קיסריה', key: 'itimLabina119', lat: 32.519, lng: 34.904, elevation: 10, tzid: 'Asia/Jerusalem' },
  { name: 'קליבלנד', key: 'itimLabina148', lat: 41.4995, lng: -81.6935, elevation: 200, tzid: 'America/New_York' },
  { name: 'קרית ארבע', key: 'itimLabina160', lat: 31.544, lng: 35.122, elevation: 976, tzid: 'Asia/Jerusalem' },
  { name: 'קרית אתא', key: 'itimLabina47', lat: 32.803, lng: 35.105, elevation: 32, tzid: 'Asia/Jerusalem' },
  { name: 'קרית גת', key: 'itimLabina74', lat: 31.613, lng: 34.754, elevation: 119, tzid: 'Asia/Jerusalem' },
  { name: 'קרית טבעון', key: 'itimLabina88', lat: 32.722, lng: 35.13, elevation: 177, tzid: 'Asia/Jerusalem' },
  { name: 'קרית יואל ניו יורק', key: 'itimLabina193', lat: 41.341, lng: -74.168, elevation: 216, tzid: 'America/New_York' },
  { name: 'קרית יערים טלז סטון', key: 'itimLabina34', lat: 31.803, lng: 35.1, elevation: 700, tzid: 'Asia/Jerusalem' },
  { name: 'קרית מלאכי', key: 'itimLabina66', lat: 31.731, lng: 34.745, elevation: 61, tzid: 'Asia/Jerusalem' },
  { name: 'קרית שמונה', key: 'itimLabina95', lat: 33.208, lng: 35.574, elevation: 122, tzid: 'Asia/Jerusalem' },
  { name: 'קשת', key: 'itimLabina117', lat: 32.9805, lng: 35.8085, elevation: 708, tzid: 'Asia/Jerusalem' },
  { name: 'ראש העין', key: 'itimLabina83', lat: 32.0955, lng: 34.9535, elevation: 50, tzid: 'Asia/Jerusalem' },
  { name: 'רבבה', key: 'itimLabina231', lat: 32.1205, lng: 35.125, elevation: 399, tzid: 'Asia/Jerusalem' },
  { name: 'ריוורדייל', key: 'itimLabina147', lat: 40.9005, lng: -73.9065, elevation: 57, tzid: 'America/New_York' },
  { name: 'רימונים חוף השרון', key: 'itimLabina131', lat: 32.241, lng: 34.8851, elevation: 34, tzid: 'Asia/Jerusalem' },
  { name: 'רכסים', key: 'itimLabina32', lat: 32.753, lng: 35.1, elevation: 30, tzid: 'Asia/Jerusalem' },
  { name: 'רמלה', key: 'itimLabina101', lat: 31.935, lng: 34.882, elevation: 87, tzid: 'Asia/Jerusalem' },
  { name: 'רמת השרון', key: 'itimLabina211', lat: 32.143, lng: 34.838, elevation: 69, tzid: 'Asia/Jerusalem' },
  { name: 'רמת מגשימים', key: 'itimLabina170', lat: 32.844, lng: 35.8075, elevation: 430, tzid: 'Asia/Jerusalem' },
  { name: 'רנסי', key: 'itimLabina122', lat: 48.895, lng: 2.5155, elevation: 99, tzid: 'Europe/Paris' },
  { name: 'רעננה', key: 'itimLabina82', lat: 32.184, lng: 34.871, elevation: 44, tzid: 'Asia/Jerusalem' },
  { name: 'שדרות', key: 'itimLabina171', lat: 31.5265, lng: 34.597, elevation: 80, tzid: 'Asia/Jerusalem' },
  { name: 'שטראסבורג', key: 'itimLabina113', lat: 48.5904, lng: 7.75643, elevation: 143, tzid: 'Europe/Paris' },
  { name: 'שיקגו', key: 'itimLabina179', lat: 42, lng: -87.7005, elevation: 190, tzid: 'America/Chicago' },
  { name: 'שלומי', key: 'itimLabina127', lat: 33.0687, lng: 35.142, elevation: 55, tzid: 'Asia/Jerusalem' },
  { name: 'תל ציון', key: 'itimLabina65', lat: 31.8768, lng: 35.2394, elevation: 759, tzid: 'Asia/Jerusalem' },
  { name: 'תפרח', key: 'itimLabina42', lat: 31.325, lng: 34.678, elevation: 169, tzid: 'Asia/Jerusalem' },
]
