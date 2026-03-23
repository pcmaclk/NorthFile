using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.Globalization;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FileExplorerUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private readonly List<Window> _windows = new();

        public MainWindow CreateWindow(string? initialPath = null)
        {
            var window = new MainWindow(initialPath);
            window.Closed += Window_Closed;
            _windows.Add(window);
            window.Activate();
            return window;
        }

        public void SetMainWindow(Window window)
        {
            if (!_windows.Contains(window))
            {
                _windows.Add(window);
            }
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            this.UnhandledException += App_UnhandledException;
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"[UNHANDLED] {e.Exception}");
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
#if DEBUG
            ApplyLaunchArguments(args.Arguments);
#endif
            CreateWindow();
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            if (sender is Window window)
            {
                window.Closed -= Window_Closed;
                _windows.Remove(window);
            }
        }

        private static void ApplyLaunchArguments(string arguments)
        {
            List<string> parts = new();
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                parts.AddRange(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            string[] commandLineArgs = Environment.GetCommandLineArgs();
            if (commandLineArgs.Length > 1)
            {
                parts.AddRange(commandLineArgs.Skip(1));
            }

            string? language = ResolveLaunchLanguageTag(parts);
            if (string.IsNullOrWhiteSpace(language))
            {
                return;
            }

            ApplyDebugLanguageOverride(language);
            LocalizedStrings.Instance.SetLanguage(language);
        }

        private static void ApplyDebugLanguageOverride(string? languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return;
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(languageTag);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                ApplicationLanguages.PrimaryLanguageOverride = languageTag;
            }
            catch (CultureNotFoundException)
            {
            }
        }

        private static string? ResolveLaunchLanguageTag(IEnumerable<string> parts)
        {
            const string languagePrefix = "--lang=";
            foreach (string part in parts)
            {
                if (part.StartsWith(languagePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string language = part[languagePrefix.Length..].Trim('"');
                    if (!string.IsNullOrWhiteSpace(language))
                    {
                        return language;
                    }
                }
            }

            return null;
        }
    }
}
