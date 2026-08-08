using System.Text;

namespace PersianKeyboardConverter.Services
{
    /// <summary>
    /// Maps characters between the English QWERTY layout and the standard Persian (Farsi) keyboard layout.
    /// The conversion direction is decided ONCE per string (not per character) to avoid
    /// accidentally converting ASCII symbols inside Persian text and vice versa.
    /// </summary>
    public static class KeyboardMapper
    {
        public enum Direction
        {
            Auto,
            EnglishToPersian,
            PersianToEnglish
        }

        // English → Persian mapping (standard Iranian keyboard layout)
        private static readonly Dictionary<char, char> EnToPe = new()
          {
              // --- حروف کوچک (Lowercase) ---
              { 'q', '\u0636' }, // ض
              { 'w', '\u0635' }, // ص
              { 'e', '\u062B' }, // ث
              { 'r', '\u0642' }, // ق
              { 't', '\u0641' }, // ف
              { 'y', '\u063A' }, // غ
              { 'u', '\u0639' }, // ع
              { 'i', '\u0647' }, // ه
              { 'o', '\u062E' }, // خ
              { 'p', '\u062D' }, // ح
              { 'a', '\u0634' }, // ش
              { 's', '\u0633' }, // س
              { 'd', '\u06CC' }, // ی (فارسی)
              { 'f', '\u0628' }, // ب
              { 'g', '\u0644' }, // ل
              { 'h', '\u0627' }, // ا
              { 'j', '\u062A' }, // ت
              { 'k', '\u0646' }, // ن
              { 'l', '\u0645' }, // م
              { 'z', '\u0638' }, // ظ
              { 'x', '\u0637' }, // ط
              { 'c', '\u0632' }, // ز
              { 'v', '\u0631' }, // ر
              { 'b', '\u0630' }, // ذ
              { 'n', '\u062F' }, // د
              { 'm', '\u0626' }, // ئ
  
              // --- حروف بزرگ و اعراب (Uppercase & Diacritics) ---
              { 'Q', '\u0652' }, // ْ (ساکن)
              { 'W', '\u064C' }, // ٌ (تنوین ضمه)
              { 'E', '\u064D' }, // ٍ (تنوین کسره)
              { 'R', '\u064B' }, // ً (تنوین فتحه)
              { 'T', '\u064F' }, // ُ (ضمه)
              { 'Y', '\u064E' }, // َ (فتحه)
              { 'U', '\u0650' }, // ِ (کسره)
              { 'I', '\u0651' }, // ّ (تشدید)
              { 'O', '\u005B' }, // [
              { 'P', '\u005D' }, // ]
              { 'A', '\u0622' }, // آ
              { 'S', '\u0624' }, // ؤ
              { 'D', '\u064A' }, // ي (ي عربی)
              { 'F', '\u0625' }, // إ
              { 'G', '\u0623' }, // أ
              { 'H', '\u0671' }, // ٱ (الف وصل)
              { 'J', '\u0640' }, // ـ (کشیده / Tatweel)
              { 'K', '\u00AB' }, // «
              { 'L', '\u00BB' }, // »
              { 'Z', '\u0629' }, // ة
              { 'C', '\u0698' }, // ژ
              { 'V', '\u0670' }, // ٰ (الف خنجری)
              { 'M', '\u0621' }, // ء (همزه)
              // NOTE: 'X' → ط و 'N' → د حذف شدند؛ تکراری با x و n بودند و نقشهٔ معکوس را خراب می‌کردند.
  
              // --- اعداد فارسی (Numbers) ---
              { '0', '\u06F0' }, // ۰
              { '1', '\u06F1' }, // ۱
              { '2', '\u06F2' }, // ۲
              { '3', '\u06F3' }, // ۳
              { '4', '\u06F4' }, // ۴
              { '5', '\u06F5' }, // ۵
              { '6', '\u06F6' }, // ۶
              { '7', '\u06F7' }, // ۷
              { '8', '\u06F8' }, // ۸
              { '9', '\u06F9' }, // ۹
  
              // --- علائم و نشانه‌ها (Punctuation & Symbols) ---
              // NOTE: '`' → پ حذف شد؛ پ روی کلید بک‌اسلش است و ورودی تکراری، معکوسِ پ را '`' می‌کرد.
              { '~', '\u00F7' }, // ÷ (علامت تقسیم)
              { '@', '\u066B' }, // ٫ (ممیز اعشار فارسی)
              { '#', '\u066C' }, // ٬ (جداکننده هزارگان)
              { '$', '\uFDFC' }, // ﷼ (علامت ریال)
              { '%', '\u066A' }, // ٪ (درصد فارسی)
              { '^', '\u00D7' }, // × (علامت ضرب)
              { '&', '\u060C' }, // ، (کامای فارسی)
              { '(', '\u0029' }, // )
              { ')', '\u0028' }, // (
              { '[', '\u062C' }, // ج
              { ']', '\u0686' }, // چ
              { '{', '\u007D' }, // }
              { '}', '\u007B' }, // {
              { '\\', '\u067E' }, // پ
              { ';', '\u06A9' }, // ک
              { '\'', '\u06AF' }, // گ
              { '"', '\u061B' }, // ؛ (نقطه ویرگول فارسی)
              { ',', '\u0648' }, // و
              { '<', '\u003E' }, // > (جهت در متون راست‌به‌چپ)
              { '>', '\u003C' }, // < (جهت در متون راست‌به‌چپ)
              { '?', '\u061F' }  // ؟ (علامت سوال فارسی)
              // NOTE: نگاشت‌های همانی (! * - _ = + | : . /) حذف شدند؛ بی‌اثر بودند.
          };

