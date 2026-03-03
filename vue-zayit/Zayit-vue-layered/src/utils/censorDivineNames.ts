/**
 * Censor divine names in Hebrew text string
 */
export function censorDivineNames(text: string): string {
    // Diacritics pattern: matches any Hebrew diacritic or cantillation mark
    const D = '[\\u0591-\\u05C7]*'

    // Patterns for divine names - capture groups preserve all diacritics
    const patterns = [
        // יהוה → יקוק
        {
            regex: new RegExp(`(י${D})(ה${D})(ו${D})(ה${D})`, 'g'),
            replacement: (match: string, yud: string, heh1: string, vav: string, heh2: string) => {
                return yud + heh1.replace('ה', 'ק') + vav + heh2.replace('ה', 'ק')
            }
        },
        // אדני → אדנ-י
        {
            regex: new RegExp(`(א${D})(ד${D})(נ${D})(י${D})`, 'g'),
            replacement: '$1$2$3-$4'
        },
        // אלהים → אלקים (but NOT אלהים אחרים)
        {
            regex: new RegExp(`(א${D})(ל${D})(ה${D})(י${D})(ם${D})(?!\\s*א${D}ח${D}ר${D}י${D}ם)`, 'g'),
            replacement: (match: string, alef: string, lamed: string, heh: string, yud: string, mem: string) => {
                return alef + lamed + heh.replace('ה', 'ק') + yud + mem
            }
        },
        // אלוהים → אלוקים (but NOT אלוהים אחרים)
        {
            regex: new RegExp(`(א${D})(ל${D})(ו${D})(ה${D})(י${D})(ם${D})(?!\\s*א${D}ח${D}ר${D}י${D}ם)`, 'g'),
            replacement: (match: string, alef: string, lamed: string, vav: string, heh: string, yud: string, mem: string) => {
                return alef + lamed + vav + heh.replace('ה', 'ק') + yud + mem
            }
        },
        // אלהי → אלקי
        {
            regex: new RegExp(`(א${D})(ל${D})(ה${D})(י${D})`, 'g'),
            replacement: (match: string, alef: string, lamed: string, heh: string, yud: string) => {
                return alef + lamed + heh.replace('ה', 'ק') + yud
            }
        },
        // אלוה → אלוק (not followed by י or ם)
        {
            regex: new RegExp(`(א${D})(ל${D})(ו${D})(ה${D})(?![יםא])`, 'g'),
            replacement: (match: string, alef: string, lamed: string, vav: string, heh: string) => {
                return alef + lamed + vav + heh.replace('ה', 'ק')
            }
        },
    ]

    let result = text
    patterns.forEach(({ regex, replacement }) => {
        if (typeof replacement === 'function') {
            result = result.replace(regex, replacement)
        } else {
            result = result.replace(regex, replacement)
        }
    })

    return result
}
