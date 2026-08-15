using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PersianKeyboardConverter.Services
{
    /// <summary>
    /// Checks text against the LanguageTool public spelling API
    /// (https://api.languagetool.org/v2/check — free, no API key required) and
    /// returns the corrected text using each issue's best-ranked suggestion.
    ///
    /// The language is chosen from the dominant script of the text, so both
    /// Persian ("fa") and English ("en-US") words are handled.
    /// Note: the public endpoint is rate-limited to ~20 requests per IP per
    /// minute, which is plenty for an on-demand hotkey.
    /// </summary>
    public static class SpellCheckService
    {
        private const string ApiUrl = "https://api.languagetool.org/v2/check";

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        /// <summary>
        /// Returns the corrected version of <paramref name="text"/>, or null when
        /// the text appears correct, no suggestion is available, or the API is
        /// unreachable.
        /// </summary>
        public static string? CorrectText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Pick the language by the dominant script of the word.
            string language = KeyboardMapper.IsMostlyPersian(text) ? "fa" : "en-US";

            List<(int Offset, int Length, string Replacement)> matches;
            try
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["text"] = text,
                    ["language"] = language,
                    ["enabledOnly"] = "false"
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl) { Content = form };
                using var response = Http.Send(request);
                response.EnsureSuccessStatusCode();

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                matches = ParseMatches(json);
            }
            catch
            {
                return null; // offline / API error → no correction
            }

            if (matches.Count == 0)
                return null;

            // Single word: prefer the suggestion that covers the whole word —
            // that's the correction for the word itself, not a sub-issue.
            var wholeWord = matches.FirstOrDefault(m => m.Offset == 0 && m.Length == text.Length);
            if (wholeWord != default)
                return wholeWord.Replacement == text ? null : wholeWord.Replacement;

            // Multi-word selection: apply every suggestion last-to-first so the
            // offsets computed against the original text stay valid.
            var sb = new StringBuilder(text);
            foreach (var m in matches.OrderByDescending(m => m.Offset))
            {
                sb.Remove(m.Offset, m.Length);
                sb.Insert(m.Offset, m.Replacement);
            }
            string result = sb.ToString();
            return result == text ? null : result;
        }

        /// <summary>
        /// Extracts (offset, length, best replacement) for each issue that has at
        /// least one suggestion. The first replacement is LanguageTool's
        /// best-ranked suggestion.
        /// </summary>
        private static List<(int Offset, int Length, string Replacement)> ParseMatches(string json)
        {
            var list = new List<(int Offset, int Length, string Replacement)>();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("matches", out JsonElement matches) || matches.GetArrayLength() == 0)
                return list;

            foreach (JsonElement match in matches.EnumerateArray())
            {
                if (!match.TryGetProperty("replacements", out JsonElement replacements) || replacements.GetArrayLength() == 0)
                    continue;

                string? best = replacements[0].TryGetProperty("value", out JsonElement value)
                    ? value.GetString()
                    : null;
                if (string.IsNullOrEmpty(best)
                    || !match.TryGetProperty("offset", out JsonElement offset)
                    || !match.TryGetProperty("length", out JsonElement length))
                    continue;

                list.Add((offset.GetInt32(), length.GetInt32(), best));
            }

            return list;
        }
    }
}
