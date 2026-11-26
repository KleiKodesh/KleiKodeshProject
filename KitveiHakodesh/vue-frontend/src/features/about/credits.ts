/**
 * The credits shown in About page. One row per component or data set: its name, what
 * the app uses it for (i18n key settings.credits.use.<id>), its license and a link.
 * Keep in step with KitveiHakodesh/ThirdPartyNotices/curated-notices.mjs (the full notices
 * file) and KleiKodeshVsto/Resources/About.*.html (the Word add-in About page).
 * npm / NuGet packages are not listed one by one here; the notices file lists all of them.
 */

/** Required word for word by the SeforimLibrary / Zayit license (section 4, Attribution). */
export const ZAYIT_ATTRIBUTION = 'Powered by the technologies that drive Zayit — https://zayitapp.com/'

export interface CreditItem {
  id: string
  name: string
  license: string
  url: string
}

export interface CreditGroup {
  id: 'data' | 'ported' | 'libraries' | 'fonts'
  items: CreditItem[]
}

export const CREDIT_GROUPS: CreditGroup[] = [
  {
    id: 'data',
    items: [
      { id: 'otzaria', name: 'Otzaria', license: 'Used with permission', url: 'https://www.otzaria.org' },
      { id: 'zayit', name: 'SeforimLibrary / Zayit', license: 'Zayit Public License 1.0 (AGPL-3.0)', url: 'https://zayitapp.com/' },
      { id: 'sefaria', name: 'Sefaria', license: 'Per text (CC0, CC BY, CC BY-SA, CC BY-NC, public domain)', url: 'https://www.sefaria.org' },
      { id: 'dicta', name: 'Dicta', license: 'Used with permission', url: 'https://dicta.org.il' },
      { id: 'hebrewbooks', name: 'HebrewBooks', license: 'Each book keeps its own rights', url: 'https://hebrewbooks.org' },
      { id: 'wiktionary', name: 'Hebrew Wiktionary', license: 'CC BY-SA 4.0', url: 'https://he.wiktionary.org' },
      { id: 'wikidata', name: 'Wikidata', license: 'CC0 1.0', url: 'https://www.wikidata.org' },
      { id: 'omw', name: 'Hebrew WordNet (Open Multilingual Wordnet)', license: 'Hebrew WordNet license (University of Haifa)', url: 'https://github.com/omwn/omw-data' },
      { id: 'bhsa', name: 'ETCBC BHSA', license: 'CC BY-NC 4.0', url: 'https://github.com/ETCBC/bhsa' },
      { id: 'tomer', name: 'Tomer lexicon', license: 'CC BY 4.0', url: 'https://github.com/NNLP-IL/Hebrew-Resources' },
      { id: 'hspell', name: 'Hspell', license: 'AGPL-3.0', url: 'http://hspell.ivrix.org.il/' },
      { id: 'wikipedia', name: 'Hebrew Wikipedia', license: 'CC BY-SA', url: 'https://he.wikipedia.org' },
      { id: 'hamichlol', name: 'HaMichlol', license: 'CC BY-SA', url: 'https://www.hamichlol.org.il' },
      { id: 'geonames', name: 'GeoNames', license: 'CC BY 4.0', url: 'https://www.geonames.org' },
      { id: 'openmeteo', name: 'Open-Meteo / Copernicus DEM', license: 'CC BY 4.0', url: 'https://open-meteo.com' },
      { id: 'era5', name: 'Copernicus Climate Change Service (ERA5)', license: 'Copernicus licence (attribution)', url: 'https://cds.climate.copernicus.eu' },
      { id: 'igra', name: 'NOAA IGRA radiosonde archive', license: 'Public domain', url: 'https://www.ncei.noaa.gov/products/weather-balloon/integrated-global-radiosonde-archive' },
      { id: 'cbs', name: 'Israel Central Bureau of Statistics (localities list)', license: 'CBS usage licence v2.1', url: 'https://www.cbs.gov.il' },
      { id: 'osm', name: 'OpenStreetMap contributors', license: 'ODbL 1.0', url: 'https://www.openstreetmap.org/copyright' },
    ],
  },
  {
    id: 'ported',
    items: [
      { id: 'otzariaSearch', name: 'otzaria_search_engine', license: 'MIT', url: 'https://github.com/Otzaria/otzaria_search_engine' },
      { id: 'midotjs', name: 'Midotjs', license: 'ISC', url: 'https://github.com/AmarShaked/Midotjs' },
      { id: 'uchardet', name: 'uchardet, ICU charset detector', license: 'MPL-1.1, Unicode-3.0', url: 'https://github.com/KleiKodesh/FastDocView' },
      { id: 'libreofficeShapes', name: 'LibreOffice preset shapes', license: 'MPL-2.0', url: 'https://www.libreoffice.org' },
      { id: 'numbertext', name: 'libnumbertext', license: 'BSD-3-Clause', url: 'https://github.com/Numbertext/libnumbertext' },
    ],
  },
  {
    id: 'libraries',
    items: [
      { id: 'hebcal', name: 'Hebcal (@hebcal/core, hdate, noaa, learning)', license: 'GPL-2.0, LGPL-2.1, BSD-2-Clause', url: 'https://github.com/hebcal' },
      { id: 'pdfjs', name: 'PDF.js', license: 'Apache-2.0', url: 'https://github.com/mozilla/pdf.js' },
      { id: 'tesseract', name: 'Tesseract / tesseract.js', license: 'Apache-2.0', url: 'https://github.com/naptha/tesseract.js' },
      { id: 'dictarashi', name: 'DictaRashi OCR model (Dicta - The Israel Center for Text Analysis)', license: 'CC BY-NC 4.0', url: 'https://github.com/Dicta-Israel-Center-for-Text-Analysis/dicta_ocr' },
      { id: 'kiwix', name: 'kiwix-js', license: 'GPL-3.0', url: 'https://github.com/kiwix/kiwix-js' },
      { id: 'vue', name: 'Vue, Pinia, VueUse, vue-i18n', license: 'MIT', url: 'https://vuejs.org' },
      { id: 'lucene', name: 'Lucene.Net', license: 'Apache-2.0', url: 'https://lucenenet.apache.org' },
      { id: 'messagepack', name: 'MessagePack for C#, msgpack-javascript', license: 'MIT, ISC', url: 'https://github.com/MessagePack-CSharp/MessagePack-CSharp' },
      { id: 'zstd', name: 'ZstdSharp', license: 'MIT', url: 'https://github.com/oleg-st/ZstdSharp' },
      { id: 'sqlite', name: 'SQLite', license: 'Public domain', url: 'https://sqlite.org' },
      { id: 'slint', name: 'Slint', license: 'GPL-3.0', url: 'https://slint.dev' },
    ],
  },
  {
    id: 'fonts',
    items: [
      { id: 'culmus', name: 'Culmus fonts (Taamey Frank, Hadasim, Simple, Stam CLM)', license: 'GPL-2.0 with font exception', url: 'https://culmus.sourceforge.io' },
      { id: 'frankruhl', name: 'Frank Ruhl Libre', license: 'OFL-1.1', url: 'https://github.com/fontef/frankruhllibre' },
      { id: 'heebo', name: 'Heebo', license: 'OFL-1.1', url: 'https://github.com/OdedEzer/heebo' },
      { id: 'notorashi', name: 'Noto Rashi Hebrew', license: 'OFL-1.1', url: 'https://github.com/notofonts/hebrew' },
      { id: 'fluent', name: 'Fluent UI System Icons', license: 'MIT', url: 'https://github.com/microsoft/fluentui-system-icons' },
    ],
  },
]
