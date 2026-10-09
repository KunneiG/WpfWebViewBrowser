
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfWebViewBrowser
{
    public partial class MainWindow : Window
    {
        private const string HomeUrl = "https://example.com";

        private int currentDisplayMode;
        private bool webViewInitialized;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializeWebViewAsync();

                DisplayModeComboBox.SelectedIndex = 0;
                await NavigateAsync(HomeUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось запустить WebView2.\n\n" + ex.Message,
                    "Ошибка запуска",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task InitializeWebViewAsync()
        {
            string cachePath = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "WpfWebViewBrowser",
                "WebView2Cache");

            try
            {
                Directory.CreateDirectory(cachePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось создать папку кэша:\n" + ex.Message,
                    "Ошибка кэша",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                throw;
            }

            var environment =
                await CoreWebView2Environment.CreateAsync(
                    userDataFolder: cachePath);

            await Browser.EnsureCoreWebView2Async(environment);

            var core = Browser.CoreWebView2;

            core.NavigationStarting += (_, args) =>
            {
                AddressBar.Text = args.Uri;
            };

            core.NavigationCompleted += Core_NavigationCompleted;
            core.NewWindowRequested += Core_NewWindowRequested;
            core.ProcessFailed += (_, args) =>
            {
                MessageBox.Show(
                    "Ошибка процесса WebView2: " +
                    args.ProcessFailedKind,
                    "Ошибка браузера",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            };

            

            webViewInitialized = true;
        }

        private async void Core_NavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                MessageBox.Show(
                    "Не удалось загрузить страницу: " +
                    e.WebErrorStatus,
                    "Ошибка навигации",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            AddressBar.Text = Browser.Source?.ToString() ?? "";

            try
            {
                await ApplyDisplayModeAsync();
                await InstallWindowOpenOverrideAsync();
                await NumberLinksAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка обработки страницы:\n" + ex.Message,
                    "Ошибка JavaScript",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // ---------------- НАВИГАЦИЯ ----------------

        private async Task NavigateAsync(string input)
        {
            if (!webViewInitialized)
                return;

            input = input.Trim();

            if (string.IsNullOrWhiteSpace(input))
                return;

            string url;

            if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp
                    || uri.Scheme == Uri.UriSchemeHttps))
            {
                url = uri.AbsoluteUri;
            }
            else if (!input.Contains(' ')
                     && Uri.TryCreate(
                         "https://" + input,
                         UriKind.Absolute,
                         out Uri? hostUri)
                     && (hostUri.Scheme == Uri.UriSchemeHttps))
            {
                url = hostUri.AbsoluteUri;
            }
            else
            {
                url = "https://www.google.com/search?q=" +
                      Uri.EscapeDataString(input);
            }

            try
            {
                Browser.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка перехода:\n" + ex.Message,
                    "Навигация",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            await Task.CompletedTask;
        }

        private async void NavigateButton_Click(
            object sender, RoutedEventArgs e)
        {
            await NavigateAsync(AddressBar.Text);
        }

        private async void AddressBar_KeyDown(
            object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await NavigateAsync(AddressBar.Text);
                e.Handled = true;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (webViewInitialized && Browser.CanGoBack)
                Browser.GoBack();
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (webViewInitialized && Browser.CanGoForward)
                Browser.GoForward();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (webViewInitialized)
                Browser.Reload();
        }

        private async void HomeButton_Click(
            object sender, RoutedEventArgs e)
        {
            await NavigateAsync(HomeUrl);
        }

        // ---------------- РЕЖИМЫ ОТОБРАЖЕНИЯ ----------------

        private async void DisplayModeComboBox_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            if (!webViewInitialized ||
                DisplayModeComboBox.SelectedIndex < 0)
                return;

            currentDisplayMode = DisplayModeComboBox.SelectedIndex;

            await ApplyDisplayModeAsync();
            await NumberLinksAsync();
        }

        private async Task ApplyDisplayModeAsync()
        {
            string css = currentDisplayMode switch
            {
                // Только текст
                0 => """
                    img, picture, video, audio, iframe,
                    canvas, svg, object, embed {
                        display: none !important;
                    }
                    """,

                // Текст + изображения
                1 => """
                    video, audio, iframe, object, embed {
                        display: none !important;
                    }
                    """,

                // Текст + видео
                2 => """
                    img, picture, iframe, object, embed {
                        display: none !important;
                    }
                    """,

                _ => ""
            };

            string cssJson = JsonSerializer.Serialize(css);

            string script = """
                (() => {
                    let style = document.getElementById('wpf-mode-style');

                    if (!style) {
                        style = document.createElement('style');
                        style.id = 'wpf-mode-style';
                        (document.head || document.documentElement)
                            .appendChild(style);
                    }

                    style.textContent = CSS_VALUE;
                })();
                """.Replace("CSS_VALUE", cssJson);

            await ExecuteJavaScriptAsync(script);
        }

        // ---------------- НУМЕРАЦИЯ ССЫЛОК ----------------

        private async Task NumberLinksAsync()
        {
            string script = """
                (() => {
                    document.querySelectorAll('.wpf-link-number')
                        .forEach(el => el.remove());

                    const links = Array.from(
                        document.querySelectorAll('a[href]')
                    );

                    let number = 0;

                    for (const link of links) {
                        const rect = link.getBoundingClientRect();
                        const style = getComputedStyle(link);

                        if (rect.width <= 0 || rect.height <= 0 ||
                            style.display === 'none' ||
                            style.visibility === 'hidden')
                            continue;

                        number++;

                        link.dataset.wpfLinkNumber = String(number);

                        
                    }

                    if (!document.body.dataset.wpfFocus)
                        document.body.dataset.wpfFocus = '0';
                })();
                """;

            await ExecuteJavaScriptAsync(script);
        }

        // ---------------- ФОКУС ССЫЛОК Q / E ----------------

        private async Task MoveFocusAsync(int direction)
        {
            string script = $$"""
                (() => {
                    const links = Array.from(
                        document.querySelectorAll(
                            'a[data-wpf-link-number]'
                        )
                    );

                    if (!links.length)
                        return;

                    let current = Number(
                        document.body.dataset.wpfFocus || '0'
                    );

                    current += {{direction}};

                    if (current < 1)
                        current = links.length;

                    if (current > links.length)
                        current = 1;

                    document.body.dataset.wpfFocus = String(current);

                    for (const link of links) {
                        link.style.outline = '';
                        link.style.backgroundColor = '';
                    }

                    const selected = links.find(
                        link => Number(link.dataset.wpfLinkNumber)
                            === current
                    );

                    if (selected) {
                        selected.style.outline = '3px solid red';
                        selected.style.backgroundColor =
                            'rgba(255,255,0,0.25)';

                        selected.scrollIntoView({
                            behavior: 'smooth',
                            block: 'center'
                        });
                    }
                })();
                """;

            await ExecuteJavaScriptAsync(script);
        }

        // ---------------- ENTER И ЦИФРЫ 1-9 ----------------

        private async Task ActivateFocusedLinkAsync()
        {
            string script = """
                (() => {
                    const current = Number(
                        document.body.dataset.wpfFocus || '0'
                    );

                    const link = Array.from(
                        document.querySelectorAll(
                            'a[data-wpf-link-number]'
                        )
                    ).find(
                        el => Number(el.dataset.wpfLinkNumber) === current
                    );

                    if (link)
                        link.click();
                })();
                """;

            await ExecuteJavaScriptAsync(script);
        }

        private async Task ActivateNumberedLinkAsync(int number)
        {
            string script = $$"""
                (() => {
                    const link = Array.from(
                        document.querySelectorAll(
                            'a[data-wpf-link-number]'
                        )
                    ).find(
                        el => Number(el.dataset.wpfLinkNumber)
                            === {{number}}
                    );

                    if (link)
                        link.click();
                })();
                """;

            await ExecuteJavaScriptAsync(script);
        }

        // ---------------- ОТКРЫТИЕ В ТЕКУЩЕМ ОКНЕ ----------------

        private void Core_NewWindowRequested(
            object? sender,
            CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;

            if (!string.IsNullOrWhiteSpace(e.Uri))
            {
                _ = NavigateAsync(e.Uri);
            }
        }

        private async Task InstallWindowOpenOverrideAsync()
        {
            string script = """
                (() => {
                    if (window.__wpfOpenInstalled)
                        return;

                    window.__wpfOpenInstalled = true;

                    window.open = function(url) {
                        if (url) {
                            window.location.href =
                                new URL(url, document.baseURI).href;
                        }

                        return window;
                    };
                })();
                """;

            await ExecuteJavaScriptAsync(script);
        }

        // ---------------- КЛАВИАТУРА WEBVIEW2 ----------------


        

        private async Task<bool> IsPageInputFocusedAsync()
        {
            try
            {
                string result = await Browser.CoreWebView2.ExecuteScriptAsync(
                    """
                    (() => {
                        const el = document.activeElement;

                        return !!el && (
                            el.isContentEditable ||
                            ['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName)
                        );
                    })();
                    """);

                return result == "true";
            }
            catch
            {
                return false;
            }
        }

        private async Task ExecuteJavaScriptAsync(string script)
        {
            if (!webViewInitialized)
                return;

            try
            {
                await Browser.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "JavaScript error: " + ex.Message);
            }
        }
    }
}