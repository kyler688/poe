using PoESkillTree.Localization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PoESkillTree.Utils.UrlProcessing
{
    /// <summary>
    /// Converts redirects and short links into direct builders and planners links.
    /// </summary>
    public class SkillTreeUrlNormalizer
    {
        private readonly Regex _prefixRegex = new Regex(@"(?<toReplace>^(http(s|):\/\/|)(www\.|)(?<hostname>.*?))(\/|\?|#)");
        private readonly Regex _poeUrlRegex = new Regex(@"(http(s|):\/\/)(www\.|)poeurl\.com\/");

        private readonly Dictionary<string, string> _urlCompletionMap = new Dictionary<string, string>
        {
            // If supports both http and https - prefer secured
            { "goo.gl", "https://goo.gl" },
            { "poeurl.com", "http://poeurl.com" },
            { "tinyurl.com", "https://tinyurl.com" },
            { "pathofexile.com", "https://pathofexile.com" },
            { "br.pathofexile.com", "https://br.pathofexile.com" },
            { "ru.pathofexile.com", "https://ru.pathofexile.com" }
        };

        private readonly Func<string, HttpCompletionOption, Task<HttpResponseMessage>> _getResponseAsync;

        public SkillTreeUrlNormalizer() : this(new HttpClient().GetAsync)
        {
        }

        public SkillTreeUrlNormalizer(Func<string, HttpCompletionOption, Task<HttpResponseMessage>> getResponseAsync) => _getResponseAsync = getResponseAsync;

        /// <summary>
        /// Creates link to official pathofexile.com builder.
        /// Extracts build url from google link if needed, resolves shortened urls and removes unused query parameters.
        /// </summary>
        public async Task<string> NormalizeAsync(string buildUrl, Func<string, Task<HttpResponseMessage>, Task<HttpResponseMessage>> loadingWrapper)
        {
            buildUrl = Regex.Replace(buildUrl, @"\s", "");

            while (true)
            {
                if (buildUrl.Contains("google.com"))
                {
                    buildUrl = ExtractUrlFromQuery(buildUrl, "q");
                    continue;
                }

                if (buildUrl.Contains("tinyurl.com") || buildUrl.Contains("poeurl.com") || buildUrl.Contains("goo.gl"))
                {
                    buildUrl = await ResolveShortenedUrl(buildUrl, loadingWrapper);
                    continue;
                }

                break;
            }

            buildUrl = WebUtility.UrlDecode(buildUrl);

            return EnsureProtocol(buildUrl);
        }

        private static string ExtractUrlFromQuery(string url, string parameterName)
        {
            var match = Regex.Match(url, $@"{parameterName}=(?<urlParam>.*?)(&|$)");
            if (!match.Success)
            {
                throw new ArgumentException($"The URL doesn't contain required query parameter '{parameterName}'."); // internal exception
            }

            return match.Groups["urlParam"].Value;
        }

        private async Task<string> ResolveShortenedUrl(string buildUrl, Func<string, Task<HttpResponseMessage>, Task<HttpResponseMessage>> loadingWrapper)
        {
            buildUrl = EnsureProtocol(buildUrl);

            if (_poeUrlRegex.IsMatch(buildUrl))
            {
                buildUrl = buildUrl.Replace("preview.", "");
                if (!buildUrl.Contains("redirect.php"))
                {
                    buildUrl = _poeUrlRegex.Replace(buildUrl, "http://poeurl.com/redirect.php?url=");
                }
            }

            var response = await loadingWrapper(L10n.Message("Resolving shortened tree address"),
                _getResponseAsync(buildUrl, HttpCompletionOption.ResponseHeadersRead));

            response.EnsureSuccessStatusCode();

            return response.RequestMessage.RequestUri.ToString();
        }

        private string EnsureProtocol(string buildUrl)
        {
            var match = _prefixRegex.Match(buildUrl);

            if (!match.Success)
            {
                return buildUrl;
            }

            if (_urlCompletionMap.TryGetValue(match.Groups["hostname"].Value, out var completion))
            {
                return buildUrl.Replace(match.Groups["toReplace"].Value, completion);
            }

            return buildUrl;
        }
    }
}