        // Build Persian → English reverse map automatically
        private static readonly Dictionary<char, char> PeToEn;

        // Special two-character Persian output for certain English keys
        private static readonly Dictionary<char, string> EnToPeMulti = new()
          {
              { 'B', "\u0644\u0627" }, // لا
              // NOTE: در جهت معکوس، «لا» عمداً به B تبدیل نمی‌شود؛
              // چون تقریباً همیشه حاصل زدن g و h است (مثل «سلام» → "sghl").
          };

        static KeyboardMapper()
        {
            PeToEn = new Dictionary<char, char>();
            foreach (var kvp in EnToPe)
            {
                // در تداخل‌ها، کلیدِ بدون Shift (حروف کوچک) اولویت دارد
                if (!PeToEn.ContainsKey(kvp.Value) || (char.IsUpper(PeToEn[kvp.Value]) && char.IsLower(kvp.Key)))
                    PeToEn[kvp.Value] = kvp.Key;
            }

            // Additional explicit Persian → English entries that aren't in EnToPe
            PeToEn['\u064A'] = 'd';  // ي عربی → d (مثل ی فارسی رفتار کند)
            PeToEn['\u0643'] = ';';  // ك عربی → ; (مثل ک فارسی)
        }

        /// <summary>
        /// Converts a string between English and Persian layouts.
        /// Direction is decided once for the whole string (Auto), or forced by the caller.
        /// </summary>
        public static string Convert(string input, Direction direction = Direction.Auto)
        {
            if (string.IsNullOrEmpty(input)) return input;

            if (direction == Direction.Auto)
                direction = IsMostlyPersian(input)
                    ? Direction.PersianToEnglish
                    : Direction.EnglishToPersian;

            var sb = new StringBuilder(input.Length * 2);
            foreach (char c in input)
            {
                if (direction == Direction.EnglishToPersian)
                {
                    if (EnToPeMulti.TryGetValue(c, out string? multi))
                        sb.Append(multi);
                    else if (EnToPe.TryGetValue(c, out char persian))
                        sb.Append(persian);
                    else
                        sb.Append(c);
                }
                else // PersianToEnglish
                {
                    if (PeToEn.TryGetValue(c, out char english))
                        sb.Append(english);
                    else
                        sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns true if the character is a Persian (Arabic-script) character.
        /// </summary>
        public static bool IsPersian(char c)
            => (c >= '\u0600' && c <= '\u06FF') || (c >= '\uFB50' && c <= '\uFDFF') || (c >= '\uFE70' && c <= '\uFEFF');

        /// <summary>
        /// Detects whether the majority of mappable characters in the string are Persian or English.
        /// </summary>
        public static bool IsMostlyPersian(string text)
        {
            int pe = 0, en = 0;
            foreach (char c in text)
            {
                if (IsPersian(c)) pe++;
                else if (char.IsLetter(c)) en++;
            }
            return pe > en; // تساوی (مثلاً فقط علائم) → انگلیسی به فارسی
        }
    }
}