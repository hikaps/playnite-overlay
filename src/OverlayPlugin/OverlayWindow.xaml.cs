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
using PlayniteOverlay.Models;
using PlayniteOverlay.Services;

namespace PlayniteOverlay;

public partial class OverlayWindow : Window
{
    // P/Invoke for extended window styles (hide from Alt+Tab)
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
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
    
    // Two-level navigation state
    private NavigationTarget navigationTarget = NavigationTarget.CurrentGameSection;
    private bool isInsideSection = false;
    private int selectedIndex = -1;
    private int runningAppSelectedIndex = -1;
    private int shortcutsSelectedIndex = -1;
    private bool isClosing;
    private bool isInitializingAudio = false;
    
    // Section highlight color
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

        // Initialize time display timer (updates every second)
        timeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timeTimer.Tick += TimeTimer_Tick;
        timeTimer.Start();

        // Update immediately on load
        UpdateTimeDisplay();

        // Setup current game section
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
                    // Cover failed to load, image will remain empty
                }
            }

            // Setup achievements section
            SetupAchievementsDisplay(currentGame.Achievements);
        }
        else
        {
            CurrentGameSection.Visibility = Visibility.Collapsed;
        }

        // Setup running apps section
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

        // Setup recent games list
        RecentList.ItemsSource = this.items;
        
        // Show empty state if no recent games
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

        // Setup audio devices section
        SetupAudioDevices(audioDevices);

        // Setup shortcuts section
        SetupShortcuts();

        VolumeSlider.ValueChanged += OnVolumeSliderChanged;
        MuteBtn.Click += OnMuteBtnClick;

        // Initial focus - section level on first visible section
        Dispatcher.BeginInvoke(() => FocusFirstVisibleSection(), DispatcherPriority.Loaded);

        SwitchBtn.Click += (_, __) =>
        {
            // Close overlay first, then switch/activate Playnite after window fully closed
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
            // Close overlay first, then exit game after window fully closed
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
        
        Loaded += (_, __) =>
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
        
        Closing += OnClosingWithFade;
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
        // If audio ComboBox dropdown is open, let it handle its own navigation
        if (AudioDeviceCombo.IsDropDownOpen && 
            (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter))
        {
            // Don't mark as handled - let the ComboBox receive these keys
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
        
        // Consume all other keystrokes - don't pass any input to underlying applications
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
        
        // Clear all item selections
        RunningAppsList.SelectedIndex = -1;
        RecentList.SelectedIndex = -1;
        
        // Clear all section highlights
        ClearSectionHighlights();
        
        // Highlight the target section and move keyboard focus to it
        // This removes focus from buttons (hiding their borders) while keeping keyboard input working
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
                // Wrap to last visible section
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                if (shortcuts.Count > 0) return NavigationTarget.ShortcutsSection;
                return null; // Only one section visible
                
            case NavigationTarget.ShortcutsSection:
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                // Wrap
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                return null;
                
            case NavigationTarget.RunningAppsSection:
                if (ShortcutsSection.Visibility == Visibility.Visible) return NavigationTarget.ShortcutsSection;
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                // Wrap
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                return null;
                
            case NavigationTarget.RecentGamesSection:
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                if (ShortcutsSection.Visibility == Visibility.Visible) return NavigationTarget.ShortcutsSection;
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
                if (shortcuts.Count > 0) return NavigationTarget.ShortcutsSection;
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                return null; // Only one section visible
                
            case NavigationTarget.ShortcutsSection:
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                // Wrap
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                return null;
                
            case NavigationTarget.RunningAppsSection:
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                // Wrap
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                if (ShortcutsSection.Visibility == Visibility.Visible) return NavigationTarget.ShortcutsSection;
                return null;
                
            case NavigationTarget.RecentGamesSection:
                // Wrap to first visible section
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                if (shortcuts.Count > 0) return NavigationTarget.ShortcutsSection;
                if (runningApps.Count > 0) return NavigationTarget.RunningAppsSection;
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
                if (shortcuts.Count > 0)
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
        // Determine which section we're in based on current navigation target
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

    private void FocusShortcutButton(int index)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => FocusShortcutButton(index));
            return;
        }

        if (index >= 0 && index < ShortcutsPanel.Children.Count)
        {
            var button = ShortcutsPanel.Children[index] as Button;
            if (button != null)
            {
                button.Focus();
                ShortcutsSection.BringIntoView();
            }
        }
    }

    #endregion

    #region Navigation Commands

    private void NavigateUp()
    {
        if (!isInsideSection)
        {
            // Section-level: move to previous section
            var prev = GetPreviousVisibleSection(navigationTarget);
            if (prev.HasValue)
            {
                FocusSection(prev.Value);
            }
        }
        else
        {
            // Item-level navigation
            switch (navigationTarget)
            {
                case NavigationTarget.SwitchButton:
                    // Already at top of CurrentGameSection, exit to section level
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
                case NavigationTarget.ShortcutItem:
                    if (shortcutsSelectedIndex <= 0)
                    {
                        ExitToSectionLevel();
                    }
                    else
                    {
                        shortcutsSelectedIndex = shortcutsSelectedIndex - 1;
                        FocusShortcutButton(shortcutsSelectedIndex);
                    }
                    break;
            }
        }
    }

    private void NavigateDown()
    {
        if (!isInsideSection)
        {
            // Section-level: move to next section
            var next = GetNextVisibleSection(navigationTarget);
            if (next.HasValue)
            {
                FocusSection(next.Value);
            }
        }
        else
        {
            // Item-level navigation
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
                case NavigationTarget.ShortcutItem:
                    if (shortcutsSelectedIndex >= shortcuts.Count - 1)
                    {
                        ExitToSectionLevel();
                    }
                    else
                    {
                        shortcutsSelectedIndex = shortcutsSelectedIndex + 1;
                        FocusShortcutButton(shortcutsSelectedIndex);
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
                shortcutsSelectedIndex = Math.Min(shortcuts.Count - 1, shortcutsSelectedIndex + 1);
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
            // Section-level: drill into the section
            EnterSection(navigationTarget);
        }
        else
        {
            // Item-level: perform action
            switch (navigationTarget)
            {
                case NavigationTarget.SwitchButton:
                    SwitchBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;
                case NavigationTarget.ExitButton:
                    ExitBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;
                case NavigationTarget.VolumeSlider:
                    // Volume slider adjustment handled via Left/Right keys
                    break;
                case NavigationTarget.MuteBtn:
                    MuteBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;
                case NavigationTarget.AudioDeviceCombo:
                    // Open the ComboBox dropdown when Enter is pressed
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
                case NavigationTarget.RecentGameItem:
                    if (selectedIndex >= 0 && selectedIndex < items.Count)
                    {
                        var item = items[selectedIndex];
                        item.OnSelect?.Invoke();
                        Close();
                    }
                    break;
                case NavigationTarget.ShortcutItem:
                    if (shortcutsSelectedIndex >= 0 && shortcutsSelectedIndex < ShortcutsPanel.Children.Count)
                    {
                        var button = ShortcutsPanel.Children[shortcutsSelectedIndex] as Button;
                        button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    break;
            }
        }
    }

    private void PerformCancel()
    {
        // If the audio ComboBox dropdown is open, close it first
        if (AudioDeviceCombo.IsDropDownOpen)
        {
            AudioDeviceCombo.IsDropDownOpen = false;
            return;
        }
        
        if (isInsideSection)
        {
            // Exit to section level
            ExitToSectionLevel();
        }
        else
        {
            // Close overlay
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

        // Display progress: "Achievements: 15/50 (30%)"
        var percentText = achievements.PercentComplete.ToString("F0");
        AchievementsProgress.Text = $"Achievements: {achievements.UnlockedCount}/{achievements.TotalCount} ({percentText}%)";

        // Display recently unlocked achievements
        RecentAchievementsList.Children.Clear();
        foreach (var achievement in achievements.RecentlyUnlocked)
        {
            var text = new TextBlock
            {
                Text = $"\U0001F3C6 {achievement.Name}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), // Gold color
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            RecentAchievementsList.Children.Add(text);
        }

        // Display locked achievements
        LockedAchievementsList.Children.Clear();
        foreach (var achievement in achievements.LockedToShow)
        {
            var text = new TextBlock
            {
                Text = $"\U0001F512 {achievement.Name}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), // Gray color
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
        // Update current time (12-hour format)
        TimeDisplay.Text = DateTime.Now.ToString("h:mm tt");

        // Update playtime display if game is active
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
        // Skip during initialization to avoid triggering device switch on setup
        if (isInitializingAudio) return;
        
        if (AudioDeviceCombo.SelectedItem is AudioDevice selectedDevice && onAudioDeviceChanged != null)
        {
            try
            {
                // Call the callback with the device ID and a success handler
                onAudioDeviceChanged(selectedDevice.Id, (success) =>
                {
                    if (success)
                    {
                        // Update IsDefault flags to reflect the new default device
                        // This will trigger UI refresh thanks to INotifyPropertyChanged
                        if (AudioDeviceCombo.ItemsSource is IEnumerable<AudioDevice> devices)
                        {
                            foreach (var device in devices)
                            {
                                device.IsDefault = (device.Id == selectedDevice.Id);
                            }
                        }
                    }
                    // If not successful, do nothing - the UI stays as-is (fail silently)
                });
            }
            catch (Exception ex)
            {
                // Log error but don't crash - this is a non-critical feature
                System.Diagnostics.Debug.WriteLine($"Error changing audio device: {ex.Message}");
            }
        }
    }

    #endregion

    #region Shortcuts Setup

    private void SetupShortcuts()
    {
        ShortcutsPanel.Children.Clear();

        foreach (var shortcut in shortcuts)
        {
            var button = new Button
            {
                Content = shortcut.Label,
                Margin = new Thickness(0, 0, 8, 0),
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

            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            style.Triggers.Add(new Trigger { Property = Button.IsMouseOverProperty, Value = true, Setters = { new Setter { Property = Button.BackgroundProperty, Value = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A)) } } });
            style.Triggers.Add(new Trigger { Property = Button.IsPressedProperty, Value = true, Setters = { new Setter { Property = Button.BackgroundProperty, Value = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)) } } });
            style.Triggers.Add(new Trigger { Property = Button.IsKeyboardFocusedProperty, Value = true, Setters = { new Setter { Property = Button.BorderBrushProperty, Value = new SolidColorBrush(Colors.White) } } });

            button.Style = style;

            button.Click += (_, __) =>
            {
                EventHandler? closed = null;
                closed = (s, e2) =>
                {
                    this.Closed -= closed;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(shortcut.Command))
                        {
                            var processStartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = shortcut.Command,
                                Arguments = shortcut.Arguments ?? ""
                            };

                            // Set working directory to command folder when available
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
                        System.Diagnostics.Debug.WriteLine($"Error running shortcut command: {ex.Message}");
                    }
                };
                this.Closed += closed;
                this.Close();
            };

            ShortcutsPanel.Children.Add(button);
        }

        ShortcutsSection.Visibility = shortcuts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Window Lifecycle

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // Hide from Alt+Tab (no WPF alternative for this)
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        // Reliably steal focus from the game using AttachThreadInput pattern.
        // This ensures the overlay receives keyboard/controller input instead of
        // the input bleeding through to the underlying game.
        Win32Window.ActivateOverlayWindow(hwnd);

        // Topmost is now handled by WPF via Topmost="True" in XAML
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
                // Detach handler to avoid re-entrancy and blinking
                this.Closing -= OnClosingWithFade;
                try { RootCard.Opacity = 0; } catch { }
                this.Close();
            };
            RootCard.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        catch
        {
            // If animation fails, detach and close immediately
            this.Closing -= OnClosingWithFade;
            this.Close();
        }
    }

    #endregion

    private enum NavigationTarget
    {
        // Section-level (Level 1)
        CurrentGameSection,
        ShortcutsSection,
        RunningAppsSection,
        RecentGamesSection,

        // Item-level (Level 2) - inside CurrentGameSection
        SwitchButton,
        ExitButton,
        VolumeSlider,
        MuteBtn,
        AudioDeviceCombo,

        // Item-level (Level 2) - inside list sections
        ShortcutItem,
        RunningAppItem,
        RecentGameItem,
    }
}
