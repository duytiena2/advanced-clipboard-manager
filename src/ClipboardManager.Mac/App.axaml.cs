using System;
using System.Diagnostics;
using System.IO;
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

            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "ClipboardManager");

            var menu = new NativeMenu();

            var openItem = new NativeMenuItem("Quick Paste");
            openItem.Click += (_, _) => _mainWindow.ToggleWindow();

            var clearItem = new NativeMenuItem("Clear History (Keep Pinned)");
            clearItem.Click += (_, _) => _mainWindow.ClearHistory();

            var folderItem = new NativeMenuItem("Open Data Folder…");
            folderItem.Click += (_, _) =>
            {
                try
                {
                    if (OperatingSystem.IsMacOS())
                    {
                        Process.Start("open", dataFolder);
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo(dataFolder) { UseShellExecute = true });
                    }
                }
                catch { }
            };

            var quitItem = new NativeMenuItem("Quit");
            quitItem.Click += (_, _) => desktop.Shutdown();

            menu.Items.Add(openItem);
            menu.Items.Add(clearItem);
            menu.Items.Add(folderItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quitItem);

            var trayIcon = new TrayIcon
            {
                ToolTipText = "Advanced Clipboard Manager",
                IsVisible = true,
                Menu = menu
            };
            trayIcon.Clicked += (_, _) => _mainWindow.ToggleWindow();

            var trayIcons = new TrayIcons { trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
