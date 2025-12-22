using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PlayniteOverlay.Models;

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
    private readonly List<OverlayItem> items;
    private readonly List<RunningApp> runningApps;
    
    // Two-level navigation state
    private NavigationTarget navigationTarget = NavigationTarget.CurrentGameSection;
    private bool isInsideSection = false;
    private int selectedIndex = -1;
    private int runningAppSelectedIndex = -1;
    private bool isClosing;
    
    // Section highlight color
    private static readonly SolidColorBrush HighlightBrush = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public OverlayWindow(Action onSwitch, Action onExit, OverlayItem? currentGame, IEnumerable<RunningApp> runningApps, IEnumerable<OverlayItem> recentGames)
    {
        InitializeComponent();
        this.onSwitch = onSwitch;
        this.onExit = onExit;
        this.items = new List<OverlayItem>(recentGames);
        this.runningApps = new List<RunningApp>(runningApps);

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
            
            // Setup backdrop image (only if background image available)
            if (!string.IsNullOrWhiteSpace(currentGame.BackgroundImagePath))
            {
                try
                {
                    BackdropImage.Source = new BitmapImage(new Uri(currentGame.BackgroundImagePath, UriKind.RelativeOrAbsolute));
                    BackdropImage.Visibility = Visibility.Visible;
                }
                catch
                {
                    // Failed to load, keep hidden (will show solid dark background)
                }
            }
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
                
                // Animate backdrop if visible (fade to 0.6 opacity)
                if (BackdropImage.Visibility == Visibility.Visible)
                {
                    var backdropAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 0.6, new Duration(TimeSpan.FromMilliseconds(200)))
                    {
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    BackdropImage.BeginAnimation(UIElement.OpacityProperty, backdropAnim);
                }
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
            case Key.Enter:
                PerformAccept();
                break;
            case Key.Space:
                // Let WPF handle Space for button activation, don't mark as handled
                // The key still won't pass to the game because the overlay has focus
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
                return null; // Only one section visible
                
            case NavigationTarget.RunningAppsSection:
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                // Wrap
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                return null;
                
            case NavigationTarget.RecentGamesSection:
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
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                return null; // Only one section visible
                
            case NavigationTarget.RunningAppsSection:
                if (items.Count > 0) return NavigationTarget.RecentGamesSection;
                // Wrap
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
                return null;
                
            case NavigationTarget.RecentGamesSection:
                // Wrap to first visible section
                if (CurrentGameSection.Visibility == Visibility.Visible) return NavigationTarget.CurrentGameSection;
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
                section = NavigationTarget.CurrentGameSection;
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
                    // At bottom of CurrentGameSection, exit to section level
                    ExitToSectionLevel();
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
            }
        }
    }

    private void NavigateLeft()
    {
        if (isInsideSection && navigationTarget == NavigationTarget.ExitButton)
        {
            FocusSwitchButton();
        }
    }

    private void NavigateRight()
    {
        if (isInsideSection && navigationTarget == NavigationTarget.SwitchButton)
        {
            FocusExitButton();
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
            }
        }
    }

    private void PerformCancel()
    {
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
        RunningAppsSection,
        RecentGamesSection,
        
        // Item-level (Level 2) - inside CurrentGameSection
        SwitchButton,
        ExitButton,
        
        // Item-level (Level 2) - inside list sections
        RunningAppItem,
        RecentGameItem,
    }
}
