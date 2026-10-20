<script setup lang="ts">
/**
 * Terms of use / copyright notice. Mandatory: the wizard hides Skip and keeps Next disabled
 * on this step until the checkbox is ticked (see SetupWizard.vue). The checkbox itself is
 * disabled until the terms box has been scrolled to the bottom.
 */
import { ref, watch } from 'vue'
import { useScroll } from '@vueuse/core'

const agreed = defineModel<boolean>({ default: false })

// The checkbox stays disabled until the user has scrolled to the bottom of the terms once.
const termsBox = ref<HTMLElement | null>(null)
const { arrivedState, measure } = useScroll(termsBox, { offset: { bottom: 4 } })
const reachedBottom = ref(agreed.value)
watch(termsBox, (el) => el && measure(), { flush: 'post' })
watch(
  () => arrivedState.bottom,
  (atBottom) => {
    if (atBottom) reachedBottom.value = true
  },
  { immediate: true },
)
</script>

<template>
  <div class="terms-step">
    <div ref="termsBox" class="terms-box toc-thin-scroll" tabindex="0">
      <h3>כללי</h3>
      <p>
        כתבי הקודש היא תוכנה לעיון ולימוד בספרי הקודש, והיא חלק מפרויקט "כלי קודש" — מיזם
        התנדבותי שאינו למטרות רווח, שנועד להנגיש את וורד ואת המחשב ללומדים ולהקל על עבודתם.
        התוכנה ניתנת לשימוש ללא תשלום.
      </p>

      <h3>מקור הטקסטים וזכויות יוצרים</h3>
      <p>
        הספרים המוצגים בתוכנה אינם חלק ממנה. הם נקראים ממאגר הספרים של תוכנת אוצריא (או זית)
        המותקן במחשב, ומקורם במאגרים ציבוריים כגון אוצריא, ספריא ואחרים.
      </p>
      <p>
        הזכויות בכל טקסט שמורות לבעליהן כדין. חלק מהספרים נמצאים בנחלת הכלל, וחלקם הועלו
        באישור בעלי הזכויות או לפי תנאי רישיון מסוימים. אין לעשות שימוש מסחרי בטקסטים, ואין
        להעתיק, להפיץ או לפרסם חומר המוגן בזכויות יוצרים, אלא בהתאם לתנאים שקבעו בעלי
        הזכויות.
      </p>
      <p>
        אם הנך בעל זכויות בספר מסוים וסבור שהוא מופיע שלא כדין, נא לפנות למאגר שממנו הגיע
        הספר, או אלינו, והדבר יטופל בהקדם.
      </p>

      <h3>אחריות</h3>
      <p>
        התוכנה ניתנת כמות שהיא (AS IS), ללא כל אחריות מכל סוג שהוא, מפורשת או משתמעת. המפתחים
        לא יישאו באחריות לכל נזק, ישיר או עקיף, שייגרם כתוצאה מהשימוש בתוכנה או מחוסר
        האפשרות להשתמש בה.
      </p>

      <h3>דיוק התוכן</h3>
      <p>
        הטקסטים הוקלדו ועובדו בידי אדם ובאמצעים ממוחשבים, וייתכנו בהם טעויות והשמטות. האחריות
        לבדוק את דיוק הדברים מוטלת על המשתמש. אין לסמוך על התוכנה לפסיקת הלכה למעשה — יש
        לשאול רב מורה הוראה.
      </p>

      <h3>דיווח על טעויות</h3>
      <p>
        נשמח לקבל דיווח על טעויות בטקסט או בתוכנה. ניתן לדווח דרך אפשרות הדיווח על שגיאה
        שבתוכנה, ובכך לסייע בתיקון המאגר לטובת כלל הלומדים.
      </p>

      <h3>פרויקט כלי קודש</h3>
      <p>
        פרויקט כלי קודש מפתח ערכת כלים חינמית לעורכים תורניים ולכלל הלומדים: ספריית ספרי קודש
        וחיפוש בהם, ספריות ואתרים תורניים, עיצוב תורני של מסמכים, ניקוד וחיפוש מתקדם. מידע
        נוסף ועדכונים באתר הפרויקט: kleikodesh.github.io
      </p>
      <p>יהי רצון שנזכה להגדיל תורה ולהאדירה.</p>
    </div>

    <label class="terms-agree" :class="{ disabled: !reachedBottom }">
      <input v-model="agreed" type="checkbox" :disabled="!reachedBottom" />
      <span>קראתי את התנאים ואני מסכים להם</span>
    </label>
  </div>
</template>

<style scoped>
.terms-step {
  display: flex;
  flex-direction: column;
  gap: 12px;
  flex: 1;
  min-height: 0;
}

.terms-box {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 12px 14px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-primary);
  font-size: 13px;
  line-height: 1.7;
  color: var(--text-primary);
}

.terms-box h3 {
  margin: 12px 0 4px;
  font-size: 13.5px;
  font-weight: 700;
}
.terms-box h3:first-child {
  margin-top: 0;
}
.terms-box p {
  margin: 0 0 6px;
  color: var(--text-secondary);
}

.terms-agree {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  cursor: pointer;
  user-select: none;
}
.terms-agree input {
  accent-color: var(--accent-color);
  width: 15px;
  height: 15px;
  cursor: pointer;
}
.terms-agree.disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.terms-agree.disabled input {
  cursor: not-allowed;
}
</style>
