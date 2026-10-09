
using System;
using System.Windows;

namespace WpfWebViewBrowser
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show(
                    args.Exception.Message,
                    "Ошибка приложения",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                MessageBox.Show(
                    args.ExceptionObject?.ToString() ?? "Неизвестная ошибка",
                    "Критическая ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };

            base.OnStartup(e);
        }
    }
}