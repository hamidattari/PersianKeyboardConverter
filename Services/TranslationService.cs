using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PersianKeyboardConverter.Services
{
    /// <summary>Result of a translation request.</summary>
    public sealed record TranslationResult
    {
        /// <summary>The translated text.</summary>
        public string Text { get; init; } = "";

        /// <summary>True when the source text was Persian (fa → en); false for English (en → fa).</summary>
        public bool SourceWasPersian { get; init; }
    }

    /// <summary>
    /// Translates text between English and Persian. The direction is chosen from
    /// the dominant script of the source text.
    ///
    /// Google's free translate endpoint is tried first: it handles long text (up
    /// to ~5000 chars in a single request) and gives the best quality. When it is
    /// unreachable, the free MyMemory API is used as a fallback — it caps queries
    /// at 500 chars, so longer text is split into sentence/word chunks and the
    /// per-chunk translations are re-joined.
    /// </summary>
    public static class TranslationService
    {
        private const int MyMemoryMaxChars = 480; // leave margin under its 500-char query limit

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        /// <summary>
        /// Translates <paramref name="text"/> fa → en or en → fa. Returns null when
        /// the text is empty or every backend is unreachable.
        /// </summary>
        public static TranslationResult? Translate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            bool fromPersian = KeyboardMapper.IsMostlyPersian(text);

            string? translated = TranslateViaGoogle(text, fromPersian);
            if (translated != null && IsEcho(text, translated))
                translated = null; // Google echoed the input → not a real translation

            translated ??= TranslateViaMyMemory(text, fromPersian);
            if (translated != null && IsEcho(text, translated))
                translated = null;

            if (translated == null) return null;
            return new TranslationResult { Text = translated, SourceWasPersian = fromPersian };
        }

        /// <summary>True when the translation is unchanged from the source (a no-op).</summary>
        private static bool IsEcho(string source, string translated)
            => string.Equals(source.Trim(), translated.Trim(), StringComparison.Ordinal);

        // ── Google (primary) ───────────────────────────────────────────────

        private static string? TranslateViaGoogle(string text, bool fromPersian)
        {
            try
            {
                string sl = fromPersian ? "fa" : "en";
                string tl = fromPersian ? "en" : "fa";
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={Uri.EscapeDataString(text)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                using var response = Http.Send(request);
                response.EnsureSuccessStatusCode();

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);

                // Response shape: [[["segment","source",…], …], null, "lang", …]
                // The translation is the concatenation of every segment's [0] text.
                if (doc.RootElement.ValueKind != JsonValueKind.Array
                    || doc.RootElement.GetArrayLength() == 0)
                    return null;

                JsonElement segments = doc.RootElement[0];
                if (segments.ValueKind != JsonValueKind.Array)
                    return null;

                var sb = new StringBuilder();
                foreach (JsonElement segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind == JsonValueKind.Array
                        && segment.GetArrayLength() > 0
                        && segment[0].GetString() is string piece
                        && piece.Length > 0)
                        sb.Append(piece);
                }

                string result = sb.ToString();
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            catch
            {
                return null;
            }
        }

        // ── MyMemory (fallback, chunked for >500 chars) ────────────────────

        private static string? TranslateViaMyMemory(string text, bool fromPersian)
        {
            string pair = fromPersian ? "fa|en" : "en|fa";

            List<string> chunks = SplitForMyMemory(text);
            var parts = new List<string>(chunks.Count);
            foreach (string chunk in chunks)
            {
                string? part = TranslateOneMyMemory(chunk, pair);
                if (part == null) return null; // a failed chunk → give up on the fallback
                parts.Add(part);
            }

            string result = string.Concat(parts);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        private static string? TranslateOneMyMemory(string text, string pair)
        {
            try
            {
                string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={pair}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "PersianKeyboardConverter/1.0");

                using var response = Http.Send(request);
                response.EnsureSuccessStatusCode();

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                // The anonymous API signals quota/errors via responseStatus (200 = success).
                if (root.TryGetProperty("responseStatus", out JsonElement statusElement))
                {
                    bool ok = statusElement.ValueKind switch
                    {
                        JsonValueKind.Number => statusElement.GetInt32() == 200,
                        JsonValueKind.String => statusElement.GetString() == "200",
                        _ => true
                    };
                    if (!ok) return null;
                }

                if (root.TryGetProperty("responseData", out JsonElement responseData)
                    && responseData.TryGetProperty("translatedText", out JsonElement translatedElement)
                    && translatedElement.GetString() is string translated
                    && !string.IsNullOrWhiteSpace(translated))
                {
                    // MyMemory returns HTML-escaped entities (e.g. &quot;); decode for display.
                    translated = System.Net.WebUtility.HtmlDecode(translated);
                    return string.IsNullOrWhiteSpace(translated) ? null : translated;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Splits <paramref name="text"/> into chunks under the MyMemory query limit,
        /// preferring sentence boundaries then word boundaries, so a chunk never
        /// cuts a word in half (which would hurt translation quality).
        /// </summary>
        private static List<string> SplitForMyMemory(string text)
        {
            var result = new List<string>();
            foreach (string sentence in SplitOnSentenceEnds(text))
            {
                if (sentence.Length <= MyMemoryMaxChars)
                {
                    if (sentence.Length > 0) result.Add(sentence);
                    continue;
                }

                // Sentence still too long → pack whole words into chunks, then
                // re-join with the single space that separated each word so the
                // translation isn't glued together at chunk boundaries.
                var sb = new StringBuilder();
                var wordChunks = new List<string>();
                foreach (string word in sentence.Split(' '))
                {
                    if (sb.Length > 0 && sb.Length + 1 + word.Length > MyMemoryMaxChars)
                    {
                        wordChunks.Add(sb.ToString());
                        sb.Clear();
                    }
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(word);
                }
                if (sb.Length > 0) wordChunks.Add(sb.ToString());
                result.Add(string.Join(' ', wordChunks));
            }
            return result;
        }

        private static IEnumerable<string> SplitOnSentenceEnds(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                sb.Append(c);
                if (c is '.' or '!' or '?' or '\n')
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
            if (sb.Length > 0) yield return sb.ToString();
        }
    }
}
