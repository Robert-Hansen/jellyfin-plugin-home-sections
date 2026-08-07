using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class TranslationManager : ITranslationManager
    {
        private Dictionary<string, JObject> m_translationPacks = new Dictionary<string, JObject>();
        private readonly ILogger<ITranslationManager> m_logger;

        public TranslationManager(ILogger<ITranslationManager> logger)
        {
            m_logger = logger;
        }

        public void Initialize()
        {
            m_logger.LogTrace("Loading translation files");
            if (m_logger.IsEnabled(LogLevel.Trace))
            {
                string resources = string.Join(
                    ',',
                    HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceNames());
                PluginLog.AvailableResources(m_logger, resources);
            }
            
            // Get all the json files from the embedded resources
            string[] locJsonFiles = HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceNames()
                .Where(x => x.EndsWith(".json") && x.Contains("_Localization.")).ToArray();

            foreach (string locFile in locJsonFiles)
            {
                PluginLog.LoadingTranslationFile(m_logger, locFile);
                using Stream? locStream = HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceStream(locFile);

                if (locStream != null)
                {
                    using TextReader reader = new StreamReader(locStream);

                    string key = locFile.Replace(".json", "").Split('.').Last();

                    if (!m_translationPacks.ContainsKey(key))
                    {
                        m_translationPacks.Add(key, JObject.Parse(reader.ReadToEnd()));
                        PluginLog.LoadedTranslationFile(m_logger, locFile, m_translationPacks[key].Count);
                    }
                    else
                    {
                        PluginLog.TranslationFileAlreadyLoaded(m_logger, locFile);
                    }
                }
            }
        }

        public string Translate(string key, string desiredLanguage, string fallbackText, TranslationMetadata? metadata = null)
        {
            PluginLog.TranslatingKey(m_logger, key, desiredLanguage);
            
            bool languageFound = false;
            string languageKey = desiredLanguage;

            do
            {
                // If we don't have the language, but it has a region remove the region and just grab the language and see if we 
                // have a blanket translation for that language.
                if (!m_translationPacks.ContainsKey(languageKey) && languageKey.Contains("-"))
                {
                    PluginLog.LanguageMissingRemoveRegion(m_logger, languageKey);
                    languageKey = languageKey.Split("-")[0];
                }
                // If we don't then fallback to english so we don't get keys being sent to the client
                else if (!m_translationPacks.ContainsKey(languageKey))
                {
                    PluginLog.LanguageMissingFallbackEnglish(m_logger, languageKey);
                    languageKey = "en";
                }
                // If we have it then we're done.
                else if (m_translationPacks.ContainsKey(languageKey))
                {
                    PluginLog.FoundTranslationPack(m_logger, languageKey);
                    languageFound = true;
                }
            } while (!languageFound);

            JObject translationPack = m_translationPacks[languageKey];

            string translatedText = "";
            string fullTextKey = fallbackText.Replace(" ", "").Replace("-", "");
            if (key != fullTextKey && translationPack.ContainsKey(fullTextKey))
            {
                PluginLog.FoundFullTextTranslation(m_logger, fullTextKey, languageKey);
                translatedText = translationPack.Value<string>(fullTextKey)!;
                
                // Since we've got a full translation we don't need the metadata
                metadata = null;
            }
            else if (translationPack.ContainsKey(key))
            {
                PluginLog.FoundKeyTranslation(m_logger, key, languageKey);
                translatedText = translationPack.Value<string>(key)!;
            }
            else
            {
                PluginLog.NoTranslationFound(m_logger, key, languageKey);
                // If Libre is disabled this will be null
                string? libreTranslateVersion = LibreTranslateHelper.TranslateAsync(fallbackText, "en", desiredLanguage).GetAwaiter().GetResult();
                
                translatedText = libreTranslateVersion ?? m_translationPacks["en"].Value<string>(key) ?? fallbackText;
            }

            if (metadata != null)
            {
                PluginLog.ApplyingTranslationMetadata(m_logger, translatedText);

                string? additionalContent = metadata.AdditionalContent;
                if (metadata.TranslateAdditionalContent && !string.IsNullOrEmpty(additionalContent))
                {
                    additionalContent = Translate(additionalContent.Replace(" ", "").Replace("-", ""), desiredLanguage, additionalContent);
                }
                
                if (metadata.Type == TranslationType.Prefix)
                {
                    translatedText = $"{translatedText} {additionalContent}".Trim();
                }
                else if (metadata.Type == TranslationType.Suffix)
                {
                    translatedText = $"{additionalContent} {translatedText}".Trim();
                }
                else if (metadata.Type == TranslationType.Pattern)
                {
                    translatedText = translatedText.Replace("{0}", additionalContent);
                }
                
                PluginLog.AppliedTranslationMetadata(m_logger, translatedText);
            }
            
            return translatedText;
        }

        public void UpdateTranslationPack(string language, JObject translationPack)
        {
            m_translationPacks[language] = translationPack;
        }

        public IDictionary<string, string>? GetTranslationPack(string language)
        {
            string languageKey = language;

            if (!m_translationPacks.ContainsKey(languageKey) && languageKey.Contains("-"))
            {
                languageKey = languageKey.Split("-")[0];
            }

            if (!m_translationPacks.ContainsKey(languageKey))
            {
                languageKey = "en";
            }

            if (m_translationPacks.TryGetValue(languageKey, out JObject? pack))
            {
                return pack.ToObject<Dictionary<string, string>>();
            }

            return null;
        }
    }
}
