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

            List<(int Offset, int Length, List<string> Replacements)> matches = FetchMatches(text);
            if (matches.Count == 0)
                return null;

            // Single word: prefer the suggestion that covers the whole word —
            // that's the correction for the word itself, not a sub-issue.
            var wholeWord = matches.FirstOrDefault(m => m.Offset == 0 && m.Length == text.Length);
            if (wholeWord.Replacements != null && wholeWord.Replacements.Count > 0)
                return wholeWord.Replacements[0] == text ? null : wholeWord.Replacements[0];

            // Multi-word selection: apply every suggestion last-to-first so the
            // offsets computed against the original text stay valid.
            var sb = new StringBuilder(text);
            foreach (var m in matches.OrderByDescending(m => m.Offset))
            {
                sb.Remove(m.Offset, m.Length);
                sb.Insert(m.Offset, m.Replacements[0]);
            }
            string result = sb.ToString();
            return result == text ? null : result;
        }

        /// <summary>
        /// Returns the candidate corrections LanguageTool ranked for the match that
        /// covers <paramref name="word"/> entirely, in the API's ranking order
        /// (best first), deduplicated and without the word itself. Returns an empty
        /// list when the word is correct, is part of a larger match, or the API is
        /// unreachable.
        /// </summary>
        public static List<string> GetSuggestions(string word)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(word)) return result;

            List<(int Offset, int Length, List<string> Replacements)> matches = FetchMatches(word);
            var wholeWord = matches.FirstOrDefault(m => m.Offset == 0 && m.Length == word.Length);
            if (wholeWord.Replacements == null)
                return result;

            foreach (string s in wholeWord.Replacements)
            {
                if (s != word && !result.Contains(s, StringComparer.Ordinal))
                    result.Add(s);
            }
            return result;
        }

        /// <summary>
        /// Posts <paramref name="text"/> to the LanguageTool API and returns every
        /// issue's (offset, length, ranked replacements). The language is chosen
        /// from the dominant script of the text ("fa" for Persian, "en-US" for
        /// English). Returns an empty list on network/API errors.
        /// </summary>
        private static List<(int Offset, int Length, List<string> Replacements)> FetchMatches(string text)
        {
            var list = new List<(int Offset, int Length, List<string> Replacements)>();

            string language = KeyboardMapper.IsMostlyPersian(text) ? "fa" : "en-US";
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

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("matches", out JsonElement matches) || matches.GetArrayLength() == 0)
                    return list;

                foreach (JsonElement match in matches.EnumerateArray())
                {
                    if (!match.TryGetProperty("offset", out JsonElement offset)
                        || !match.TryGetProperty("length", out JsonElement length))
                        continue;

                    var replacements = new List<string>();
                    if (match.TryGetProperty("replacements", out JsonElement replArray))
                    {
                        foreach (JsonElement r in replArray.EnumerateArray())
                        {
                            if (r.TryGetProperty("value", out JsonElement value)
                                && value.GetString() is string s && !string.IsNullOrEmpty(s)
                                && !replacements.Contains(s, StringComparer.Ordinal))
                                replacements.Add(s);
                        }
                    }

                    if (replacements.Count > 0)
                        list.Add((offset.GetInt32(), length.GetInt32(), replacements));
                }
            }
            catch
            {
                // offline / API error → no suggestions
            }

            return list;
        }
    }
}
