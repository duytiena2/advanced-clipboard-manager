using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using ClipboardManager.App.Native;
using ClipboardManager.App.Platform;
using ClipboardManager.Core.Classification;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Search;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.UI;

public partial class QuickPasteWindow : Window
{
    private const int MaxResults = 300;

    private readonly ClipboardService _svc;
    private readonly IClipboardWriter _writer;
    private readonly WindowsPasteSimulator _paste;
    private readonly ObservableCollection<ItemViewModel> _items = new();
    private readonly ObservableCollection<SearchFilterChip> _activeChips = new();
    private bool _isUpdatingSearchBoxText;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _trackForeground;
    private readonly DispatcherTimer _saveSizeDebounce;
    private bool _resizingFromCode;
    private bool _layoutIsCompact;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private bool _isLiveResizing;

    private bool KeepOpen => KeepOpenButton.IsChecked == true;
    private bool IsPinnedFloating => KeepOpenButton.IsChecked == true;
    private const double FullLayoutBreakpoint = 540;
    private bool IsCompact => (ActualWidth > 0 ? ActualWidth : (!double.IsNaN(Width) && Width > 0 ? Width : TargetWidth)) < FullLayoutBreakpoint;
    private const double PaletteWidth = 760, PaletteHeight = 520;
    private const double PinnedWidth = 360, PinnedHeight = 440;
    private const double MinPinnedWidth = 280, MinPinnedHeight = 220;
    private const double MinPaletteWidth = 540, MinPaletteHeight = 360;

    private double TargetWidth => IsPinnedFloating
        ? (_svc.Settings.QuickPastePinnedWidth ?? PinnedWidth)
        : (_svc.Settings.QuickPasteWidth ?? PaletteWidth);

    private double TargetHeight => IsPinnedFloating
        ? (_svc.Settings.QuickPastePinnedHeight ?? PinnedHeight)
        : (_svc.Settings.QuickPasteHeight ?? PaletteHeight);

    private bool _hiding;
    private long _markSequence;
    private readonly HashSet<long> _onDemandScanned = new();
    private readonly HashSet<long> _scanningItems = new();

    internal QuickPasteWindow(ClipboardService svc, IClipboardWriter writer, WindowsPasteSimulator paste)
    {
        _svc = svc;
        _writer = writer;
        _paste = paste;
        InitializeComponent();

        ItemsList.ItemsSource = _items;
        // Keep-open mode follows the app the user works in, so Enter pastes there.
        _trackForeground = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _trackForeground.Tick += (_, _) => _paste.RememberForegroundWindow(onlyIfPasteTarget: true);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };

