using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClipboardManager.Mac.Platform;
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

            var stackItem = new NativeMenuItem("Paste Stack: (Inactive)");
            stackItem.IsVisible = false;
            stackItem.Click += (_, _) => _mainWindow.PasteStack.Advance();

            var startupItem = new NativeMenuItem("Launch at Login")
            {
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = MacStartupRegistration.IsEnabled()
            };
            startupItem.Click += (_, _) =>
            {
                bool newState = !startupItem.IsChecked;
                if (MacStartupRegistration.SetEnabled(newState))
                {
                    startupItem.IsChecked = newState;
                }
            };

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
            menu.Items.Add(stackItem);
            menu.Items.Add(startupItem);
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

            _mainWindow.PasteStack.StatusChanged += status =>
            {
                if (status is not null)
                {
                    stackItem.Header = status;
                    stackItem.IsVisible = true;
                    trayIcon.ToolTipText = status;
                }
                else
                {
                    stackItem.IsVisible = false;
                    trayIcon.ToolTipText = "Advanced Clipboard Manager";
                }
            };

            var trayIcons = new TrayIcons { trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
