using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.ComponentModel;
using System.IO;

namespace FileExplorerUI
{
    public sealed class LocalizedStrings : INotifyPropertyChanged
    {
        private static LocalizedStrings? _instance;
        private readonly ResourceManager _resourceManager;
        private readonly ResourceMap _resourceMap;
        private readonly ResourceContext _resourceContext;
        private string _currentLanguageTag;

        public static LocalizedStrings Instance => _instance ??= new LocalizedStrings();

        public event PropertyChangedEventHandler? PropertyChanged;

        public string this[string key] => Get(key);

        public string DebugLanguageButtonText =>
            _currentLanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "EN" : "中";

        public string CurrentLanguageTag => _currentLanguageTag;

        public LocalizedStrings()
        {
            _instance ??= this;
            string? priPath = ResolvePriPath();
            _resourceManager = string.IsNullOrWhiteSpace(priPath)
                ? new ResourceManager()
                : new ResourceManager(priPath);
            _resourceMap = _resourceManager.MainResourceMap.GetSubtree("Resources");
            _resourceContext = _resourceManager.CreateResourceContext();
            _currentLanguageTag = ResolveInitialLanguageTag();
            _resourceContext.QualifierValues["Language"] = _currentLanguageTag;
        }

        public string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            try
            {
                string? value = TryGetString(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }

                if (key.Contains('.', StringComparison.Ordinal))
                {
                    value = TryGetString(key.Replace('.', '/'));
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                return key;
            }
            catch
            {
                return key;
            }
        }

        private string? TryGetString(string key)
        {
            ResourceCandidate candidate = _resourceMap.GetValue(key, _resourceContext);
            return candidate.ValueAsString;
        }

        public string ToggleDebugLanguage()
        {
            string nextLanguage = _currentLanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? "en-US"
                : "zh-CN";
            SetLanguage(nextLanguage);
            return _currentLanguageTag;
        }

        public void SetLanguage(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag) ||
                string.Equals(_currentLanguageTag, languageTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentLanguageTag = languageTag;
            _resourceContext.QualifierValues["Language"] = languageTag;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DebugLanguageButtonText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguageTag)));
        }

        private static string? ResolvePriPath()
        {
            string baseDirectory = AppContext.BaseDirectory;
            string assemblyName = typeof(LocalizedStrings).Assembly.GetName().Name ?? "Application";
            string[] candidates =
            [
                Path.Combine(baseDirectory, $"{assemblyName}.pri"),
                Path.Combine(baseDirectory, "resources.pri")
            ];

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string ResolveInitialLanguageTag()
        {
            string current = System.Globalization.CultureInfo.CurrentUICulture.Name;
            if (current.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-CN";
            }

            return "en-US";
        }
    }
}
