using System.Text;

namespace PersianKeyboardConverter.Services
{
    /// <summary>
    /// Maps characters between the English QWERTY layout and the standard Persian (Farsi) keyboard layout.
    /// Conversion is bidirectional and character-by-character.
    /// </summary>
    public static class KeyboardMapper
    {
        // English → Persian mapping (standard Iranian ISIRI keyboard layout)
        private static readonly Dictionary<char, char> EnToPe = new()
        {
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
            { '[', '\u062C' }, // ج
            { ']', '\u0686' }, // چ

            { 'a', '\u0634' }, // ش
            { 's', '\u0633' }, // س
            { 'd', '\u06CC' }, // ی
            { 'f', '\u0628' }, // ب
            { 'g', '\u0644' }, // ل
            { 'h', '\u0627' }, // ا
            { 'j', '\u062A' }, // ت
            { 'k', '\u0646' }, // ن
            { 'l', '\u0645' }, // م
            { ';', '\u06A9' }, // ک
            { '\'', '\u06AF' }, // گ

            { 'z', '\u0638' }, // ظ
            { 'x', '\u0637' }, // ط
            { 'c', '\u0632' }, // ز
            { 'v', '\u0631' }, // ر
            { 'b', '\u0630' }, // ذ
            { 'n', '\u062F' }, // د
            { 'm', '\u0626' }, // ئ
            { ',', '\u0648' }, // و
            { '.', '\u0632' }, // ز  (dot → ز on some layouts; use standard)
            { '/', '\u0632' }, // fallback

            // Uppercase equivalents map to Persian shifted chars
            { 'Q', '\u0652' }, // ْ (sukun)
            { 'W', '\u0064' }, // fallback (some use ص shift variants)
            { 'E', '\u064B' }, // ً (tanwin nasb)
            { 'R', '\u064C' }, // ٌ (tanwin damm)
            { 'T', '\u064D' }, // ٍ (tanwin kasr)
            { 'Y', '\u064E' }, // َ (fatha)
            { 'U', '\u064F' }, // ُ (damma)
            { 'I', '\u0650' }, // ِ (kasra)
            { 'O', '\u0651' }, // ّ (shadda)
            { 'P', '\u0029' }, // )  — Persian shifted P
            { '{', '\u005D' }, // ]
            { '}', '\u005B' }, // [

            { 'A', '\u0622' }, // آ
            { 'S', '\u0624' }, // ؤ
            { 'D', '\u06CC' }, // ی (same)
            { 'F', '\u0625' }, // إ
            { 'G', '\u0623' }, // أ
            { 'H', '\u0671' }, // ٱ
            { 'J', '\u0640' }, // ـ (tatweel)
            { 'K', '\u00AB' }, // «
            { 'L', '\u00BB' }, // »
            { ':', '\u003A' }, // :
            { '"', '\u061B' }, // ؛

            { 'Z', '\u0629' }, // ة
            { 'X', '\u0637' }, // ط (same as x)
            { 'C', '\u0698' }, // ژ
            { 'V', '\u0670' }, // ٰ
            // 'B' → لا is two chars; it is handled in EnToPeMulti below, not here
            { 'N', '\u062F' }, // د  (N on some Persian keyboard variants)
            { 'M', '\u0621' }, // ء
            { '<', '\u0650' }, // ِ
            { '>', '\u064E' }, // َ
            { '?', '\u061F' }, // ؟

            // Numbers — standard Persian digits via Shift+number on some keyboards
            // Persian digit mapping (when numlock/number row used)
            { '`', '\u0060' }, // keep as-is
            { '~', '\u0651' }, // ّ

            // Number row (digits stay as digits; Persian digits via Alt/special)
            { '1', '\u06F1' }, // ۱
            { '2', '\u06F2' }, // ۲
            { '3', '\u06F3' }, // ۳
            { '4', '\u06F4' }, // ۴
            { '5', '\u06F5' }, // ۵
            { '6', '\u06F6' }, // ۶
            { '7', '\u06F7' }, // ۷
            { '8', '\u06F8' }, // ۸
            { '9', '\u06F9' }, // ۹
            { '0', '\u06F0' }, // ۰
            { '-', '\u002D' }, // -  (same)
            { '=', '\u003D' }, // =  (same)
        };

        // Build Persian → English reverse map automatically
        private static readonly Dictionary<char, char> PeToEn;

        // Special two-character Persian output for certain English keys
        private static readonly Dictionary<char, string> EnToPeMulti = new()
        {
            { 'B', "\u0644\u0627" }, // لا
        };

        static KeyboardMapper()
        {
            PeToEn = new Dictionary<char, char>();
            foreach (var kvp in EnToPe)
            {
                // Skip multi-char entries and duplicates; first-seen wins
                if (!PeToEn.ContainsKey(kvp.Value))
                    PeToEn[kvp.Value] = kvp.Key;
            }

            // Add Persian digit → ASCII digit reverse map
            for (int i = 0; i <= 9; i++)
            {
                char persianDigit = (char)('\u06F0' + i);
                char asciiDigit = (char)('0' + i);
                if (!PeToEn.ContainsKey(persianDigit))
                    PeToEn[persianDigit] = asciiDigit;
            }

            // Additional explicit Persian → English entries that aren't in EnToPe
            PeToEn['\u061F'] = '?';  // ؟ → ?
            PeToEn['\u061B'] = ';';  // ؛ → ;
            PeToEn['\u00AB'] = 'K';  // « → K
            PeToEn['\u00BB'] = 'L';  // » → L
        }

        /// <summary>
        /// Converts a string by flipping every character between English and Persian layout.
        /// The direction is determined per-character: English chars → Persian, Persian chars → English.
        /// </summary>
        public static string Convert(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new StringBuilder(input.Length * 2);
            foreach (char c in input)
            {
                if (EnToPeMulti.TryGetValue(c, out string? multi))
                {
                    sb.Append(multi);
                }
                else if (EnToPe.TryGetValue(c, out char persian))
                {
                    sb.Append(persian);
                }
                else if (PeToEn.TryGetValue(c, out char english))
                {
                    sb.Append(english);
                }
                else
                {
                    // Keep unchanged (spaces, punctuation not in map, etc.)
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
        /// Detects whether the majority of mappable characters in the string are Persian or English,
        /// to give a hint about which direction the conversion went.
        /// </summary>
        public static bool IsMostlyPersian(string text)
        {
            int pe = 0, en = 0;
            foreach (char c in text)
            {
                if (IsPersian(c)) pe++;
                else if (char.IsLetter(c)) en++;
            }
            return pe >= en;
        }
    }
}
