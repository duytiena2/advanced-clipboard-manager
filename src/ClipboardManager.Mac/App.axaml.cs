using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClipboardManager.Mac.Views;

namespace ClipboardManager.Mac;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;

            var menu = new NativeMenu();
            var openItem = new NativeMenuItem("Quick Paste");
            openItem.Click += (_, _) =>
            {
                _mainWindow.Show();
                _mainWindow.Activate();
            };

            var quitItem = new NativeMenuItem("Quit");
            quitItem.Click += (_, _) => desktop.Shutdown();

            menu.Items.Add(openItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quitItem);

            var trayIcon = new TrayIcon
            {
                ToolTipText = "Advanced Clipboard Manager",
                IsVisible = true,
                Menu = menu
            };
            trayIcon.Clicked += (_, _) =>
            {
                _mainWindow.Show();
                _mainWindow.Activate();
            };

            var trayIcons = new TrayIcons { trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