        // Live countdown for sensitive items while the palette is open.
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) =>
        {
            foreach (var vm in _items)
            {
                if (vm.Item.IsSensitive) vm.Refresh();
            }
            if (ItemsList.SelectedItem is ItemViewModel sel && sel.IsSensitive) MetaExpires.Text = sel.MetaExpires;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _tick.Start(); else _tick.Stop();
            if (IsVisible && KeepOpen) _trackForeground.Start(); else _trackForeground.Stop();
        };

        FilterChipsItemsControl.ItemsSource = _activeChips;
        _activeChips.CollectionChanged += (_, _) =>
        {
            UpdateSearchChipsState();
        };

        SearchBox.TextChanged += OnSearchBoxTextChanged;

        SearchBox.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (!SearchSuggestionsPopup.IsOpen)
            {
                RefreshSuggestionsPopup();
                SearchSuggestionsPopup.IsOpen = true;
            }
        };

        SearchContainer.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Button)
            {
                SearchBox.Focus();
                if (!SearchSuggestionsPopup.IsOpen)
                {
                    RefreshSuggestionsPopup();
                    SearchSuggestionsPopup.IsOpen = true;
                }
            }
        };

        PreviewMouseDown += (_, e) =>
        {
            if (SearchSuggestionsPopup is not null && SearchSuggestionsPopup.IsOpen)
            {
                var originalSource = e.OriginalSource as DependencyObject;
                if (!IsDescendantOf(originalSource, SearchContainer) &&
                    !IsDescendantOf(originalSource, SearchSuggestionsPopup.Child))
                {
                    SearchSuggestionsPopup.IsOpen = false;
                }
            }
        };

        PreviewMouseWheel += (_, e) =>
        {
            if (SearchSuggestionsPopup is not null && SearchSuggestionsPopup.IsOpen)
            {
                var originalSource = e.OriginalSource as DependencyObject;
                if (!IsDescendantOf(originalSource, SearchContainer) &&
                    !IsDescendantOf(originalSource, SearchSuggestionsPopup.Child))
                {
                    SearchSuggestionsPopup.IsOpen = false;
                }
            }
        };

        ItemsList.SelectionChanged += (_, _) => UpdatePreview();
        ItemsList.MouseDoubleClick += (_, _) => PasteSelected();
        ItemsList.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (ItemsList.SelectedItem is not null) { ShowTransformMenu(); e.Handled = true; }
        };
        PreviewCode.ContextMenu = PreviewContextMenu;
        OcrTextBox.ContextMenu = PreviewContextMenu;
        PreviewContextMenu.Opened += (_, _) =>
        {
            bool hasSelection = HasPreviewSelection;
            MenuPasteSelection.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            MenuCopySelection.Header = hasSelection ? "Copy selection" : "Copy";
            MenuCopySelection.IsEnabled = hasSelection;
        };
        MenuPasteSelection.Click += (_, _) => PastePartialPreviewText();
        MenuCopySelection.Click += (_, _) => CopyPartialPreviewText();
        MenuCopyAll.Click += (_, _) => CopyAllPreviewText();
        MenuSelectAll.Click += (_, _) => SelectAllPreviewText();

        SensitiveRevealButton.Click += (_, _) => ToggleRevealSelected();
        SensitiveHideButton.Click += (_, _) => ToggleRevealSelected();
        SensitiveHideTextButton.Click += (_, _) => ToggleRevealSelected();
        ToggleRevealButton.Click += (_, _) => ToggleRevealSelected();

        UrlLaunchButton.Click += (_, _) => LaunchCurrentUrl();
        OpenUrlButton.Click += (_, _) => LaunchCurrentUrl();
        UrlCopyButton.Click += (_, _) => CopyAllPreviewText();

        ToggleOcrButton.Click += (_, _) =>
        {
            OcrTextPanel.Visibility = OcrTextPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            ToggleOcrButton.Content = OcrTextPanel.Visibility == Visibility.Visible ? "Hide OCR text" : "View OCR text";
        };
        CopyQrButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(QrTextBox.Text))
            {
                try
                {
                    _writer.Write(new ClipboardPayload { Text = QrTextBox.Text });
                    StatusText.Text = "Copied QR code";
                }
                catch (COMException) { StatusText.Text = "Clipboard is busy — try again"; }
            }
        };
        OpenQrUrlButton.Click += (_, _) =>
        {
            var url = QrTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(url) && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    if (!KeepOpen) HidePalette();
                }
                catch { }
            }
        };
        CopyOcrButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(OcrTextBox.Text))
            {
                try
                {
                    _writer.Write(new ClipboardPayload { Text = OcrTextBox.Text });
                    StatusText.Text = "Copied recognized text";
                }
                catch (COMException) { StatusText.Text = "Clipboard is busy — try again"; }
            }
        };

        FormatButton.Click += (_, _) => FormatSelectedCode();
        CopyPreviewButton.Click += (_, _) => CopyAllPreviewText();
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) =>
        {
            App.Log(new Exception($"Deactivated fired. KeepOpen={KeepOpen}, IsVisible={IsVisible}, _hiding={_hiding}"));
            if (SearchSuggestionsPopup is not null) SearchSuggestionsPopup.IsOpen = false;
            if (!KeepOpen) HidePalette();
        };

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(0),
        });

        _saveSizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveSizeDebounce.Tick += (_, _) => { _saveSizeDebounce.Stop(); SaveSettings(); };
        SizeChanged += OnWindowSizeChanged;

        // "Keep open" / pin mode: the palette stays on screen (compact when floating, docked at screen edge) and pastes into the app you used last.
        KeepOpenButton.IsChecked = _svc.Settings.QuickPasteKeepOpen;
        KeepOpenButton.Checked += (_, _) => SetKeepOpen(true);
        KeepOpenButton.Unchecked += (_, _) => SetKeepOpen(false);

        // Borderless window: drag it by the search bar or the footer (the text box itself keeps normal mouse behavior).
        Header.MouseLeftButtonDown += OnDragAreaMouseDown;
        Footer.MouseLeftButtonDown += OnDragAreaMouseDown;

        SettingsButton.Click += (_, _) => OpenSettings();
        if (CloseButton is not null) CloseButton.Click += (_, _) => HidePalette();
        if (ClearSearchButton is not null)
        {
            ClearSearchButton.Click += (_, _) =>
            {
                SearchBox.Text = string.Empty;
                _debounce.Stop();
                Reload();
                SearchBox.Focus();
            };
        }
        if (HelpButton is not null) HelpButton.Click += (_, _) => ShowKeyHelp();
        // Filter bar chips (All, Code, Images, Pinned, Snippets)
        if (ChipAll is not null) ChipAll.Click += (_, _) => SetOrToggleCategoryFilter(null, null);
        if (ChipCode is not null) ChipCode.Click += (_, _) => SetOrToggleCategoryFilter("type", "code");
        if (ChipImage is not null) ChipImage.Click += (_, _) => SetOrToggleCategoryFilter("type", "image");
        if (ChipPinned is not null) ChipPinned.Click += (_, _) => SetOrToggleCategoryFilter("pinned", "true");
        if (ChipSnippet is not null) ChipSnippet.Click += (_, _) => SetOrToggleCategoryFilter("type", "snippet");

        if (PreviewSplit is not null)
        {
            PreviewSplit.DragDelta += OnSplitterDragDelta;
            PreviewSplit.DragCompleted += OnSplitterDragCompleted;
            PreviewSplit.MouseDoubleClick += (_, _) => CycleSplitRatio();
            PreviewSplit.MouseRightButtonUp += (s, e) => { e.Handled = true; ShowSplitContextMenu(PreviewSplit); };
        }
        if (StopPasteStackButton is not null) StopPasteStackButton.Click += (_, _) => StopPasteStackRequested?.Invoke();
        if (HelpCloseButton is not null) HelpCloseButton.Click += (_, _) => HideHelpOverlay();
        if (HelpClearSearchButton is not null) HelpClearSearchButton.Click += (_, _) => { HelpSearchBox.Text = ""; HelpSearchBox.Focus(); };
        if (HelpSearchBox is not null) HelpSearchBox.TextChanged += (_, _) => FilterHelpShortcuts(HelpSearchBox.Text);
        if (DismissOnboardingButton is not null) DismissOnboardingButton.Click += DismissOnboarding_Click;
        UpdateSearchChipsState();
        ItemsList.SelectionChanged += (_, _) => UpdateContextualToolbar();
        Loaded += (_, _) => ApplyTransparency();

        _resizingFromCode = true;
        try
        {
            Width = TargetWidth;
            Height = TargetHeight;
            ApplyLayout();
            KeepOpenButton.ToolTip = IsPinnedFloating
                ? "Unpin window (Ctrl+T)"
                : "Pin window / keep open (Ctrl+T)";
        }
        finally
        {
            _resizingFromCode = false;
        }

        ApplyLocalization();
        LocalizationService.LanguageChanged += ApplyLocalization;

        Closed += (_, _) =>
        {
            LocalizationService.LanguageChanged -= ApplyLocalization;
        };

        _svc.HistoryChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsVisible) Reload(keepSelection: true);
        }));
    }

    public void ApplyLocalization()
    {
        Placeholder.Text = LocalizationService.Get("QuickPaste_SearchPlaceholder");
        if (ChipAll is not null) ChipAll.Content = LocalizationService.Get("QuickPaste_All");
        if (ChipCode is not null) ChipCode.Content = "💻 " + LocalizationService.Get("QuickPaste_Code");
        if (ChipImage is not null) ChipImage.Content = "🗃️ " + LocalizationService.Get("QuickPaste_Images");
        if (ChipPinned is not null) ChipPinned.Content = "📌 " + LocalizationService.Get("QuickPaste_Pinned");
        if (ChipSnippet is not null) ChipSnippet.Content = "🏷️ " + LocalizationService.Get("QuickPaste_Snippets");

        KeepOpenButton.ToolTip = LocalizationService.Get("QuickPaste_PinTooltip");
        SettingsButton.ToolTip = LocalizationService.Get("QuickPaste_SettingsTooltip");
        if (HelpButton is not null) HelpButton.ToolTip = LocalizationService.Get("QuickPaste_HelpTooltip");
        if (StopPasteStackButton is not null) StopPasteStackButton.Content = LocalizationService.Get("QuickPaste_StopStack");
        if (ClearAllRecentButton is not null) ClearAllRecentButton.Content = LocalizationService.Get("QuickPaste_ClearRecent");

        UpdateOnboardingBanner();
    }

    private void UpdateOnboardingBanner()
    {
        if (OnboardingBanner is null) return;
        if (_svc.Settings.OnboardingDismissed)
        {
            OnboardingBanner.Visibility = Visibility.Collapsed;
            return;
        }

        OnboardingBanner.Visibility = Visibility.Visible;
        if (OnboardingBadgeText is not null)
            OnboardingBadgeText.Text = LocalizationService.Get("Onboarding_Badge");
        if (OnboardingPrefixText is not null)
            OnboardingPrefixText.Text = LocalizationService.Get("Onboarding_TipPrefix");
        if (OnboardingSuffixText is not null)
            OnboardingSuffixText.Text = LocalizationService.Get("Onboarding_TipSuffix");
        if (DismissLabelText is not null)
            DismissLabelText.Text = LocalizationService.Get("Onboarding_Dismiss");
        if (DismissOnboardingButton is not null)
            DismissOnboardingButton.ToolTip = LocalizationService.Get("Onboarding_DismissTooltip");

        if (OnboardingKeycapsContainer is not null)
        {
            OnboardingKeycapsContainer.Children.Clear();
            var parts = (_svc.Settings.QuickPasteHotkey ?? "Ctrl+Shift+V").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    OnboardingKeycapsContainer.Children.Add(new TextBlock
                    {
                        Text = "+",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 4, 0)
                    });
                }

                var border = new Border
                {
                    Style = (Style)FindResource("OnboardingKeycap"),
                    Margin = new Thickness(0, 0, 4, 0)
                };
                border.Child = new TextBlock
                {
                    Text = parts[i],
                    Style = (Style)FindResource("OnboardingKeycapText")
                };
                OnboardingKeycapsContainer.Children.Add(border);
            }
        }
    }

    private void DismissOnboarding_Click(object sender, RoutedEventArgs e)
    {
        OnboardingBanner.Visibility = Visibility.Collapsed;
        _svc.Settings.OnboardingDismissed = true;
        try { _svc.Settings.Save(System.IO.Path.Combine(PackageInfo.DataFolder, "settings.json")); } catch { }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
        {
            source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
        }
        source?.AddHook(WndProc);
        WindowBackdrop.Apply(this, false);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ENTERSIZEMOVE)
        {
            _isLiveResizing = true;
            if (PreviewImage is not null)
            {
                RenderOptions.SetBitmapScalingMode(PreviewImage, BitmapScalingMode.LowQuality);
            }
        }
        else if (msg == WM_EXITSIZEMOVE)
        {
            _isLiveResizing = false;
            if (PreviewImage is not null)
            {
                RenderOptions.SetBitmapScalingMode(PreviewImage, BitmapScalingMode.HighQuality);
            }
            if (IsPinnedFloating)
            {
                _svc.Settings.QuickPastePinnedWidth = ActualWidth;
                _svc.Settings.QuickPastePinnedHeight = ActualHeight;
            }
            else
            {
                _svc.Settings.QuickPasteWidth = ActualWidth;
                _svc.Settings.QuickPasteHeight = ActualHeight;
            }
            _svc.Settings.QuickPasteLeft = Left;
            _svc.Settings.QuickPasteTop = Top;
            SaveSettings();
        }
        return IntPtr.Zero;
    }

    public void TogglePalette()
    {
        App.Log(new Exception($"TogglePalette: IsVisible={IsVisible}, IsActive={IsActive}, KeepOpen={KeepOpen}"));
        if (IsVisible && IsActive) HidePalette();
        else ShowPalette();
    }

    /// <param name="keepTarget">Reopening after our own dialog: keep pasting into the app that was active before.</param>
    public void ShowPalette(bool keepTarget = false, string search = "")
    {
        App.Log(new Exception($"ShowPalette: keepTarget={keepTarget}, search='{search}', Left={Left}, Top={Top}, Width={Width}, Height={Height}, IsVisible={IsVisible}"));
        if (!keepTarget) _paste.RememberForegroundWindow();
        _resizingFromCode = true;
        try
        {
            Width = TargetWidth;
            Height = TargetHeight;
            ApplyLayout();
            PositionOnScreen();
            if (!IsCompact) UpdatePreview();
        }
        finally
        {
            _resizingFromCode = false;
        }

        _activeChips.Clear();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var (extractedChips, remaining) = SearchFilterChip.ExtractFilters(search);
            foreach (var c in extractedChips) _activeChips.Add(c);
            _isUpdatingSearchBoxText = true;
            try
            {
                SearchBox.Text = remaining;
                SearchBox.CaretIndex = remaining.Length;
            }
            finally
            {
                _isUpdatingSearchBoxText = false;
            }
        }
        else
        {
            _isUpdatingSearchBoxText = true;
            try
            {
                SearchBox.Text = "";
                SearchBox.CaretIndex = 0;
            }
            finally
            {
                _isUpdatingSearchBoxText = false;
            }
        }
        if (SearchSuggestionsPopup is not null) SearchSuggestionsPopup.IsOpen = false;
        UpdateOnboardingBanner();
        Reload(keepSelection: keepTarget);
        _hiding = false;
        Show();
        Topmost = false;
        Topmost = true;
        Activate();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        NativeMethods.ForceForeground(hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void SetKeepOpen(bool on)
    {
        _svc.Settings.QuickPasteKeepOpen = on;
        SaveSettings();
        if (on && IsVisible) _trackForeground.Start(); else if (!KeepOpen) _trackForeground.Stop();

        _resizingFromCode = true;
        try
        {
            Width = on ? (_svc.Settings.QuickPastePinnedWidth ?? PinnedWidth) : (_svc.Settings.QuickPasteWidth ?? PaletteWidth);
            Height = on ? (_svc.Settings.QuickPastePinnedHeight ?? PinnedHeight) : (_svc.Settings.QuickPasteHeight ?? PaletteHeight);
            ApplyLayout();
            ClampToWorkArea();
            if (!IsCompact) UpdatePreview();
        }
        finally
        {
            _resizingFromCode = false;
        }

        KeepOpenButton.ToolTip = on ? "Unpin window (Ctrl+T)" : "Pin window / keep open (Ctrl+T)";
        StatusText.Text = on ? "Pinned — Enter pastes into the app you used last" : $"{_svc.Count()} items";
    }

    private void SaveSettings()
    {
        try { _svc.Settings.Save(Path.Combine(_svc.DataFolder, "settings.json")); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void HidePalette()
    {
        App.Log(new Exception($"HidePalette: _hiding={_hiding}, IsVisible={IsVisible}"));
        if (_hiding || !IsVisible) return;
        _hiding = true;
        if (SearchSuggestionsPopup is not null) SearchSuggestionsPopup.IsOpen = false;
        foreach (var vm in _items) vm.IsMarked = false;
        Hide();
        _hiding = false;
        FirstHideRequested?.Invoke();
    }

    /// <summary>Narrow floating (&lt; 540) = compact list only; wide floating (&gt;= 540) = full list + preview.</summary>
    private void ApplyLayout()
    {
        bool compact = IsCompact;
        _layoutIsCompact = compact;

        ResizeMode = ResizeMode.CanResize;
        MinWidth = MinPinnedWidth;
        MaxWidth = double.PositiveInfinity;
        MinHeight = MinPinnedHeight;
        MaxHeight = double.PositiveInfinity;

        if (compact)
        {
            ListColumn.MinWidth = 0;
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitColumn.MinWidth = 0;
            SplitColumn.Width = new GridLength(0);
            PreviewColumn.MinWidth = 0;
            PreviewColumn.Width = new GridLength(0);
            if (PreviewSplit is not null) PreviewSplit.Visibility = Visibility.Collapsed;
        }
        else
        {
            ListColumn.MinWidth = 180;
            SplitColumn.MinWidth = 0;
            SplitColumn.Width = new GridLength(5);
            PreviewColumn.MinWidth = 180;
            double totalAvailable = (ActualWidth > 0 ? ActualWidth : TargetWidth) - 7;
            double ratio = _svc.Settings.QuickPasteListRatio ?? 0.40;
            ratio = Math.Clamp(ratio, 0.20, 0.80);
            double listWidth = Math.Round(totalAvailable * ratio);
            listWidth = Math.Clamp(listWidth, 180, Math.Max(180, totalAvailable - 180));

            ListColumn.Width = new GridLength(listWidth, GridUnitType.Pixel);
            PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            if (PreviewSplit is not null) PreviewSplit.Visibility = Visibility.Visible;
        }
        if (RootBorder is not null)
        {
            RootBorder.CornerRadius = new CornerRadius(0);
        }
        PreviewPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        UpdateContextualToolbar();
        if (ResizeGripIndicator is not null)
            ResizeGripIndicator.Visibility = Visibility.Visible;
        Placeholder.Text = "Search clipboard…";
        Header.ToolTip = Footer.ToolTip = "Drag to move";
    }

    private static readonly (double ratio, string label, string description)[] SplitPresets =
    [
        (0.40, "40% / 60%", "Tìm kiếm nhanh • Preview nhỏ"),
        (0.50, "50% / 50%", "Cân bằng 1:1"),
        (0.30, "30% / 70%", "Mở rộng Preview"),
        (0.25, "25% / 75%", "Xem screenshot & code • Preview lớn"),
    ];

    private void SetPaneRatio(double listRatio)
    {
        if (IsCompact) return;
        listRatio = Math.Clamp(listRatio, 0.20, 0.80);
        _svc.Settings.QuickPasteListRatio = listRatio;
        double total = ListColumn.ActualWidth + PreviewColumn.ActualWidth;
        if (total <= 50) total = (ActualWidth > 0 ? ActualWidth : TargetWidth) - 7;
        double listWidth = Math.Round(total * listRatio);
        listWidth = Math.Clamp(listWidth, 180, Math.Max(180, total - 180));
        ListColumn.Width = new GridLength(listWidth, GridUnitType.Pixel);
        PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
        _saveSizeDebounce.Stop();
        _saveSizeDebounce.Start();
    }

    private void CycleSplitRatio()
    {
        if (IsCompact) return;
        double current = _svc.Settings.QuickPasteListRatio ?? 0.40;
        int idx = -1;
        for (int i = 0; i < SplitPresets.Length; i++)
        {
            if (Math.Abs(SplitPresets[i].ratio - current) < 0.04)
            {
                idx = i;
                break;
            }
        }
        int nextIdx = (idx + 1) % SplitPresets.Length;
        SetPaneRatio(SplitPresets[nextIdx].ratio);
        StatusText.Text = $"Tỷ lệ: {SplitPresets[nextIdx].label} ({SplitPresets[nextIdx].description})";
    }

    private void ShowSplitContextMenu(UIElement target)
    {
        var menu = new ContextMenu();
        double current = _svc.Settings.QuickPasteListRatio ?? 0.40;

        foreach (var (ratio, label, desc) in SplitPresets)
        {
            var item = new MenuItem
            {
                Header = $"{label}  —  {desc}",
                IsChecked = Math.Abs(ratio - current) < 0.04,
            };
            double r = ratio;
            item.Click += (_, _) =>
            {
                SetPaneRatio(r);
                StatusText.Text = $"Tỷ lệ: {label} ({desc})";
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var resetItem = new MenuItem { Header = "Mặc định (40% / 60%)" };
        resetItem.Click += (_, _) =>
        {
            SetPaneRatio(0.40);
            StatusText.Text = "Tỷ lệ: Mặc định (40% / 60%)";
        };
        menu.Items.Add(resetItem);

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void OnSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        double total = ListColumn.ActualWidth + PreviewColumn.ActualWidth;
        if (total > 50)
        {
            double ratio = ListColumn.ActualWidth / total;
            StatusText.Text = $"Tỷ lệ: {ratio:P0} / {1.0 - ratio:P0}";
        }
    }

    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (IsCompact) return;
        double total = ListColumn.ActualWidth + PreviewColumn.ActualWidth;
        if (total > 50)
        {
            double ratio = ListColumn.ActualWidth / total;
            ratio = Math.Clamp(ratio, 0.20, 0.80);
            _svc.Settings.QuickPasteListRatio = ratio;
            ListColumn.Width = new GridLength(Math.Round(ListColumn.ActualWidth), GridUnitType.Pixel);
            PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            _saveSizeDebounce.Stop();
            _saveSizeDebounce.Start();
            StatusText.Text = $"Tỷ lệ: {ratio:P0} / {1.0 - ratio:P0}";
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || _resizingFromCode) return;

        bool compact = ActualWidth < FullLayoutBreakpoint;
        if (compact != _layoutIsCompact)
        {
            ApplyLayout();
            if (!compact)
            {
                UpdatePreview();
            }
        }
        else if (!compact && ListColumn.Width.IsAbsolute)
        {
            // Khi co hẹp cửa sổ, đảm bảo PreviewColumn luôn có tối thiểu 180px
            double maxListWidth = Math.Max(180, ActualWidth - 180 - 7);
            if (ListColumn.Width.Value > maxListWidth)
            {
                ListColumn.Width = new GridLength(maxListWidth, GridUnitType.Pixel);
            }
        }

        if (IsPinnedFloating)
        {
            _svc.Settings.QuickPastePinnedWidth = ActualWidth;
            _svc.Settings.QuickPastePinnedHeight = ActualHeight;
        }
        else
        {
            _svc.Settings.QuickPasteWidth = ActualWidth;
            _svc.Settings.QuickPasteHeight = ActualHeight;
        }
        _svc.Settings.QuickPasteLeft = Left;
        _svc.Settings.QuickPasteTop = Top;

        if (!_isLiveResizing)
        {
            _saveSizeDebounce.Stop();
            _saveSizeDebounce.Start();
        }
    }

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SearchSuggestionsPopup is not null) SearchSuggestionsPopup.IsOpen = false;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return; // mouse already released
        }
        // Remember where the user put it (also across restarts).
        _svc.Settings.QuickPasteLeft = Left;
        _svc.Settings.QuickPasteTop = Top;
        SaveSettings();
        SearchBox.Focus();
    }

    private void PositionOnScreen()
    {
        var s = _svc.Settings;
        if (s.QuickPasteLeft is double left && s.QuickPasteTop is double top && IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
            ClampToWorkArea();
            return;
        }
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + Math.Max(40, (area.Height - Height) * 0.3);
    }

    private void ClampToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        if (Left + Width > area.Right) Left = Math.Max(area.Left, area.Right - Width);
        if (Top + Height > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
        if (Left < area.Left) Left = area.Left;
        if (Top < area.Top) Top = area.Top;
    }

    /// <summary>True when at least the top-left 100×40 px of the window would be visible on some monitor.</summary>
    private static bool IsOnScreen(double left, double top)
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        return left >= vl - 50 && top >= vt && left + 100 <= vl + vw && top + 40 <= vt + vh;
    }

    private void Reload(bool keepSelection = false)
    {
        long? selectedId = keepSelection && ItemsList.SelectedItem is ItemViewModel cur ? cur.Item.Id : null;
        var marks = _items.Where(i => i.IsMarked).ToDictionary(i => i.Item.Id, i => i.MarkOrder);

        string effectiveQuery = SearchFilterChip.Combine(_activeChips, SearchBox.Text);
        UpdateFilterBarActiveChips();
        var results = _svc.Search(effectiveQuery, MaxResults);
        _items.Clear();
        foreach (var item in results)
        {
            _items.Add(new ItemViewModel(item, _svc)
            {
                IsMarked = marks.ContainsKey(item.Id),
                MarkOrder = marks.GetValueOrDefault(item.Id),
                Shortcut = _items.Count < 9 ? (_items.Count + 1).ToString() : "",
            });
        }

        var toSelect = selectedId is null ? null : _items.FirstOrDefault(i => i.Item.Id == selectedId);
        ItemsList.SelectedItem = toSelect ?? _items.FirstOrDefault();
        if (ItemsList.SelectedItem is not null) ItemsList.ScrollIntoView(ItemsList.SelectedItem);

        CountText.Text = string.IsNullOrWhiteSpace(effectiveQuery) ? "" : $"{_items.Count} result{(_items.Count == 1 ? "" : "s")}";
        StatusText.Text = _svc.Settings.CaptureEnabled
            ? LocalizationService.Get("QuickPaste_ItemsCount", _svc.Count())
            : LocalizationService.Get("Tray_PauseCapture");
        if (_items.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(effectiveQuery))
            {
                if (EmptyClipboardContainer is not null) EmptyClipboardContainer.Visibility = Visibility.Visible;
                if (EmptySearchContainer is not null) EmptySearchContainer.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (EmptyClipboardContainer is not null) EmptyClipboardContainer.Visibility = Visibility.Collapsed;
                if (EmptySearchContainer is not null) EmptySearchContainer.Visibility = Visibility.Visible;
            }
            UpdatePreview();
        }
        else
        {
            if (EmptyClipboardContainer is not null) EmptyClipboardContainer.Visibility = Visibility.Collapsed;
            if (EmptySearchContainer is not null) EmptySearchContainer.Visibility = Visibility.Collapsed;
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        UpdateContextualToolbar();
        if (ItemsList.SelectedItem is not ItemViewModel vm)
        {
            PreviewHeaderBar.Visibility = Visibility.Collapsed;
            SensitiveHiddenContainer.Visibility = Visibility.Collapsed;
            SensitiveRevealedBanner.Visibility = Visibility.Collapsed;
            SensitiveRevealedTextBanner.Visibility = Visibility.Collapsed;
            ImageContainer.Visibility = Visibility.Collapsed;
            UrlContainer.Visibility = Visibility.Collapsed;
            CodeContainer.Visibility = Visibility.Collapsed;
            TextContainer.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            MetaSource.Text = MetaCopied.Text = MetaExpires.Text = "";
            MetaExpiresLabel.Visibility = MetaExpires.Visibility = Visibility.Collapsed;
            HeaderOcrBadge.Visibility = Visibility.Collapsed;
            return;
        }

        if (EmptyClipboardContainer is not null) EmptyClipboardContainer.Visibility = Visibility.Collapsed;
        if (EmptySearchContainer is not null) EmptySearchContainer.Visibility = Visibility.Collapsed;
        PreviewHeaderBar.Visibility = Visibility.Visible;

        // Reset action buttons & badges
        FormatButton.Visibility = Visibility.Collapsed;
        OpenUrlButton.Visibility = Visibility.Collapsed;
        ToggleRevealButton.Visibility = Visibility.Collapsed;
        HeaderOcrBadge.Visibility = Visibility.Collapsed;

        // Populate bottom metadata table (Source, Copied, và Expires nếu sensitive)
        MetaSource.Text = vm.MetaSource;
        MetaCopied.Text = vm.MetaCopied;
        if (vm.IsSensitive)
        {
            MetaExpiresLabel.Visibility = Visibility.Visible;
            MetaExpires.Visibility = Visibility.Visible;
            MetaExpires.Text = vm.MetaExpires;
        }
        else
        {
            MetaExpiresLabel.Visibility = Visibility.Collapsed;
            MetaExpires.Visibility = Visibility.Collapsed;
            MetaExpires.Text = "";
        }

        // Hide all view containers before showing the active one
        SensitiveHiddenContainer.Visibility = Visibility.Collapsed;
        SensitiveRevealedBanner.Visibility = Visibility.Collapsed;
        SensitiveRevealedTextBanner.Visibility = Visibility.Collapsed;
        ImageContainer.Visibility = Visibility.Collapsed;
        UrlContainer.Visibility = Visibility.Collapsed;
        CodeContainer.Visibility = Visibility.Collapsed;
        TextContainer.Visibility = Visibility.Collapsed;

        // 1. Sensitive content
        if (vm.IsSensitive)
        {
            SetPreviewBadge("🔒 SENSITIVE", "#FEF2F2", "#DC2626");

            if (!vm.Revealed)
            {
                SensitiveHiddenContainer.Visibility = Visibility.Visible;
                SensitiveSubtypeText.Text = $"{vm.SecretTypeName} detected";
                SensitiveMaskedText.Text = vm.MaskedSecret;
                SensitiveExpiryText.Text = vm.MetaExpires;
                PreviewStatsText.Text = "Hidden · Press Ctrl+R to reveal";

                ToggleRevealButton.Visibility = Visibility.Visible;
                ToggleRevealButton.Content = "Reveal (Ctrl+R)";
            }
            else
            {
                PreviewStatsText.Text = $"{vm.SecretTypeName} · Revealed";
                ToggleRevealButton.Visibility = Visibility.Visible;
                ToggleRevealButton.Content = "Hide (Ctrl+R)";

                if (vm.IsCode)
                {
                    CodeContainer.Visibility = Visibility.Visible;
                    SensitiveRevealedBanner.Visibility = Visibility.Visible;
                    PreviewCode.Document = SyntaxHighlighter.CreateDocument(vm.PreviewText, vm.Item.Subtype);
                    PreviewCode.ScrollToHome();
                }
                else
                {
                    TextContainer.Visibility = Visibility.Visible;
                    SensitiveRevealedTextBanner.Visibility = Visibility.Visible;
                    PreviewText.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
                    PreviewText.Text = vm.PreviewText;
                    PreviewText.ScrollToHome();
                }
            }
            return;
        }

        // 2. Image
        if (vm.IsImage)
        {
            ImageContainer.Visibility = Visibility.Visible;
            SetPreviewBadge("SCREENSHOT", "#F3E8FF", "#7C3AED");

            string dimStr = vm.ImageResolutionText;
            ImageDimensionsText.Text = dimStr;
            PreviewStatsText.Text = dimStr;

            PreviewImage.Source = vm.Image;
            PreviewImage.ToolTip = vm.Item.OcrText is { Length: > 0 } ocr ? "Text / QR in image (Ctrl+Shift+Enter pastes it):\n" + ocr : null;

            var (qrCode, otherOcr) = ClipboardService.ParseQrAndOcrText(vm.Item.OcrText);

            if (qrCode is not null || otherOcr is not null)
            {
                _scanningItems.Remove(vm.Item.Id);
                HeaderOcrBadge.Visibility = Visibility.Visible;

                if (qrCode is not null && otherOcr is not null)
                    HeaderOcrBadgeText.Text = "QR + Text";
                else if (qrCode is not null)
                    HeaderOcrBadgeText.Text = "QR Code";
                else
                    HeaderOcrBadgeText.Text = "OCR Text";

                if (qrCode is not null)
                {
                    QrCodePanel.Visibility = Visibility.Visible;
                    QrTextBox.Text = qrCode;
                    bool isUrl = qrCode.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || qrCode.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                    OpenQrUrlButton.Visibility = isUrl ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    QrCodePanel.Visibility = Visibility.Collapsed;
                }

                if (otherOcr is not null)
                {
                    OcrPanelBadgeText.Text = "OCR TEXT";
                    OcrPanelBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(0x18, 0x0E, 0x6B, 0x68));
                    OcrPanelBadgeText.Foreground = (Brush)FindResource("Accent");
                    OcrPanelSubtitle.Text = "Recognized text in image:";
                    CopyOcrButton.Content = "Copy Text";
                    CopyOcrButton.IsEnabled = true;
                    OcrTextPanel.Visibility = Visibility.Visible;
                    OcrTextBox.Text = otherOcr;
                }
                else
                {
                    OcrTextPanel.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                HeaderOcrBadge.Visibility = Visibility.Collapsed;
                QrCodePanel.Visibility = Visibility.Collapsed;
                bool isScanning = vm.Item.OcrText is null || _scanningItems.Contains(vm.Item.Id);
                if (isScanning)
                {
                    OcrPanelBadgeText.Text = "SCANNING";
                    OcrPanelBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
                    OcrPanelBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
                    OcrPanelSubtitle.Text = "Detecting text & QR codes...";
                    OcrTextBox.Text = "Scanning image for text / QR code...";
                    CopyOcrButton.IsEnabled = false;
                    OcrTextPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    OcrTextPanel.Visibility = Visibility.Collapsed;
                }

                if (string.IsNullOrEmpty(vm.Item.OcrText) && !_onDemandScanned.Contains(vm.Item.Id))
                {
                    _onDemandScanned.Add(vm.Item.Id);
                    _scanningItems.Add(vm.Item.Id);
                    if (Application.Current is App app)
                    {
                        app.StartOcr(new[] { vm.Item });
                    }
                }
            }
            return;
        }

        // 3. URL
        if (vm.IsUrl)
        {
            UrlContainer.Visibility = Visibility.Visible;
            SetPreviewBadge("🔗 " + vm.UrlTitle.ToUpperInvariant(), "#E6F4F1", "#0E6B68");
            PreviewStatsText.Text = vm.UrlDomain;

            var rawUrl = (vm.Item.TextContent ?? "").Trim();
            UrlFullText.Text = rawUrl;
            UrlSiteName.Text = vm.UrlTitle;
            UrlHostText.Text = vm.UrlDomain;
            OpenUrlButton.Visibility = Visibility.Visible;

            var cand = rawUrl.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + rawUrl : rawUrl;
            if (Uri.TryCreate(cand, UriKind.Absolute, out var uri))
            {
                UrlSchemeVal.Text = uri.Scheme;
                UrlHostVal.Text = uri.Host;
                UrlPathVal.Text = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
            }
            else
            {
                UrlSchemeVal.Text = "—";
                UrlHostVal.Text = vm.UrlDomain;
                UrlPathVal.Text = rawUrl;
            }
            return;
        }

        // 4. Code (SQL, JSON, XML, YAML, Shell, etc.)
        if (vm.IsCode)
        {
            CodeContainer.Visibility = Visibility.Visible;
            SetPreviewBadge(vm.CodeLanguage.ToUpperInvariant(), "#E8F2FF", "#0969DA");
            PreviewStatsText.Text = vm.CodeStats;

            if (vm.Item.Subtype.Equals("sql", StringComparison.OrdinalIgnoreCase) ||
                vm.Item.Subtype.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                FormatButton.Visibility = Visibility.Visible;
            }

            PreviewCode.Document = SyntaxHighlighter.CreateDocument(vm.PreviewText, vm.Item.Subtype);
            PreviewCode.ScrollToHome();
            return;
        }

        // 5. Plain text / Markdown / Snippet / other
        TextContainer.Visibility = Visibility.Visible;
        SetPreviewBadge(vm.TypeLabel.ToUpperInvariant(), "#F3F4F6", "#4B5563");
        if (vm.IsSnippet)
        {
            var payload = _svc.LoadPayload(vm.Item, plainText: true, SnippetContext());
            var expanded = payload?.Text ?? vm.PreviewText;
            PreviewText.Text = expanded;
            var lineCount = string.IsNullOrEmpty(expanded) ? 0 : expanded.Split('\n').Length;
            PreviewStatsText.Text = $"{expanded.Length} characters · {lineCount} lines";
            if (TemplateEngine.HasVariables(vm.Item.TextContent ?? ""))
            {
                PreviewStatsText.ToolTip = $"Template: {vm.Item.TextContent}";
            }
            else
            {
                PreviewStatsText.ToolTip = null;
            }
        }
        else
        {
            PreviewStatsText.Text = $"{vm.TextLength} characters · {vm.LineCount} lines";
            PreviewStatsText.ToolTip = null;
            PreviewText.Text = vm.PreviewText;
        }

        PreviewText.FontFamily = vm.PreviewFont;
        PreviewText.ScrollToHome();
    }

    private void SetPreviewBadge(string text, string bgHex, string fgHex)
    {
        PreviewBadgeText.Text = text;
        PreviewBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
        PreviewBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex));
    }

    private void ToggleRevealSelected()
    {
        if (ItemsList.SelectedItem is ItemViewModel r)
        {
            r.Revealed = !r.Revealed;
            UpdatePreview();
        }
    }

    private void LaunchCurrentUrl()
    {
        if (ItemsList.SelectedItem is ItemViewModel vm && !string.IsNullOrEmpty(vm.Item.TextContent))
        {
            var raw = vm.Item.TextContent.Trim();
            if (raw.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) raw = "https://" + raw;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(raw) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void FormatSelectedCode()
    {
        if (ItemsList.SelectedItem is ItemViewModel vm && !string.IsNullOrEmpty(vm.Item.TextContent))
        {
            var sub = vm.Item.Subtype.ToLowerInvariant();
            try
            {
                string formatted = sub switch
                {
                    "sql" => TextTransforms.Apply("sql", vm.Item.TextContent),
                    "json" => TextTransforms.Apply("json-pretty", vm.Item.TextContent),
                    _ => vm.Item.TextContent
                };
                PreviewCode.Document = SyntaxHighlighter.CreateDocument(formatted, sub);
                StatusText.Text = $"Formatted {vm.CodeLanguage}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Format error: {ex.Message}";
            }
        }
    }

    private bool HasPreviewSelection =>
        (PreviewText.IsKeyboardFocused && PreviewText.SelectionLength > 0) ||
        (PreviewCode.IsKeyboardFocused && !PreviewCode.Selection.IsEmpty) ||
        (UrlFullText.IsKeyboardFocused && UrlFullText.SelectionLength > 0) ||
        (OcrTextBox.IsKeyboardFocused && OcrTextBox.SelectionLength > 0);

    private string GetPreviewSelectedText()
    {
        if (PreviewText.IsKeyboardFocused && PreviewText.SelectionLength > 0)
            return PreviewText.SelectedText;
        if (PreviewCode.IsKeyboardFocused && !PreviewCode.Selection.IsEmpty)
            return PreviewCode.Selection.Text;
        if (UrlFullText.IsKeyboardFocused && UrlFullText.SelectionLength > 0)
            return UrlFullText.SelectedText;
        if (OcrTextBox.IsKeyboardFocused && OcrTextBox.SelectionLength > 0)
            return OcrTextBox.SelectedText;
        return "";
    }

    private bool IsAnyPreviewFocused =>
        PreviewText.IsKeyboardFocused || PreviewCode.IsKeyboardFocused ||
        UrlFullText.IsKeyboardFocused || OcrTextBox.IsKeyboardFocused;

    private string GetCurrentPreviewFullText()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return "";
        if (vm.IsImage) return vm.Item.OcrText ?? "";
        if (vm.IsSnippet)
        {
            var payload = _svc.LoadPayload(vm.Item, plainText: true, SnippetContext());
            return payload?.Text ?? vm.Item.TextContent ?? "";
        }
        return vm.Item.TextContent ?? "";
    }

    private void SelectAllPreviewText()
    {
        if (CodeContainer.Visibility == Visibility.Visible)
        {
            PreviewCode.Focus();
            PreviewCode.SelectAll();
        }
        else if (UrlContainer.Visibility == Visibility.Visible)
        {
            UrlFullText.Focus();
            UrlFullText.SelectAll();
        }
        else if (OcrTextPanel.Visibility == Visibility.Visible && OcrTextBox.IsKeyboardFocused)
        {
            OcrTextBox.Focus();
            OcrTextBox.SelectAll();
        }
        else
        {
            PreviewText.Focus();
            PreviewText.SelectAll();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                if (HelpOverlay is not null && HelpOverlay.Visibility == Visibility.Visible)
                {
                    HideHelpOverlay();
                    e.Handled = true;
                    break;
                }
                if (SearchSuggestionsPopup is not null && SearchSuggestionsPopup.IsOpen)
                {
                    SearchSuggestionsPopup.IsOpen = false;
                    e.Handled = true;
                    break;
                }
                if (PasteStackBanner is not null && PasteStackBanner.Visibility == Visibility.Visible)
                {
                    StopPasteStackRequested?.Invoke();
                    e.Handled = true;
                    break;
                }
                if (_items.Any(i => i.IsMarked))
                {
                    ClearMarks();
                    e.Handled = true;
                    break;
                }
                HidePalette();
                e.Handled = true;
                break;
            case Key.Back when SearchBox.IsKeyboardFocused && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == 0 && _activeChips.Count > 0:
                _activeChips.RemoveAt(_activeChips.Count - 1);
                Reload();
                e.Handled = true;
                break;
            case (Key.Enter or Key.Tab) when SearchBox.IsKeyboardFocused && SearchFilterChip.TryParseFilter(SearchBox.Text.Trim(), out var chip) && chip is not null:
                AddOrReplaceChip(chip);
                _isUpdatingSearchBoxText = true;
                try
                {
                    SearchBox.Text = "";
                    SearchBox.CaretIndex = 0;
                }
                finally
                {
                    _isUpdatingSearchBoxText = false;
                }
                Reload();
                e.Handled = true;
                break;
            case Key.Down when SearchBox.IsKeyboardFocused && SearchSuggestionsPopup is not null && !SearchSuggestionsPopup.IsOpen && SearchBox.Text.Length == 0:
                RefreshSuggestionsPopup();
                SearchSuggestionsPopup.IsOpen = true;
                e.Handled = true;
                break;
            case Key.Down when !IsAnyPreviewFocused:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up when !IsAnyPreviewFocused:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.PageDown when !IsAnyPreviewFocused:
                MoveSelection(+8);
                e.Handled = true;
                break;
            case Key.PageUp when !IsAnyPreviewFocused:
                MoveSelection(-8);
                e.Handled = true;
                break;
            case Key.Enter:
                if (HasPreviewSelection)
                {
                    PastePartialPreviewText();
                }
                else
                {
                    PasteSelected(plainText: mods == (ModifierKeys.Control | ModifierKeys.Shift));
                }
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9
                when mods is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift):
                // Ctrl+1…9 pastes the n-th visible item directly (Ctrl+Shift = plain text).
                int n = key >= Key.NumPad1 ? key - Key.NumPad1 : key - Key.D1;
                if (n < _items.Count) PasteSelected(plainText: mods.HasFlag(ModifierKeys.Shift), target: _items[n]);
                e.Handled = true;
                break;
            case Key.P when mods == ModifierKeys.Control:
                TogglePinSelected();
                e.Handled = true;
                break;
            case Key.F1:
                ShowKeyHelp();
                e.Handled = true;
                break;
            case Key.F2:
                RenameSelected();
                e.Handled = true;
                break;
            case Key.S when mods == ModifierKeys.Control:
                StartPasteStack();
                e.Handled = true;
                break;
            case Key.N when mods == ModifierKeys.Control:
                EditSnippet(createNew: true);
                e.Handled = true;
                break;
            case Key.E when mods == ModifierKeys.Control:
                EditSnippet(createNew: false);
                e.Handled = true;
                break;
            case Key.K when mods == ModifierKeys.Control:
                ShowTransformMenu();
                e.Handled = true;
                break;
            case Key.T when mods == ModifierKeys.Control:
                KeepOpenButton.IsChecked = !(KeepOpenButton.IsChecked == true);
                e.Handled = true;
                break;
            case Key.L when mods == ModifierKeys.Control:
                CycleSplitRatio();
                e.Handled = true;
                break;
            case Key.OemComma when mods == ModifierKeys.Control:
                OpenSettings();
                e.Handled = true;
                break;
            case Key.M when mods == ModifierKeys.Control:
                ToggleWidgetMode();
                e.Handled = true;
                break;
            case Key.R when mods == ModifierKeys.Control:
                ToggleRevealSelected();
                e.Handled = true;
                break;
            case Key.Space when mods == ModifierKeys.Control:
                if (ItemsList.SelectedItem is ItemViewModel m)
                {
                    m.IsMarked = !m.IsMarked;
                    m.MarkOrder = m.IsMarked ? ++_markSequence : 0;
                    MoveSelection(+1);
                    UpdateMarkedStatus();
                }
                e.Handled = true;
                break;
            case Key.C when mods == ModifierKeys.Control:
                if (HasPreviewSelection)
                {
                    CopyPartialPreviewText();
                    e.Handled = true;
                }
                else if (SearchBox.SelectionLength == 0)
                {
                    CopySelected();
                    e.Handled = true;
                }
                break;
            case Key.Delete when mods == ModifierKeys.None && !IsAnyPreviewFocused && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == SearchBox.Text.Length:
                // At the end of the search text Delete would do nothing in the text box, so it deletes the item.
                DeleteSelected();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        int i = Math.Clamp(ItemsList.SelectedIndex + delta, 0, _items.Count - 1);
        ItemsList.SelectedIndex = i;
        ItemsList.ScrollIntoView(ItemsList.SelectedItem);
    }

    private void UpdateMarkedStatus()
    {
        bool hasMarked = _items.Any(i => i.IsMarked);
        foreach (var item in _items)
        {
            item.MultiSelectActive = hasMarked;
        }
        UpdateContextualToolbar();
    }

    private void UpdateContextualToolbar()
    {
        if (IsCompact)
        {
            if (NormalToolbar is not null) NormalToolbar.Visibility = Visibility.Collapsed;
            if (MultiSelectToolbar is not null) MultiSelectToolbar.Visibility = Visibility.Collapsed;
            if (SensitiveToolbar is not null) SensitiveToolbar.Visibility = Visibility.Collapsed;
            return;
        }

        int markedCount = _items.Count(i => i.IsMarked);
        if (markedCount > 0)
        {
            if (NormalToolbar is not null) NormalToolbar.Visibility = Visibility.Collapsed;
            if (SensitiveToolbar is not null) SensitiveToolbar.Visibility = Visibility.Collapsed;
            if (MultiSelectToolbar is not null)
            {
                MultiSelectToolbar.Visibility = Visibility.Visible;
                if (MultiSelectCountBadge is not null)
                    MultiSelectCountBadge.Text = $"{markedCount} selected";
            }
            StatusText.Text = markedCount > 1 ? $"{markedCount} items selected — Enter merges, Ctrl+S stacks" : "1 item selected";
        }
        else if (ItemsList.SelectedItem is ItemViewModel cur && cur.IsSensitive && !cur.Revealed)
        {
            if (NormalToolbar is not null) NormalToolbar.Visibility = Visibility.Collapsed;
            if (MultiSelectToolbar is not null) MultiSelectToolbar.Visibility = Visibility.Collapsed;
            if (SensitiveToolbar is not null) SensitiveToolbar.Visibility = Visibility.Visible;
            StatusText.Text = "Sensitive item — press Ctrl+R to reveal";
        }
        else
        {
            if (MultiSelectToolbar is not null) MultiSelectToolbar.Visibility = Visibility.Collapsed;
            if (SensitiveToolbar is not null) SensitiveToolbar.Visibility = Visibility.Collapsed;
            if (NormalToolbar is not null) NormalToolbar.Visibility = Visibility.Visible;
            StatusText.Text = _svc.Settings.CaptureEnabled
                ? LocalizationService.Get("QuickPaste_ItemsCount", _svc.Count())
                : LocalizationService.Get("Tray_PauseCapture");
        }
    }

    /// <param name="plainText">Drop HTML/RTF formatting (Ctrl+Shift+Enter).</param>
    /// <param name="target">A specific item (Ctrl+1…9); null = the marked items, or the selection.</param>
    /// <param name="transform">Converts the text before it goes on the clipboard (Ctrl+K menu); implies plain text.</param>
    private bool WriteToClipboard(bool plainText = false, ItemViewModel? target = null, TextTransform? transform = null)
    {
        try
        {
            var marked = target is null ? _items.Where(i => i.IsMarked).ToList() : new();
            if (marked.Count > 1)
            {
                var merged = MergeService.Merge(marked.Select(m => m.Item), MergeSeparator.NewLine);
                _writer.Write(new ClipboardPayload
                {
                    Text = transform is null ? merged : transform.Apply(merged),
                    IsSensitive = marked.Any(m => m.Item.IsSensitive),
                });
                foreach (var m in marked) _svc.MarkUsed(m.Item);
                return true;
            }
            var vm = target ?? (marked.Count == 1 ? marked[0] : ItemsList.SelectedItem as ItemViewModel);
            if (vm is null) return false;
            var payload = _svc.LoadPayload(vm.Item, plainText || transform is not null, SnippetContext());
            if (payload is not null && transform is not null)
                payload = new ClipboardPayload { Text = transform.Apply(payload.Text ?? ""), IsSensitive = payload.IsSensitive };
            if (payload is null)
            {
                StatusText.Text = plainText ? "This item has no text to paste" : "The item's file is missing";
                return false;
            }
            _writer.Write(payload);
            _svc.MarkUsed(vm.Item);
            return true;
        }
        catch (COMException)
        {
            StatusText.Text = "Clipboard is busy — try again";
            return false;
        }
        catch (IOException)
        {
            StatusText.Text = "Could not read the item's file";
            return false;
        }
        catch (TransformException ex)
        {
            StatusText.Text = ex.Message;
            return false;
        }
    }

    private void PasteSelected(bool plainText = false, ItemViewModel? target = null, TextTransform? transform = null)
    {
        var vm = target ?? (_items.Where(i => i.IsMarked).ToList().Count == 1 ? _items.First(i => i.IsMarked) : ItemsList.SelectedItem as ItemViewModel);
        if (vm is not null && transform is null && _paste.HasTarget && _paste.IsRemoteDesktopTarget)
        {
            var text = vm.Item.Kind == ContentKind.Image ? vm.Item.OcrText : vm.Item.TextContent;
            if (RemoteCredentials.TryParse(text, out var creds) && creds is not null)
            {
                _ = PasteRemoteDesktopAsync(creds);
                return;
            }
        }

        if (!WriteToClipboard(plainText, target, transform)) return;
        string effectiveQuery = SearchFilterChip.Combine(_activeChips, SearchBox.Text);
        if (!string.IsNullOrWhiteSpace(effectiveQuery)) _svc.AddRecentSearch(effectiveQuery);
        if (KeepOpen) ClearMarks(); else HidePalette();
        // Opened from the tray: there is no app to paste into, the item is just on the clipboard now.
        if (_paste.HasTarget) _paste.PasteIntoPreviousWindow();
    }

    private async Task PasteRemoteDesktopAsync(RemoteCredentials creds)
    {
        string effectiveQuery = SearchFilterChip.Combine(_activeChips, SearchBox.Text);
        if (!string.IsNullOrWhiteSpace(effectiveQuery)) _svc.AddRecentSearch(effectiveQuery);
        if (KeepOpen) ClearMarks(); else HidePalette();
        await _paste.PasteRemotePairAsync(_writer, creds.Id, creds.Password);
    }

    private void CopySelected()
    {
        if (!WriteToClipboard()) return;
        string effectiveQuery = SearchFilterChip.Combine(_activeChips, SearchBox.Text);
        if (!string.IsNullOrWhiteSpace(effectiveQuery)) _svc.AddRecentSearch(effectiveQuery);
        if (KeepOpen) ClearMarks(); else HidePalette();
    }

    private void CopyPartialPreviewText()
    {
        var text = GetPreviewSelectedText();
        if (string.IsNullOrEmpty(text)) return;

        bool sensitive = (ItemsList.SelectedItem as ItemViewModel)?.Item.IsSensitive ?? false;
        try
        {
            _writer.Write(new ClipboardPayload { Text = text, IsSensitive = sensitive });
            StatusText.Text = "Copied selection to clipboard";
            if (!KeepOpen) HidePalette();
        }
        catch (COMException)
        {
            StatusText.Text = "Clipboard is busy — try again";
        }
    }

    private void PastePartialPreviewText()
    {
        var text = GetPreviewSelectedText();
        if (string.IsNullOrEmpty(text)) return;

        bool sensitive = (ItemsList.SelectedItem as ItemViewModel)?.Item.IsSensitive ?? false;
        try
        {
            _writer.Write(new ClipboardPayload { Text = text, IsSensitive = sensitive });
            if (KeepOpen) ClearMarks(); else HidePalette();
            if (_paste.HasTarget) _paste.PasteIntoPreviousWindow();
        }
        catch (COMException)
        {
            StatusText.Text = "Clipboard is busy — try again";
        }
    }

    private void CopyAllPreviewText()
    {
        var text = GetCurrentPreviewFullText();
        if (string.IsNullOrEmpty(text)) return;

        bool sensitive = (ItemsList.SelectedItem as ItemViewModel)?.Item.IsSensitive ?? false;
        try
        {
            _writer.Write(new ClipboardPayload { Text = text, IsSensitive = sensitive });
            StatusText.Text = "Copied text to clipboard";
            if (!KeepOpen) HidePalette();
        }
        catch (COMException)
        {
            StatusText.Text = "Clipboard is busy — try again";
        }
    }

    private const string KeyHelp =
        "Enter               paste (merged if several are marked)\n" +
        "Ctrl+Shift+Enter    paste as plain text (an image: its text)\n" +
        "Ctrl+1 … 9          paste item 1 … 9 (Ctrl+Shift = plain)\n" +
        "Ctrl+K / right-click  transform: case, trim, JSON, SQL, Base64, URL\n" +
        "Ctrl+C              copy without pasting (or copies selection in preview)\n" +
        "Ctrl+Space          mark item (in order)\n" +
        "Ctrl+S              paste stack: each Ctrl+V pastes the next marked item\n" +
        "Ctrl+P              pin / unpin item\n" +
        "Ctrl+N / Ctrl+E     save as snippet / edit snippet\n" +
        "Ctrl+T              pin window / keep open (compact size)\n" +
        "Ctrl+L              tỷ lệ chia: 25/75, 30/70, 40/60, 50/50\n" +
        "Ctrl+,              open settings\n" +
        "Ctrl+M              toggle compact widget mode\n" +
        "Ctrl+R              reveal a secret\n" +
        "Del                 delete item\n" +
        "Esc                 close\n\n" +
        "Search filters: type:sql  type:snippet  type:image  pinned:true  after:2026-09-01";

    private void ShowHelpOverlay()
    {
        if (HelpOverlay is not null)
        {
            HelpOverlay.Visibility = Visibility.Visible;
            if (HelpSearchBox is not null)
            {
                HelpSearchBox.Text = "";
                HelpSearchBox.Focus();
            }
        }
    }

    private void HideHelpOverlay()
    {
        if (HelpOverlay is not null)
        {
            HelpOverlay.Visibility = Visibility.Collapsed;
            SearchBox.Focus();
        }
    }

    private void FilterHelpShortcuts(string? query)
    {
        if (HelpCategoriesPanel is null) return;
        string q = (query ?? "").Trim();
        bool filterEmpty = string.IsNullOrEmpty(q);

        if (HelpSearchPlaceholder is not null)
            HelpSearchPlaceholder.Visibility = filterEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (HelpClearSearchButton is not null)
            HelpClearSearchButton.Visibility = filterEmpty ? Visibility.Collapsed : Visibility.Visible;

        foreach (UIElement catElem in HelpCategoriesPanel.Children)
        {
            if (catElem is Border catBorder && catBorder.Child is StackPanel sp)
            {
                int matchedRows = 0;
                for (int i = 0; i < sp.Children.Count; i++)
                {
                    var rowElem = sp.Children[i];
                    // Skip header row
                    if (i == 0) continue;

                    bool match = filterEmpty || ElementContainsText(rowElem, q);
                    rowElem.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                    if (match) matchedRows++;
                }
                catBorder.Visibility = (filterEmpty || matchedRows > 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static bool ElementContainsText(UIElement element, string query)
    {
        if (element is TextBlock tb)
            return tb.Text.Contains(query, StringComparison.OrdinalIgnoreCase);
        if (element is Border b && b.Child is UIElement bChild)
            return ElementContainsText(bChild, query);
        if (element is Panel p)
        {
            foreach (UIElement child in p.Children)
            {
                if (ElementContainsText(child, query))
                    return true;
            }
        }
        return false;
    }

    /// <summary>F1: the keyboard reference, shown in the help modal or preview pane.</summary>
    private void ShowKeyHelp()
    {
        if (HelpOverlay is not null)
        {
            ShowHelpOverlay();
            return;
        }
        if (IsCompact)
        {
            StatusText.Text = "Keys: Ctrl+K transform · Ctrl+, settings · Ctrl+S stack · Ctrl+1…9 · Ctrl+T unpin";
            return;
        }
        SensitiveHiddenContainer.Visibility = Visibility.Collapsed;
        SensitiveRevealedBanner.Visibility = Visibility.Collapsed;
        SensitiveRevealedTextBanner.Visibility = Visibility.Collapsed;
        ImageContainer.Visibility = Visibility.Collapsed;
        UrlContainer.Visibility = Visibility.Collapsed;
        CodeContainer.Visibility = Visibility.Collapsed;

        PreviewHeaderBar.Visibility = Visibility.Visible;
        SetPreviewBadge("SHORTCUTS", "#F3F4F6", "#4B5563");
        PreviewStatsText.Text = "Keyboard reference (F1)";

        TextContainer.Visibility = Visibility.Visible;
        PreviewText.Visibility = Visibility.Visible;
        PreviewText.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
        PreviewText.Text = KeyHelp;
    }

    /// <summary>Ctrl+S: the marked items, in the order they were marked, become a paste stack (each Ctrl+V pastes the next).</summary>
    private void StartPasteStack()
    {
        var marked = _items.Where(i => i.IsMarked).OrderBy(i => i.MarkOrder).Select(i => i.Item).ToList();
        if (marked.Count == 0)
        {
            if (ItemsList.SelectedItem is ItemViewModel sel && sel.Item is { } item)
            {
                var text = item.Kind == ContentKind.Image ? item.OcrText : item.TextContent;
                if (RemoteCredentials.TryParse(text, out var creds) && creds is not null)
                {
                    StartRemotePasteStack(creds);
                    return;
                }
            }

            StatusText.Text = "Mark items with Ctrl+Space in the order to paste, then Ctrl+S";
            return;
        }
        if (PasteStackRequested is null) return;
        if (KeepOpen) ClearMarks(); else HidePalette();
        PasteStackRequested(marked);
    }

    private void StartRemotePasteStack(RemoteCredentials creds)
    {
        var idItem = new ClipboardItem
        {
            Kind = ContentKind.Text,
            Subtype = "plain",
            Title = $"{creds.Provider} ID: {creds.Id}",
            TextContent = creds.Id,
        };
        var passItem = new ClipboardItem
        {
            Kind = ContentKind.Text,
            Subtype = "password",
            Title = $"{creds.Provider} Pass: {creds.Password}",
            TextContent = creds.Password,
            IsSensitive = true,
        };
        if (KeepOpen) ClearMarks(); else HidePalette();
        PasteStackRequested?.Invoke(new[] { idItem, passItem });
    }

    /// <summary>Raised by Ctrl+S with the items to paste one by one.</summary>
    public event Action<IReadOnlyList<ClipboardItem>>? PasteStackRequested;

    /// <summary>Raised by Stop button or Esc when a paste stack is active.</summary>
    public event Action? StopPasteStackRequested;

    /// <summary>Raised the first time the window is hidden, to show a tray reminder.</summary>
    public event Action? FirstHideRequested;

    public void UpdatePasteStackState(PasteStackState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (PasteStackBanner is null) return;
            if (state.IsActive && state.Total > 0)
            {
                PasteStackBanner.Visibility = Visibility.Visible;
                if (PasteStackStepText is not null)
                    PasteStackStepText.Text = $"{state.Position} / {state.Total}";
                if (PasteStackNextTitle is not null)
                    PasteStackNextTitle.Text = $"Next: {state.CurrentTitle ?? "item"}";
                if (PasteStackProgressBar is not null)
                {
                    PasteStackProgressBar.Maximum = state.Total;
                    PasteStackProgressBar.Value = state.Position;
                }
            }
            else
            {
                PasteStackBanner.Visibility = Visibility.Collapsed;
            }
        });
    }

    /// <summary>Raised by Ctrl+, or Settings button to open Settings window.</summary>
    public event Action? OpenSettingsRequested;

    private void OpenSettings()
    {
        if (!KeepOpen) HidePalette();
        OpenSettingsRequested?.Invoke();
    }

    private void ToggleTransparency()
    {
        _svc.Settings.EnableTransparency = false;
        ApplyTransparency();
        SaveSettings();
    }

    internal void ApplyTransparency(bool? enableOverride = null, double? opacityOverride = null)
    {
        _svc.Settings.EnableTransparency = false;
        if (IsLoaded)
        {
            WindowBackdrop.Apply(this, false);
        }
        if (RootBorder is not null)
        {
            RootBorder.Background = System.Windows.Media.Brushes.White;
            RootBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0xE7, 0xEB));
        }
        if (SearchContainer is not null)
        {
            SearchContainer.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0xF4, 0xF6));
            SearchContainer.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0xE7, 0xEB));
        }
    }

    private void ToggleWidgetMode()
    {
        _svc.Settings.WidgetMode = !_svc.Settings.WidgetMode;
        SaveSettings();
        _resizingFromCode = true;
        try
        {
            ApplyLayout();
            Width = TargetWidth;
            Height = TargetHeight;
            PositionOnScreen();
            if (!IsCompact) UpdatePreview();
        }
        finally
        {
            _resizingFromCode = false;
        }
    }

    /// <summary>Snippet variables: {clipboard} is what is on the Windows clipboard right now (falls back to the newest history text).</summary>
    private TemplateContext SnippetContext() => new()
    {
        Clipboard = () =>
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText()) return System.Windows.Clipboard.GetText();
            }
            catch (COMException) { /* clipboard busy */ }
            return _svc.LatestHistoryText();
        },
    };

    /// <summary>Ctrl+N: new snippet from the selected item (or empty). Ctrl+E: edit the selected snippet.</summary>
    private void EditSnippet(bool createNew)
    {
        var vm = ItemsList.SelectedItem as ItemViewModel;
        bool editing = !createNew && vm?.Item.Kind == ContentKind.Snippet;
        if (!createNew && !editing)
        {
            StatusText.Text = "Select a snippet to edit (Ctrl+N saves the selection as a new snippet)";
            return;
        }
        if (createNew && vm is not null && (vm.IsImage || vm.Item.IsSensitive))
        {
            StatusText.Text = vm.IsImage ? "Images can't be snippets" : "Secrets can't be saved as snippets";
            return;
        }

        var (name, body) = vm is null ? ("", "") : ClipboardService.SnippetDraftFrom(vm.Item);
        var dialog = new SnippetDialog(_svc, name, body, editing ? vm!.Item : null);
        bool wasVisible = IsVisible;
        HidePalette();
        bool saved = dialog.ShowDialog() == true;
        if (saved || wasVisible) ShowPalette(keepTarget: true, search: saved ? "type:snippet " : "");
    }


    // ==========================================
    // Filter Chips & Search Suggestions System
    // ==========================================

    private void UpdateSearchChipsState()
    {
        Placeholder.Visibility = (SearchBox.Text.Length == 0 && _activeChips.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        if (ClearSearchButton is not null)
            ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        UpdatePillActiveStates();
        UpdateFilterBarActiveChips();
    }

    private void SetOrToggleCategoryFilter(string? key, string? val)
    {
        if (SearchSuggestionsPopup is not null) SearchSuggestionsPopup.IsOpen = false;

        if (key is null || val is null)
        {
            var toRemove = _activeChips.Where(c => c.Key is "type" or "pinned").ToList();
            foreach (var c in toRemove) _activeChips.Remove(c);
        }
        else
        {
            bool alreadyActive = _activeChips.Any(c => c.Key == key && (c.Value.Equals(val, StringComparison.OrdinalIgnoreCase) ||
                (key == "type" && val == "image" && c.Value.Equals("images", StringComparison.OrdinalIgnoreCase)) ||
                (key == "type" && val == "snippet" && c.Value.Equals("snippets", StringComparison.OrdinalIgnoreCase))));

            var toRemove = _activeChips.Where(c => c.Key is "type" or "pinned").ToList();
            foreach (var c in toRemove) _activeChips.Remove(c);

            if (!alreadyActive)
            {
                _activeChips.Add(new SearchFilterChip(key, val));
            }
        }

        SearchBox.Focus();
        Reload();
    }

    private void UpdateFilterBarActiveChips()
    {
        if (ChipAll is null || ChipCode is null || ChipImage is null || ChipPinned is null || ChipSnippet is null)
            return;

        bool isCode = _activeChips.Any(c => c.Key == "type" && c.Value.Equals("code", StringComparison.OrdinalIgnoreCase));
        bool isImage = _activeChips.Any(c => c.Key == "type" && (c.Value.Equals("image", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("images", StringComparison.OrdinalIgnoreCase)));
        bool isSnippet = _activeChips.Any(c => c.Key == "type" && (c.Value.Equals("snippet", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("snippets", StringComparison.OrdinalIgnoreCase)));
        bool isPinned = _activeChips.Any(c => c.Key == "pinned" && (c.Value.Equals("true", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("yes", StringComparison.OrdinalIgnoreCase) || c.Value == "1"));

        bool hasCategoryOrPinned = isCode || isImage || isSnippet || isPinned;
        bool isAll = !hasCategoryOrPinned;

        ChipAll.Tag = isAll ? "active" : null;
        ChipCode.Tag = isCode ? "active" : null;
        ChipImage.Tag = isImage ? "active" : null;
        ChipPinned.Tag = isPinned ? "active" : null;
        ChipSnippet.Tag = isSnippet ? "active" : null;
    }

    private void OnSearchBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSearchBoxText) return;

        var text = SearchBox.Text;
        Placeholder.Visibility = (text.Length == 0 && _activeChips.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        if (ClearSearchButton is not null)
            ClearSearchButton.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

        // 1. If text contains whitespace (e.g. "type:sql " or user pasted multiple tokens), extract completed filters
        if (text.Contains(' '))
        {
            var (extractedChips, remaining) = SearchFilterChip.ExtractFilters(text);
            if (extractedChips.Count > 0)
            {
                _isUpdatingSearchBoxText = true;
                try
                {
                    foreach (var chip in extractedChips)
                    {
                        AddOrReplaceChip(chip);
                    }
                    SearchBox.Text = remaining;
                    SearchBox.CaretIndex = SearchBox.Text.Length;
                }
                finally
                {
                    _isUpdatingSearchBoxText = false;
                }
                _debounce.Stop();
                Reload();
                return;
            }
        }
        // 2. Exact match of a recognized complete filter token (e.g. "type:sql", "pinned:true")
        else if (SearchFilterChip.TryParseFilter(text.Trim(), out var singleChip) && singleChip is not null)
        {
            if (IsRecognizedCompleteFilter(singleChip.Key, singleChip.Value))
            {
                _isUpdatingSearchBoxText = true;
                try
                {
                    AddOrReplaceChip(singleChip);
                    SearchBox.Text = "";
                    SearchBox.CaretIndex = 0;
                }
                finally
                {
                    _isUpdatingSearchBoxText = false;
                }
                _debounce.Stop();
                Reload();
                return;
            }
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private bool IsRecognizedCompleteFilter(string key, string val)
    {
        var k = key.ToLowerInvariant();
        var v = val.ToLowerInvariant();

        if (k is "pinned" or "sensitive")
            return v is "true" or "false" or "yes" or "no" or "1" or "0";

        if (k is "before" or "after")
            return v.Length == 10 && DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        if (k is "type" or "is")
        {
            return v is "sql" or "json" or "xml" or "yaml" or "shell" or "markdown"
                or "code" or "url" or "link" or "links" or "image" or "images" or "files" or "file"
                or "text" or "snippet" or "snippets" or "template" or "sensitive" or "email" or "phone"
                or "number" or "ip" or "log";
        }

        return false;
    }

    private void AddOrReplaceChip(SearchFilterChip chip)
    {
        int existing = -1;
        for (int i = 0; i < _activeChips.Count; i++)
        {
            if (_activeChips[i].Key == chip.Key)
            {
                existing = i;
                break;
            }
        }
        if (existing >= 0) _activeChips[existing] = chip;
        else _activeChips.Add(chip);
    }

    private void OnRemoveFilterChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is SearchFilterChip chip)
        {
            _activeChips.Remove(chip);
            SearchBox.Focus();
            Reload();
        }
    }

    private void RefreshSuggestionsPopup()
    {
        PopulateRecentSearches();
        PopulateFilterPills();
    }

    private void PopulateRecentSearches()
    {
        var recent = _svc.Settings.RecentSearches;
        if (recent.Count == 0)
        {
            RecentSearchesSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            RecentSearchesSection.Visibility = Visibility.Visible;
            RecentSearchesList.ItemsSource = recent.Take(6).ToList();
        }
    }

    private void OnClearAllRecentClick(object sender, RoutedEventArgs e)
    {
        _svc.Settings.RecentSearches.Clear();
        SaveSettings();
        RecentSearchesSection.Visibility = Visibility.Collapsed;
    }

    private void OnRemoveRecentSearchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is string query)
        {
            _svc.RemoveRecentSearch(query);
            PopulateRecentSearches();
        }
    }

    private void OnRecentSearchItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is string query)
        {
            ApplySearchQuery(query);
            SearchSuggestionsPopup.IsOpen = false;
            SearchBox.Focus();
        }
    }

    private void ApplySearchQuery(string query)
    {
        _activeChips.Clear();
        var (chips, rem) = SearchFilterChip.ExtractFilters(query);
        foreach (var c in chips) _activeChips.Add(c);
        _isUpdatingSearchBoxText = true;
        try
        {
            SearchBox.Text = rem;
            SearchBox.CaretIndex = rem.Length;
        }
        finally
        {
            _isUpdatingSearchBoxText = false;
        }
        Reload();
    }

    private void PopulateFilterPills()
    {
        // Type pills
        TypeFilterPills.Children.Clear();
        var types = new (string val, string label)[]
        {
            ("sql", "SQL"),
            ("code", "Code"),
            ("json", "JSON"),
            ("snippet", "Snippet"),
            ("image", "Images"),
            ("url", "URL"),
            ("files", "Files"),
            ("sensitive", "Sensitive"),
            ("text", "Text")
        };
        foreach (var t in types)
        {
            TypeFilterPills.Children.Add(CreateFilterPillButton("type", t.val, t.label));
        }

        // Pinned pills
        PinnedFilterPills.Children.Clear();
        PinnedFilterPills.Children.Add(CreateFilterPillButton("pinned", "true", "📌 Pinned"));
        PinnedFilterPills.Children.Add(CreateFilterPillButton("pinned", "false", "Unpinned"));

        // Date pills
        DateFilterPills.Children.Clear();
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var last7 = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd");
        var last30 = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd");
        DateFilterPills.Children.Add(CreateFilterPillButton("after", today, "Today"));
        DateFilterPills.Children.Add(CreateFilterPillButton("after", last7, "Last 7 days"));
        DateFilterPills.Children.Add(CreateFilterPillButton("after", last30, "Last 30 days"));

        // Sensitive pills
        SensitiveFilterPills.Children.Clear();
        SensitiveFilterPills.Children.Add(CreateFilterPillButton("sensitive", "true", "🔒 Sensitive only"));
        SensitiveFilterPills.Children.Add(CreateFilterPillButton("sensitive", "false", "Non-sensitive"));

        UpdatePillActiveStates();
    }

    private Button CreateFilterPillButton(string key, string val, string label)
    {
        var btn = new Button
        {
            Content = label,
            DataContext = (key, val),
            Style = (Style)FindResource("PillButton"),
        };
        btn.Click += OnFilterPillClick;
        return btn;
    }

    private void UpdatePillActiveStates()
    {
        void CheckPanel(Panel panel)
        {
            if (panel is null) return;
            foreach (UIElement child in panel.Children)
            {
                if (child is Button b && b.DataContext is ValueTuple<string, string> tuple)
                {
                    bool isActive = _activeChips.Any(c => c.Key == tuple.Item1 && c.Value.Equals(tuple.Item2, StringComparison.OrdinalIgnoreCase));
                    b.Tag = isActive ? "active" : null;
                    b.ToolTip = isActive ? "Click to remove this filter" : $"Add filter {tuple.Item1}:{tuple.Item2}";
                }
            }
        }
        CheckPanel(TypeFilterPills);
        CheckPanel(PinnedFilterPills);
        CheckPanel(DateFilterPills);
        CheckPanel(SensitiveFilterPills);
    }

    private void OnFilterPillClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is (string key, string val))
        {
            var existing = _activeChips.FirstOrDefault(c => c.Key == key && c.Value.Equals(val, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _activeChips.Remove(existing);
            }
            else
            {
                AddOrReplaceChip(new SearchFilterChip(key, val));
            }
            SearchSuggestionsPopup.IsOpen = false;
            SearchBox.Focus();
            Reload();
        }
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject? parent)
    {
        if (child is null || parent is null) return false;
        var current = child;
        while (current is not null)
        {
            if (ReferenceEquals(current, parent)) return true;
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                current = VisualTreeHelper.GetParent(current);
            else if (current is FrameworkContentElement fce)
                current = fce.Parent;
            else
                break;
        }
        return false;
    }

    /// <summary>Ctrl+K / right-click: paste the selection (or the marked items) converted by a text transform.</summary>
    private void ShowTransformMenu()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return;

        var menu = new ContextMenu
        {
            PlacementTarget = ItemsList.ItemContainerGenerator.ContainerFromItem(vm) as UIElement ?? ItemsList,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        // 0. Remote Desktop Actions if detected
        var textForRemote = vm.Item.Kind == ContentKind.Image ? vm.Item.OcrText : vm.Item.TextContent;
        if (RemoteCredentials.TryParse(textForRemote, out var creds) && creds is not null)
        {
            var headerMi = new MenuItem
            {
                Header = $"🖥️ {creds.Provider} ({creds.Id} / {creds.Password})",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(headerMi);

            var autoTabItem = new MenuItem { Header = $"⚡ Auto-Fill {creds.Provider} (ID → Tab → Pass)", FontWeight = FontWeights.SemiBold };
            autoTabItem.Click += (_, _) => _ = PasteRemoteDesktopAsync(creds);
            menu.Items.Add(autoTabItem);

            var remoteStackItem = new MenuItem { Header = "📋 Start Paste Stack (ID then Pass)" };
            remoteStackItem.Click += (_, _) => StartRemotePasteStack(creds);
            menu.Items.Add(remoteStackItem);

            var copyIdItem = new MenuItem { Header = $"Copy ID ({creds.Id})" };
            copyIdItem.Click += (_, _) =>
            {
                _writer.Write(new ClipboardPayload { Text = creds.Id });
                StatusText.Text = $"Copied {creds.Provider} ID: {creds.Id}";
            };
            menu.Items.Add(copyIdItem);

            var copyPassItem = new MenuItem { Header = $"Copy Password ({creds.Password})" };
            copyPassItem.Click += (_, _) =>
            {
                _writer.Write(new ClipboardPayload { Text = creds.Password });
                StatusText.Text = $"Copied {creds.Provider} Password";
            };
            menu.Items.Add(copyPassItem);

            menu.Items.Add(new Separator());
        }

        // 1. Primary actions
        var pasteItem = new MenuItem { Header = "Paste", InputGestureText = "Enter" };
        pasteItem.Click += (_, _) => PasteSelected();
        menu.Items.Add(pasteItem);

        var plainItem = new MenuItem { Header = "Paste as plain text", InputGestureText = "Ctrl+Shift+Enter" };
        plainItem.Click += (_, _) => PasteSelected(plainText: true);
        menu.Items.Add(plainItem);

        var copyItem = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
        copyItem.Click += (_, _) => CopySelected();
        menu.Items.Add(copyItem);

        // 2. Transform Submenu (for text items)
        if (!vm.IsImage || _items.Any(i => i.IsMarked))
        {
            menu.Items.Add(new Separator());
            var transformSubmenu = new MenuItem { Header = "Transform →" };
            foreach (var t in TextTransforms.All)
            {
                if (t.Id is "json-pretty" or "sql" or "base64-encode") transformSubmenu.Items.Add(new Separator());
                var mi = new MenuItem { Header = t.Name };
                var curTransform = t;
                mi.Click += (_, _) => PasteSelected(transform: curTransform);
                transformSubmenu.Items.Add(mi);
            }
            menu.Items.Add(transformSubmenu);
        }

        menu.Items.Add(new Separator());

        // 3. Pin / Unpin
        var pinItem = new MenuItem
        {
            Header = vm.Item.IsPinned ? "Unpin item" : "Pin item",
            InputGestureText = "Ctrl+P",
        };
        pinItem.Click += (_, _) => TogglePinSelected();
        menu.Items.Add(pinItem);

        // 4. Rename
        var renameItem = new MenuItem
        {
            Header = LocalizationService.Get("Menu_Rename"),
            InputGestureText = "F2",
        };
        renameItem.Click += (_, _) => RenameSelected();
        menu.Items.Add(renameItem);

        // 5. Snippet and Stack
        if (!vm.IsImage && !vm.IsSensitive)
        {
            var snippetItem = new MenuItem { Header = "Save as snippet", InputGestureText = "Ctrl+N" };
            snippetItem.Click += (_, _) => EditSnippet(createNew: true);
            menu.Items.Add(snippetItem);
        }

        var stackItem = new MenuItem { Header = "Start paste stack", InputGestureText = "Ctrl+S" };
        stackItem.Click += (_, _) =>
        {
            if (!vm.IsMarked)
            {
                vm.IsMarked = true;
                vm.MarkOrder = ++_markSequence;
                UpdateMarkedStatus();
            }
            StartPasteStack();
        };
        menu.Items.Add(stackItem);

        menu.Items.Add(new Separator());

        // 6. Delete
        var delItem = new MenuItem { Header = "Delete", InputGestureText = "Del" };
        delItem.Click += (_, _) => DeleteSelected();
        menu.Items.Add(delItem);

        menu.Closed += (_, _) => { if (IsVisible) SearchBox.Focus(); };
        menu.IsOpen = true;
    }

    private void ClearMarks()
    {
        foreach (var vm in _items)
        {
            vm.IsMarked = false;
            vm.MultiSelectActive = false;
        }
        UpdateMarkedStatus();
    }

    private void TogglePinSelected()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return;
        _svc.TogglePin(vm.Item); // raises HistoryChanged → Reload(keepSelection)
    }

    private void RenameSelected()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return;
        var dialog = new RenameDialog(_svc, vm.Item);
        bool wasVisible = IsVisible;
        HidePalette();
        bool saved = dialog.ShowDialog() == true;
        if (saved || wasVisible) ShowPalette(keepTarget: true);
    }

    private void DeleteSelected()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return;
        int index = ItemsList.SelectedIndex;
        _svc.Delete(vm.Item);
        if (_items.Count > 0) ItemsList.SelectedIndex = Math.Clamp(index, 0, _items.Count - 1);
    }
}
