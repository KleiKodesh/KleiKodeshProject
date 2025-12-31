// Regex tips data - converted from JSON for better build integration and performance
export const regexTips = [
  {
    "Symbol": ".",
    "Meaning": "כל תו בודד",
    "Example": "a.b → acb, a1b"
  },
  {
    "Symbol": "*",
    "Meaning": "אפס או יותר מהאלמנט הקודם",
    "Example": "a* → '', a, aa"
  },
  {
    "Symbol": "+",
    "Meaning": "אחד או יותר מהאלמנט הקודם",
    "Example": "a+ → a, aa"
  },
  {
    "Symbol": "?",
    "Meaning": "אפס או אחד מהאלמנט הקודם",
    "Example": "a? → '', a"
  },
  {
    "Symbol": "\\d",
    "Meaning": "ספרה",
    "Example": "\\d → 0-9"
  },
  {
    "Symbol": "\\w",
    "Meaning": "תו מילה",
    "Example": "\\w → a-z, 0-9, ＿"
  },
  {
    "Symbol": "\\s",
    "Meaning": "רווח לבן",
    "Example": "\\s → רווח, טאבים, שורות חדשות"
  },
  {
    "Symbol": "[abc]",
    "Meaning": "כל אחד מ-א', ב', או ג'",
    "Example": "[אבג] → או א' או ב' או ג'"
  },
  {
    "Symbol": "[0-9]",
    "Meaning": "כל ספרה",
    "Example": "[0-9] → ספרה כלשהי"
  },
  {
    "Symbol": "[a-z]",
    "Meaning": "טווח a עד z",
    "Example": "[a-z] → לאותיות קטנות"
  },
  {
    "Symbol": "[A-Z]",
    "Meaning": "טווח A עד Z",
    "Example": "[A-Z] → לאותיות גדולות"
  },
  {
    "Symbol": "^",
    "Meaning": "תחילת מחרוזת או שורה",
    "Example": "^אבג → המחרוזת אבג בתחילה"
  },
  {
    "Symbol": "$",
    "Meaning": "סיום מחרוזת או שורה",
    "Example": "אבג$ → המחרוזת אבג בסוף"
  },
  {
    "Symbol": "\\",
    "Meaning": "תו בריחה (מגדיר תו מיוחד כתו רגיל)",
    "Example": "\\* → כוכבית ממש"
  },
  {
    "Symbol": "|",
    "Meaning": "אלטרנטיבה (או)",
    "Example": "שור|כבש → או שור או כבש"
  },
  {
    "Symbol": "(...)",
    "Meaning": "קבוצת לכידה",
    "Example": "(אב)+ → תואם אב, אבאב"
  },
  {
    "Symbol": "\\b",
    "Meaning": "גבול מילה",
    "Example": "\\bכבש\\b → כבש אבל לא כבשה"
  },
  {
    "Symbol": "\\D",
    "Meaning": "כל תו שאינו ספרה",
    "Example": "\\D → למשל: או a או '!'"
  },
  {
    "Symbol": "\\W",
    "Meaning": "כל תו שאינו תו מילה",
    "Example": "\\W → ' ', '!'"
  },
  {
    "Symbol": "\\S",
    "Meaning": "כל תו שאינו רווח לבן",
    "Example": "\\S → כל תו שאיננו רווח, טאב או שורה חדשה"
  },
  {
    "Symbol": "[^abc]",
    "Meaning": "(הגדרה בשלילה) לא א' או ב' או ג'",
    "Example": "[^אבג] → כל תו שאיננו בטווח זה"
  },
  {
    "Symbol": ".*?",
    "Meaning": "התאמה עצלה",
    "Example": "(ב.*?) או (ב+?)→ בב מתוך בבבב"
  },
  {
    "Symbol": "{n}",
    "Meaning": "בדיוק n חזרות",
    "Example": "a{3} → aaa"
  },
  {
    "Symbol": "{n,}",
    "Meaning": "לפחות n חזרות",
    "Example": "a{2,} → aa, aaa"
  },
  {
    "Symbol": "{n,m}",
    "Meaning": "בין n ל-m חזרות",
    "Example": "a{2,4} → aa, aaa, aaaa"
  },
  {
    "Symbol": "$1",
    "Meaning": "התייחסות לקבוצה",
    "Example": "$1 → קבוצה מספר 1"
  },
  {
    "Symbol": "\\b.*?",
    "Meaning": "תחיליות",
    "Example": "\\b.*?חר\\b → תואם אחר או מחר"
  },
  {
    "Symbol": ".*?\\b",
    "Meaning": "סופיות",
    "Example": "\\bאח.*?\\b → תואם אח או אחר"
  },
  {
    "Symbol": "\\B",
    "Meaning": "לא גבול מילה",
    "Example": "\\Bכבש\\B → בתוך קטלוג"
  },
  {
    "Symbol": "\\A",
    "Meaning": "תחילת מחרוזת (לא שורה)",
    "Example": "\\Aאבג → רק בתחילת מחרוזת"
  },
  {
    "Symbol": "\\Z",
    "Meaning": "סיום לפני שורת סיום אופציונלית",
    "Example": "אבג\\Z → בסיום מחרוזת"
  },
  {
    "Symbol": "\\z",
    "Meaning": "סיום מוחלט של מחרוזת",
    "Example": "אבג\\z → לסיום מוחלט"
  },
  {
    "Symbol": "(?:...)",
    "Meaning": "קבוצה לא לוכדת",
    "Example": "(?:ab)+ → ללא לכידה"
  },
  {
    "Symbol": "(?=...)",
    "Meaning": "ציפייה חיובית",
    "Example": "א(?=ב) → תואם א לפני ב"
  },
  {
    "Symbol": "(?!...)",
    "Meaning": "ציפייה שלילית",
    "Example": "א(?!ב) → תואם א לא לפני ב"
  },
  {
    "Symbol": "(?<=...)",
    "Meaning": "ציפייה לאחור חיובית",
    "Example": "(?<=א)ב → תואם ב אחרי א"
  },
  {
    "Symbol": "(?<!...)",
    "Meaning": "ציפייה לאחור שלילית",
    "Example": "(?<!א)ב → האות ב לא אחרי א"
  }
];

export default regexTips;