using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteOverlay.Models;
using PlayniteOverlay.Services;
using PlayniteOverlay.Interop;

namespace PlayniteOverlay;

public partial class OverlayWindow : Window
{
    private static readonly ILogger logger = LogManager.GetLogger();

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly Action onSwitch;
    private readonly Action onExit;
    private readonly Action<string, Action<bool>>? onAudioDeviceChanged;
    private readonly GameVolumeService? gameVolumeService;
    private readonly int? currentGameProcessId;
    private readonly List<OverlayItem> items;
    private readonly List<RunningApp> runningApps;
    private readonly GameSwitcher? gameSwitcher;
    private readonly OverlayItem? currentGameField;
    private readonly DispatcherTimer timeTimer;
    private readonly List<Models.OverlayShortcut> shortcuts;
    
    private NavigationTarget navigationTarget = NavigationTarget.CurrentGameSection;
    private bool isInsideSection = false;
    private int selectedIndex = -1;
    private int runningAppSelectedIndex = -1;
    private int shortcutsSelectedIndex = -1;
    private bool isClosing;
    private bool isInitializingAudio = false;
    private static readonly SolidColorBrush HighlightBrush = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public OverlayWindow(Action onSwitch, Action onExit, OverlayItem? currentGame, IEnumerable<RunningApp> runningApps, IEnumerable<OverlayItem> recentGames, IEnumerable<AudioDevice>? audioDevices = null, Action<string, Action<bool>>? onAudioDeviceChanged = null, GameVolumeService? gameVolumeService = null, int? currentGameProcessId = null, GameSwitcher? gameSwitcher = null, IEnumerable<Models.OverlayShortcut>? shortcuts = null)
    {
        InitializeComponent();
        this.onSwitch = onSwitch;
        this.onExit = onExit;
        this.onAudioDeviceChanged = onAudioDeviceChanged;
        this.gameVolumeService = gameVolumeService;
        this.currentGameProcessId = currentGameProcessId;
        this.gameSwitcher = gameSwitcher;

        this.currentGameField = currentGame;
        this.items = new List<OverlayItem>(recentGames);
        this.runningApps = new List<RunningApp>(runningApps);
        this.shortcuts = shortcuts != null ? new List<Models.OverlayShortcut>(shortcuts) : new List<Models.OverlayShortcut>();

            timeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timeTimer.Tick += TimeTimer_Tick;
        timeTimer.Start();

        UpdateTimeDisplay();

        if (currentGame != null)
        {
            CurrentGameSection.Visibility = Visibility.Visible;
            CurrentGameTitle.Text = currentGame.Title;
            CurrentGameInfo.Text = currentGame.SecondaryText ?? "";
            
            if (!string.IsNullOrWhiteSpace(currentGame.ImagePath))
            {
                try
                {
                    CurrentGameCover.Source = new BitmapImage(new Uri(currentGame.ImagePath, UriKind.RelativeOrAbsolute));
                }
                catch
                {
                }
            }

            SetupAchievementsDisplay(currentGame.Achievements);
        }
        else
        {
            CurrentGameSection.Visibility = Visibility.Collapsed;
        }

        if (this.runningApps.Count > 0)
        {
            RunningAppsSection.Visibility = Visibility.Visible;
            RunningAppsList.ItemsSource = this.runningApps;
        }
        else
        {
            RunningAppsSection.Visibility = Visibility.Collapsed;
        }

        RunningAppsList.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnRunningAppSwitchClick));
        RunningAppsList.SelectionChanged += (_, __) =>
        {
            if (RunningAppsList.SelectedIndex >= 0)
            {
                runningAppSelectedIndex = RunningAppsList.SelectedIndex;
            }
        };

        RecentList.ItemsSource = this.items;
        
        if (this.items.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            RecentList.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
            RecentList.Visibility = Visibility.Visible;
        }

        RecentList.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnRecentPlayClick));
        RecentList.SelectionChanged += (_, __) =>
        {
            if (RecentList.SelectedIndex >= 0)
            {
                selectedIndex = RecentList.SelectedIndex;
            }
        };

        SetupAudioDevices(audioDevices);

        SetupShortcuts();

        VolumeSlider.ValueChanged += OnVolumeSliderChanged;
        MuteBtn.Click += OnMuteBtnClick;

        Dispatcher.BeginInvoke(() => FocusFirstVisibleSection(), DispatcherPriority.Loaded);

        SwitchBtn.Click += (_, __) =>
        {
            EventHandler? closed = null;
            closed = (s, e2) =>
            {
                this.Closed -= closed;
                try { this.onSwitch(); } catch { }
            };
            this.Closed += closed;
            this.Close();
        };
        
        ExitBtn.Click += (_, __) =>
        {
            EventHandler? closed = null;
            closed = (s, e2) =>
            {
                this.Closed -= closed;
                try { this.onExit(); } catch { }
            };
            this.Closed += closed;
            this.Close();
        };
        
        Backdrop.MouseLeftButtonDown += (_, __) => this.Close();
        PreviewKeyDown += OnPreviewKeyDown;
        
        Loaded += async (_, __) =>
        {
            Activate(); Focus(); Keyboard.Focus(this);
            
            try
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                RootCard.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            catch { }
        };
    }

    private void FocusFirstVisibleSection()
    {
        if (CurrentGameSection.Visibility == Visibility.Visible)
        {
            FocusSection(NavigationTarget.CurrentGameSection);
        }
        else if (ShortcutsSection.Visibility == Visibility.Visible)
        {
            FocusSection(NavigationTarget.ShortcutsSection);
        }
        else if (RunningAppsSection.Visibility == Visibility.Visible)
        {
            FocusSection(NavigationTarget.RunningAppsSection);
        }
        else
        {
            FocusSection(NavigationTarget.RecentGamesSection);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AudioDeviceCombo.IsDropDownOpen && 
            (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter))
        {
            return;
        }
        
        switch (e.Key)
        {
            case Key.Escape:
                PerformCancel();
                break;
            case Key.Up:
                NavigateUp();
                break;
            case Key.Down:
                NavigateDown();
                break;
            case Key.Left:
                NavigateLeft();
                break;
            case Key.Right:
                NavigateRight();
                break;
            case Key.Tab:
                NavigateTab(e.KeyboardDevice.Modifiers == ModifierKeys.Shift);
                break;
            case Key.Enter:
                PerformAccept();
                break;
            case Key.Space:
                return;
        }
        
        e.Handled = true;
    }

    private void OnRecentPlayClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button btn && btn.CommandParameter is OverlayItem item)
        {
            item.OnSelect?.Invoke();
            Close();
        }
    }

    private void OnRunningAppSwitchClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button btn && btn.CommandParameter is RunningApp app)
        {
            app.OnSwitch?.Invoke();
            Close();
        }
    }

    #region Section-Level Navigation

    private void FocusSection(NavigationTarget section)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => FocusSection(section));
            return;
        }

        isInsideSection = false;
        navigationTarget = section;
        
        RunningAppsList.SelectedIndex = -1;
        RecentList.SelectedIndex = -1;
        
        ClearSectionHighlights();
        
        switch (section)
        {
            case NavigationTarget.CurrentGameSection:
                CurrentGameSection.BorderBrush = HighlightBrush;
                CurrentGameSection.BringIntoView();
                Keyboard.Focus(CurrentGameSection);
                break;
            case NavigationTarget.ShortcutsSection:
                ShortcutsSection.BorderBrush = HighlightBrush;
                ShortcutsSection.BringIntoView();
                Keyboard.Focus(ShortcutsSection);
                break;
            case NavigationTarget.RunningAppsSection:
                RunningAppsSection.BorderBrush = HighlightBrush;
                RunningAppsSection.BringIntoView();
                Keyboard.Focus(RunningAppsSection);
                break;

            case NavigationTarget.RecentGamesSection:
                RecentGamesSection.BorderBrush = HighlightBrush;
                RecentGamesSection.BringIntoView();
                Keyboard.Focus(RecentGamesSection);
                break;
        }
    }

    private void ClearSectionHighlights()
    {
        CurrentGameSection.BorderBrush = TransparentBrush;
        ShortcutsSection.BorderBrush = TransparentBrush;
        RunningAppsSection.BorderBrush = TransparentBrush;
        RecentGamesSection.BorderBrush = TransparentBrush;
    }

    private NavigationTarget? GetPreviousVisibleSection(NavigationTarget current)
    {
        switch (current)
        {
            case NavigationTarget.CurrentGameSection:
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                if (ShortcutsSection.Visibility == Visibility.Visible) return NavigationTarget.ShortcutsSection;
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                return null;
                
            case NavigationTarget.ShortcutsSection:
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                return null;
                
            case NavigationTarget.RunningAppsSection:
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                if (ShortcutsSection.Visibility == Visibility.Visible) return NavigationTarget.ShortcutsSection;
                return null;

            case NavigationTarget.RecentGamesSection:
                if (ShortcutsSection.Visibility == Visibility.Visible) return NavigationTarget.ShortcutsSection;
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                return null;
                
            default:
                return null;
        }
    }

    private NavigationTarget? GetNextVisibleSection(NavigationTarget current)
    {
        switch (current)
        {
            case NavigationTarget.CurrentGameSection:
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                if (ShortcutsSection.Visibility == Visibility.Visible) return NavigationTarget.ShortcutsSection;
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                return null;
                
            case NavigationTarget.ShortcutsSection:
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                return null;
                
            case NavigationTarget.RunningAppsSection:
                if (ShortcutsSection.Visibility == Visibility.Visible) return NavigationTarget.ShortcutsSection;
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                return null;
                
            case NavigationTarget.RecentGamesSection:
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                if (ShortcutsSection.Visibility == Visibility.Visible) return NavigationTarget.ShortcutsSection;
                return null;
                
            default:
                return null;
        }
    }

    #endregion

    #region Item-Level Navigation

    private void EnterSection(NavigationTarget section)
    {
        isInsideSection = true;
        ClearSectionHighlights();
        
        switch (section)
        {
            case NavigationTarget.CurrentGameSection:
                navigationTarget = NavigationTarget.SwitchButton;
                FocusSwitchButton();
                break;
            case NavigationTarget.ShortcutsSection:
                if (ShortcutsPanel != null && ShortcutsPanel.Children.Count > 0)
                {
                    navigationTarget = NavigationTarget.ShortcutItem;
                    shortcutsSelectedIndex = 0;
                    FocusShortcutButton(0);
                }
                break;
            case NavigationTarget.RunningAppsSection:
                navigationTarget = NavigationTarget.RunningAppItem;
                runningAppSelectedIndex = 0;
                FocusRunningAppItem(0);
                break;

            case NavigationTarget.RecentGamesSection:
                navigationTarget = NavigationTarget.RecentGameItem;
                selectedIndex = 0;
                FocusRecentGameItem(0);
                break;
        }
    }

    private void ExitToSectionLevel()
    {
        NavigationTarget section;
        switch (navigationTarget)
        {
            case NavigationTarget.SwitchButton:
            case NavigationTarget.ExitButton:
            case NavigationTarget.VolumeSlider:
            case NavigationTarget.MuteBtn:
            case NavigationTarget.AudioDeviceCombo:
                section = NavigationTarget.CurrentGameSection;
                break;
            case NavigationTarget.ShortcutItem:
                section = NavigationTarget.ShortcutsSection;
                break;
            case NavigationTarget.RunningAppItem:
                section = NavigationTarget.RunningAppsSection;
                break;

            case NavigationTarget.RecentGameItem:
                section = NavigationTarget.RecentGamesSection;
                break;
            default:
                return;
        }

        FocusSection(section);
    }

    private void FocusSwitchButton()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(FocusSwitchButton);
            return;
        }

        RunningAppsList.SelectedIndex = -1;
        RecentList.SelectedIndex = -1;
        
        navigationTarget = NavigationTarget.SwitchButton;
        SwitchBtn.Focus();
        CurrentGameSection.BringIntoView();
    }

    private void FocusExitButton()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(FocusExitButton);
            return;
        }

        RunningAppsList.SelectedIndex = -1;
        RecentList.SelectedIndex = -1;
        
        navigationTarget = NavigationTarget.ExitButton;
        ExitBtn.Focus();
        CurrentGameSection.BringIntoView();
    }

    private void FocusAudioCombo()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(FocusAudioCombo);
            return;
        }

        RunningAppsList.SelectedIndex = -1;
        RecentList.SelectedIndex = -1;

        navigationTarget = NavigationTarget.AudioDeviceCombo;
        AudioDeviceCombo.Focus();
        CurrentGameSection.BringIntoView();
    }

    private void FocusVolumeSlider()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(FocusVolumeSlider);
            return;
        }

        RunningAppsList.SelectedIndex = -1;
        RecentList.SelectedIndex = -1;

        navigationTarget = NavigationTarget.VolumeSlider;
        VolumeSlider.Focus();
        CurrentGameSection.BringIntoView();
    }

    private void FocusMuteBtn()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(FocusMuteBtn);
            return;
        }

        RunningAppsList.SelectedIndex = -1;
        RecentList.SelectedIndex = -1;

        navigationTarget = NavigationTarget.MuteBtn;
        MuteBtn.Focus();
        CurrentGameSection.BringIntoView();
    }

    private void FocusFirstShortcutButton()
    {
        if (ShortcutsPanel == null || ShortcutsPanel.Children.Count == 0) return;
        FocusShortcutButton(0);
    }

    private void FocusShortcutButton(int index)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => FocusShortcutButton(index));
            return;
        }

        if (ShortcutsPanel == null || ShortcutsPanel.Children.Count == 0) return;

        index = Math.Max(0, Math.Min(index, ShortcutsPanel.Children.Count - 1));

        RunningAppsList.SelectedIndex = -1;
        RecentList.SelectedIndex = -1;

        navigationTarget = NavigationTarget.ShortcutItem;
        shortcutsSelectedIndex = index;

        // Use async fallback pattern like FocusRunningAppItem for robustness
        if (!TryFocusShortcutButton())
        {
            Dispatcher.BeginInvoke(() => TryFocusShortcutButton(), DispatcherPriority.Loaded);
        }
    }

    private bool TryFocusShortcutButton()
    {
        if (ShortcutsPanel == null || shortcutsSelectedIndex < 0 || shortcutsSelectedIndex >= ShortcutsPanel.Children.Count)
            return false;

        var button = ShortcutsPanel.Children[shortcutsSelectedIndex] as Button;
        if (button == null) return false;

        button.Focus();
        ShortcutsSection.BringIntoView();
        return true;
    }

    private int GetShortcutIndexAbove(int currentIndex)
    {
        if (ShortcutsPanel == null || currentIndex <= 0)
            return -1;

        var currentButton = ShortcutsPanel.Children[currentIndex] as Button;
        if (currentButton == null) return -1;

        // Get current button's position relative to panel
        var currentPos = currentButton.TransformToVisual(ShortcutsPanel).Transform(new Point(0, 0));
        var currentCenterX = currentPos.X + currentButton.ActualWidth / 2;
        var currentY = currentPos.Y;

        // Find the button closest to current X position on the row above
        int bestIndex = -1;
        double bestXDiff = double.MaxValue;
        double bestY = double.MaxValue;

        for (int i = 0; i < currentIndex; i++)
        {
            var btn = ShortcutsPanel.Children[i] as Button;
            if (btn == null) continue;

            var pos = btn.TransformToVisual(ShortcutsPanel).Transform(new Point(0, 0));
            var centerY = pos.Y + btn.ActualHeight / 2;

            // Must be on a different (higher) row
            if (centerY >= currentY - 5) continue;

            // Find the closest row above
            if (centerY > bestY) continue;

            var centerX = pos.X + btn.ActualWidth / 2;
            var xDiff = Math.Abs(centerX - currentCenterX);

            if (centerY < bestY || (Math.Abs(centerY - bestY) < 5 && xDiff < bestXDiff))
            {
                bestY = centerY;
                bestXDiff = xDiff;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private int GetShortcutIndexBelow(int currentIndex)
    {
        if (ShortcutsPanel == null || currentIndex >= ShortcutsPanel.Children.Count - 1)
            return -1;

        var currentButton = ShortcutsPanel.Children[currentIndex] as Button;
        if (currentButton == null) return -1;

        // Get current button's position relative to panel
        var currentPos = currentButton.TransformToVisual(ShortcutsPanel).Transform(new Point(0, 0));
        var currentCenterX = currentPos.X + currentButton.ActualWidth / 2;
        var currentY = currentPos.Y;

        // Find the button closest to current X position on the row below
        int bestIndex = -1;
        double bestXDiff = double.MaxValue;
        double bestY = double.MinValue;

        for (int i = ShortcutsPanel.Children.Count - 1; i > currentIndex; i--)
        {
            var btn = ShortcutsPanel.Children[i] as Button;
            if (btn == null) continue;

            var pos = btn.TransformToVisual(ShortcutsPanel).Transform(new Point(0, 0));
            var centerY = pos.Y + btn.ActualHeight / 2;

            // Must be on a different (lower) row
            if (centerY <= currentY + 5) continue;

            // Find the closest row below
            if (centerY < bestY) continue;

            var centerX = pos.X + btn.ActualWidth / 2;
            var xDiff = Math.Abs(centerX - currentCenterX);

            if (centerY > bestY || (Math.Abs(centerY - bestY) < 5 && xDiff < bestXDiff))
            {
                bestY = centerY;
                bestXDiff = xDiff;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void FocusRunningAppItem(int index)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => FocusRunningAppItem(index));
            return;
        }

        if (runningApps.Count == 0) return;

        index = Math.Max(0, Math.Min(index, runningApps.Count - 1));

        RecentList.SelectedIndex = -1;
        
        navigationTarget = NavigationTarget.RunningAppItem;
        runningAppSelectedIndex = index;
        RunningAppsList.SelectedIndex = index;

        if (!TryFocusRunningAppContainer())
        {
            Dispatcher.BeginInvoke(() => TryFocusRunningAppContainer(), DispatcherPriority.Loaded);
        }
    }

    private bool TryFocusRunningAppContainer()
    {
        var container = RunningAppsList.ItemContainerGenerator.ContainerFromIndex(runningAppSelectedIndex) as ListBoxItem;
        if (container == null) return false;

        container.Focus();
        container.BringIntoView();
        return true;
    }

    private void FocusRecentGameItem(int index)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => FocusRecentGameItem(index));
            return;
        }

        if (items.Count == 0) return;

        index = Math.Max(0, Math.Min(index, items.Count - 1));

        RunningAppsList.SelectedIndex = -1;
        
        navigationTarget = NavigationTarget.RecentGameItem;
        selectedIndex = index;
        RecentList.SelectedIndex = index;

        if (!TryFocusRecentGameContainer())
        {
            Dispatcher.BeginInvoke(() => TryFocusRecentGameContainer(), DispatcherPriority.Loaded);
        }
    }

    private bool TryFocusRecentGameContainer()
    {
        var container = RecentList.ItemContainerGenerator.ContainerFromIndex(selectedIndex) as ListBoxItem;
        if (container == null) return false;

        container.Focus();
        container.BringIntoView();
        return true;
    }
    #endregion

    #region Navigation Commands
    #endregion

    #region Navigation Commands

    private void NavigateUp()
    {
        if (!isInsideSection)
        {
            var prev = GetPreviousVisibleSection(navigationTarget);
            if (prev.HasValue)
            {
                FocusSection(prev.Value);
            }
        }
        else
        {
            switch (navigationTarget)
            {
                case NavigationTarget.SwitchButton:
                    ExitToSectionLevel();
                    break;

                case NavigationTarget.ExitButton:
                    FocusSwitchButton();
                    break;

                case NavigationTarget.VolumeSlider:
                    if (AudioControlsRow.Visibility == Visibility.Visible)
                    {
                        FocusAudioCombo();
                    }
                    else
                    {
                        FocusExitButton();
                    }
                    break;

                case NavigationTarget.MuteBtn:
                    FocusExitButton();
                    break;

                case NavigationTarget.AudioDeviceCombo:
                    FocusExitButton();
                    break;

                case NavigationTarget.RunningAppItem:
                    if (runningAppSelectedIndex <= 0)
                    {
                        ExitToSectionLevel();
                    }
                    else
                    {
                        FocusRunningAppItem(runningAppSelectedIndex - 1);
                    }
                    break;

                case NavigationTarget.ShortcutItem:
                    {
                        // Navigate up in WrapPanel using actual positions
                        int newIndex = GetShortcutIndexAbove(shortcutsSelectedIndex);
                        if (newIndex < 0)
                        {
                            ExitToSectionLevel();
                        }
                        else
                        {
                            FocusShortcutButton(newIndex);
                        }
                    }
                    break;

                case NavigationTarget.RecentGameItem:
                    if (selectedIndex <= 0)
                    {
                        ExitToSectionLevel();
                    }
                    else
                    {
                        FocusRecentGameItem(selectedIndex - 1);
                    }
                    break;

            }
        }
    }

    private void NavigateDown()
    {
        if (!isInsideSection)
        {
            var next = GetNextVisibleSection(navigationTarget);
            if (next.HasValue)
            {
                FocusSection(next.Value);
            }
        }
        else
        {
            switch (navigationTarget)
            {
                case NavigationTarget.SwitchButton:
                    FocusExitButton();
                    break;

                case NavigationTarget.ExitButton:
                    if (AudioControlsRow.Visibility == Visibility.Visible)
                    {
                        FocusAudioCombo();
                    }
                    else if (VolumeControls.Visibility == Visibility.Visible)
                    {
                        FocusVolumeSlider();
                    }
                    else
                    {
                        ExitToSectionLevel();
                    }
                    break;

                case NavigationTarget.VolumeSlider:
                    ExitToSectionLevel();
                    break;

                case NavigationTarget.MuteBtn:
                    if (VolumeControls.Visibility == Visibility.Visible)
                    {
                        FocusVolumeSlider();
                    }
                    else
                    {
                        ExitToSectionLevel();
                    }
                    break;

                case NavigationTarget.AudioDeviceCombo:
                    if (VolumeControls.Visibility == Visibility.Visible)
                    {
                        FocusVolumeSlider();
                    }
                    else
                    {
                        ExitToSectionLevel();
                    }
                    break;

                case NavigationTarget.RunningAppItem:
                    if (runningAppSelectedIndex >= runningApps.Count - 1)
                    {
                        ExitToSectionLevel();
                    }
                    else
                    {
                        FocusRunningAppItem(runningAppSelectedIndex + 1);
                    }
                    break;

                case NavigationTarget.ShortcutItem:
                    {
                        // Navigate down in WrapPanel using actual positions
                        int newIndex = GetShortcutIndexBelow(shortcutsSelectedIndex);
                        if (newIndex < 0)
                        {
                            ExitToSectionLevel();
                        }
                        else
                        {
                            FocusShortcutButton(newIndex);
                        }
                    }
                    break;

                case NavigationTarget.RecentGameItem:
                    if (selectedIndex >= items.Count - 1)
                    {
                        ExitToSectionLevel();
                    }
                    else
                    {
                        FocusRecentGameItem(selectedIndex + 1);
                    }
                    break;

            }
        }
    }

    private void NavigateLeft()
    {
        if (isInsideSection)
        {
            if (navigationTarget == NavigationTarget.VolumeSlider)
            {
                VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
            }
            else if (navigationTarget == NavigationTarget.ExitButton)
            {
                FocusSwitchButton();
            }
            else if (navigationTarget == NavigationTarget.MuteBtn)
            {
                FocusAudioCombo();
            }
            else if (navigationTarget == NavigationTarget.AudioDeviceCombo)
            {
                FocusExitButton();
            }
            else if (navigationTarget == NavigationTarget.ShortcutItem)
            {
                shortcutsSelectedIndex = Math.Max(0, shortcutsSelectedIndex - 1);
                FocusShortcutButton(shortcutsSelectedIndex);
            }
        }
    }

    private void NavigateRight()
    {
        if (isInsideSection)
        {
            if (navigationTarget == NavigationTarget.VolumeSlider)
            {
                VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
            }
            else if (navigationTarget == NavigationTarget.SwitchButton)
            {
                FocusExitButton();
            }
            else if (navigationTarget == NavigationTarget.ExitButton)
            {
                if (AudioControlsRow.Visibility == Visibility.Visible)
                {
                    FocusAudioCombo();
                }
            }
            else if (navigationTarget == NavigationTarget.AudioDeviceCombo)
            {
                if (MuteBtn.Visibility == Visibility.Visible)
                {
                    FocusMuteBtn();
                }
            }
            else if (navigationTarget == NavigationTarget.ShortcutItem)
            {
                shortcutsSelectedIndex = Math.Min(ShortcutsPanel.Children.Count - 1, shortcutsSelectedIndex + 1);
                FocusShortcutButton(shortcutsSelectedIndex);
            }
        }
    }

    private void NavigateTab(bool isShift)
    {
        if (!isInsideSection) return;
        
        if (isShift)
        {
            switch (navigationTarget)
            {
                case NavigationTarget.MuteBtn:
                    FocusAudioCombo();
                    break;
                case NavigationTarget.VolumeSlider:
                    if (MuteBtn.Visibility == Visibility.Visible)
                    {
                        FocusMuteBtn();
                    }
                    else
                    {
                        FocusAudioCombo();
                    }
                    break;
            }
        }
        else
        {
            switch (navigationTarget)
            {
                case NavigationTarget.AudioDeviceCombo:
                    if (MuteBtn.Visibility == Visibility.Visible)
                    {
                        FocusMuteBtn();
                    }
                    else if (VolumeControls.Visibility == Visibility.Visible)
                    {
                        FocusVolumeSlider();
                    }
                    break;
                case NavigationTarget.MuteBtn:
                    if (VolumeControls.Visibility == Visibility.Visible)
                    {
                        FocusVolumeSlider();
                    }
                    break;
            }
        }
    }

    private void PerformAccept()
    {
        if (!isInsideSection)
        {
                    EnterSection(navigationTarget);
        }
        else
        {
            switch (navigationTarget)
            {
                case NavigationTarget.SwitchButton:
                    SwitchBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;
                case NavigationTarget.ExitButton:
                    ExitBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;
                case NavigationTarget.VolumeSlider:
                    break;
                case NavigationTarget.MuteBtn:
                    MuteBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;
                case NavigationTarget.AudioDeviceCombo:
                    AudioDeviceCombo.IsDropDownOpen = true;
                    break;
                case NavigationTarget.RunningAppItem:
                    if (runningAppSelectedIndex >= 0 && runningAppSelectedIndex < runningApps.Count)
                    {
                        var app = runningApps[runningAppSelectedIndex];
                        app.OnSwitch?.Invoke();
                        Close();
                    }
                    break;
                case NavigationTarget.ShortcutItem:
                    if (ShortcutsPanel != null && shortcutsSelectedIndex >= 0 && shortcutsSelectedIndex < ShortcutsPanel.Children.Count)
                    {
                        if (ShortcutsPanel.Children[shortcutsSelectedIndex] is Button button)
                        {
                            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                    }
                    break;
                case NavigationTarget.RecentGameItem:
                    if (selectedIndex >= 0 && selectedIndex < items.Count)
                    {
                        var item = items[selectedIndex];
                        item.OnSelect?.Invoke();
                        Close();
                    }
                    break;

            }
        }
    }

    private void PerformCancel()
    {
        if (AudioDeviceCombo.IsDropDownOpen)
        {
            AudioDeviceCombo.IsDropDownOpen = false;
            return;
        }
        
        if (isInsideSection)
        {
            ExitToSectionLevel();
        }
        else
        {
            Close();
        }
    }

    #endregion

    #region Controller Navigation Wrappers

    internal void ControllerNavigateUp() => NavigateUp();

    internal void ControllerNavigateDown() => NavigateDown();

    internal void ControllerNavigateLeft() => NavigateLeft();

    internal void ControllerNavigateRight() => NavigateRight();

    internal void ControllerAccept() => PerformAccept();

    internal void ControllerCancel() => PerformCancel();

    #endregion

    #region Achievements Display

    private void SetupAchievementsDisplay(GameAchievementSummary? achievements)
    {
        if (achievements == null || !achievements.HasData)
        {
            AchievementsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        AchievementsPanel.Visibility = Visibility.Visible;

        var percentText = achievements.PercentComplete.ToString("F0");
        AchievementsProgress.Text = $"Achievements: {achievements.UnlockedCount}/{achievements.TotalCount} ({percentText}%)";

        RecentAchievementsList.Children.Clear();
        foreach (var achievement in achievements.RecentlyUnlocked)
        {
            var text = new TextBlock
            {
                Text = $"\U0001F3C6 {achievement.Name}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            RecentAchievementsList.Children.Add(text);
        }

        LockedAchievementsList.Children.Clear();
        foreach (var achievement in achievements.LockedToShow)
        {
            var text = new TextBlock
            {
                Text = $"\U0001F512 {achievement.Name}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            LockedAchievementsList.Children.Add(text);
        }
    }

    #endregion

    private void TimeTimer_Tick(object? sender, EventArgs e)
    {
        UpdateTimeDisplay();
    }

    private void UpdateTimeDisplay()
    {
        TimeDisplay.Text = DateTime.Now.ToString("h:mm tt");

        if (currentGameField?.ActivatedTime.HasValue == true && gameSwitcher != null)
        {
            var duration = gameSwitcher.GetSessionDuration(currentGameField.ActivatedTime);
            PlaytimeDisplay.Text = $"Playing for {duration}";
            PlaytimeDisplay.Visibility = Visibility.Visible;
        }
        else
        {
            PlaytimeDisplay.Visibility = Visibility.Collapsed;
        }
    }

    #region Audio Device Setup

    private void SetupAudioDevices(IEnumerable<AudioDevice>? audioDevices)
    {
        if (CurrentGameSection.Visibility != Visibility.Visible ||
            audioDevices == null || !audioDevices.Any())
        {
            AudioControlsRow.Visibility = Visibility.Collapsed;
            VolumeControls.Visibility = Visibility.Collapsed;
            return;
        }

        AudioControlsRow.Visibility = Visibility.Visible;
        
        isInitializingAudio = true;
        AudioDeviceCombo.ItemsSource = audioDevices;

        var defaultDevice = audioDevices.FirstOrDefault(d => d.IsDefault);
        if (defaultDevice != null)
        {
            AudioDeviceCombo.SelectedItem = defaultDevice;
        }
        isInitializingAudio = false;

        var canControlVolume = gameVolumeService != null && currentGameProcessId.HasValue;
        MuteBtn.Visibility = canControlVolume ? Visibility.Visible : Visibility.Collapsed;
        
        if (canControlVolume)
        {
            VolumeControls.Visibility = Visibility.Visible;
            UpdateVolumeDisplay();
        }
        else
        {
            VolumeControls.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateVolumeDisplay()
    {
        if (gameVolumeService == null || !currentGameProcessId.HasValue) return;

        var volume = gameVolumeService.GetVolume(currentGameProcessId.Value);
        var mute = gameVolumeService.GetMute(currentGameProcessId.Value);

        VolumeSlider.Value = (volume ?? 1.0f) * 100;
        MuteBtn.Content = mute == true ? "🔇" : "🔊";
    }

    private void OnVolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (gameVolumeService == null || !currentGameProcessId.HasValue) return;

        var volume = (float)(e.NewValue / 100.0);
        gameVolumeService.SetVolume(currentGameProcessId.Value, volume);
    }

    private void OnMuteBtnClick(object sender, RoutedEventArgs e)
    {
        if (gameVolumeService == null || !currentGameProcessId.HasValue) return;

        var currentMute = gameVolumeService.GetMute(currentGameProcessId.Value);
        var newMute = !(currentMute ?? false);
        if (gameVolumeService.SetMute(currentGameProcessId.Value, newMute))
        {
            MuteBtn.Content = newMute ? "🔇" : "🔊";
        }
    }

    private void OnAudioDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isInitializingAudio) return;
        
        if (AudioDeviceCombo.SelectedItem is AudioDevice selectedDevice && onAudioDeviceChanged != null)
        {
            try
            {
                onAudioDeviceChanged(selectedDevice.Id, (success) =>
                {
                    if (success)
                    {
                        if (AudioDeviceCombo.ItemsSource is IEnumerable<AudioDevice> devices)
                        {
                            foreach (var device in devices)
                            {
                                device.IsDefault = (device.Id == selectedDevice.Id);
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error changing audio device: {ex.Message}");
            }
        }
    }

    #endregion

    #region Shortcuts Setup
    private void SetupShortcuts()
    {
        var buttonsToRemove = new List<Button>();
        foreach (var child in ShortcutsPanel.Children.OfType<Button>())
        {
            buttonsToRemove.Add(child);
        }
        foreach (var button in buttonsToRemove)
        {
            ShortcutsPanel.Children.Remove(button);
        }
        foreach (var shortcut in shortcuts)
        {
            var button = new Button
            {
                Content = shortcut.Label,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(16, 8, 16, 8),
                MinWidth = 110,
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = TransparentBrush,
                BorderThickness = new Thickness(2),
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Cursor = Cursors.Hand,
                Focusable = true,
                IsTabStop = false
            };
            var style = new Style(typeof(Button));
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "ButtonBorder";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenterFactory);
            template.VisualTree = borderFactory;
            
            // Add triggers to ControlTemplate (not Style) so we can use TargetName
            var mouseOverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            mouseOverTrigger.Setters.Add(new Setter { TargetName = "ButtonBorder", Property = Border.BackgroundProperty, Value = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A)) });
            template.Triggers.Add(mouseOverTrigger);
            
            var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter { TargetName = "ButtonBorder", Property = Border.BackgroundProperty, Value = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)) });
            template.Triggers.Add(pressedTrigger);
            
            var focusTrigger = new Trigger { Property = Button.IsKeyboardFocusedProperty, Value = true };
            focusTrigger.Setters.Add(new Setter { TargetName = "ButtonBorder", Property = Border.BorderBrushProperty, Value = new SolidColorBrush(Colors.White) });
            template.Triggers.Add(focusTrigger);
            
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            button.Style = style;
            button.Click += (_, __) =>
            {
                EventHandler? closed = null;
                closed = (s, e2) =>
                {
                    this.Closed -= closed;
                    ExecuteShortcutAction(shortcut);
                };
                this.Closed += closed;
                this.Close();
            };
            ShortcutsPanel.Children.Add(button);
        }
        ShortcutsSection.Visibility = shortcuts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExecuteShortcutAction(Models.OverlayShortcut shortcut)
    {
        try
        {
            if (shortcut.ActionType == ShortcutActionType.SendInput)
            {
                logger.Info($"ExecuteShortcutAction: Sending hotkey '{shortcut.Hotkey}'");
                System.Threading.Tasks.Task.Run(() => NativeInput.SendHotkey(shortcut.Hotkey));
                return;
            }

            if (!string.IsNullOrWhiteSpace(shortcut.Command))
            {
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = shortcut.Command,
                    Arguments = shortcut.Arguments ?? string.Empty,
                    UseShellExecute = true
                };
                var commandDirectory = Path.GetDirectoryName(shortcut.Command);
                if (!string.IsNullOrEmpty(commandDirectory))
                {
                    processStartInfo.WorkingDirectory = commandDirectory;
                }
                System.Diagnostics.Process.Start(processStartInfo);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Failed to execute shortcut action '{shortcut.Label}'");
        }
    }


    #endregion

    #region Window Lifecycle

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        Win32Window.ActivateOverlayWindow(hwnd);

    }

    private void OnClosingWithFade(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (isClosing)
        {
            return;
        }
        e.Cancel = true;
        timeTimer.Stop();
        isClosing = true;
        try
        {
            var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(60)))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            anim.Completed += (_, __) =>
            {
                this.Closing -= OnClosingWithFade;
                try { RootCard.Opacity = 0; } catch { }
                this.Close();
            };
            RootCard.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        catch
        {
            this.Closing -= OnClosingWithFade;
            this.Close();
        }
    }

    #endregion

    private enum NavigationTarget
    {
        CurrentGameSection,
        ShortcutsSection,
        RunningAppsSection,
        RecentGamesSection,
        SwitchButton,
        ExitButton,
        VolumeSlider,
        MuteBtn,
        AudioDeviceCombo,
        ShortcutItem,
        RunningAppItem,
        RecentGameItem,
    }
}
