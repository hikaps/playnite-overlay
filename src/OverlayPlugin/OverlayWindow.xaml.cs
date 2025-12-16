using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PlayniteOverlay.Models;
using PlayniteOverlay.Input;

namespace PlayniteOverlay;

public partial class OverlayWindow : Window
{
    private readonly Action onSwitch;
    private readonly Action onExit;
    private readonly List<OverlayItem> items;
    private readonly List<RunningApp> runningApps;
    private readonly bool enableControllerNavigation;
    private OverlayControllerNavigator? controllerNavigator;
    private NavigationTarget navigationTarget = NavigationTarget.RecentList;
    private int selectedIndex = -1;
    private int runningAppSelectedIndex = -1;
    private bool isClosing;

    public OverlayWindow(Action onSwitch, Action onExit, OverlayItem? currentGame, IEnumerable<RunningApp> runningApps, IEnumerable<OverlayItem> recentGames, bool enableControllerNavigation)
    {
        InitializeComponent();
        this.onSwitch = onSwitch;
        this.onExit = onExit;
        this.items = new List<OverlayItem>(recentGames);
        this.runningApps = new List<RunningApp>(runningApps);
        this.enableControllerNavigation = enableControllerNavigation;

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

        // Initial focus - priority: RunningApps → RecentList → SwitchButton
        if (this.runningApps.Count > 0)
        {
            runningAppSelectedIndex = 0;
            navigationTarget = NavigationTarget.RunningAppsList;
            Dispatcher.BeginInvoke(() => FocusRunningAppItem(0), DispatcherPriority.Loaded);
        }
        else if (this.items.Count > 0)
        {
            selectedIndex = 0;
            navigationTarget = NavigationTarget.RecentList;
            Dispatcher.BeginInvoke(() => FocusListItem(selectedIndex), DispatcherPriority.Loaded);
        }
        else if (currentGame != null)
        {
            navigationTarget = NavigationTarget.SwitchButton;
            Dispatcher.BeginInvoke(FocusSwitchButton, DispatcherPriority.Loaded);
        }

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
        KeyDown += (_, e) => { if (e.Key == Key.Escape) this.Close(); };
        
        Loaded += (_, __) =>
        {
            Activate(); Focus(); Keyboard.Focus(this);
            if (this.enableControllerNavigation)
            {
                controllerNavigator = new OverlayControllerNavigator(this);
            }
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
        
        Closed += (_, __) =>
        {
            controllerNavigator?.Dispose();
            controllerNavigator = null;
        };
        
        Closing += OnClosingWithFade;
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

    internal void ControllerNavigateUp()
    {
        if (!enableControllerNavigation)
        {
            return;
        }

        switch (navigationTarget)
        {
            case NavigationTarget.SwitchButton:
                // Go up to RecentList last item, or RunningApps, or wrap
                if (items.Count > 0)
                {
                    FocusListItem(items.Count - 1);
                }
                else if (runningApps.Count > 0)
                {
                    FocusRunningAppItem(runningApps.Count - 1);
                }
                else
                {
                    FocusExitButton(); // wrap
                }
                break;

            case NavigationTarget.ExitButton:
                FocusSwitchButton();
                break;

            case NavigationTarget.RunningAppsList:
                if (runningAppSelectedIndex <= 0)
                {
                    // Top of running apps → go to CurrentGame buttons or wrap
                    if (CurrentGameSection.Visibility == Visibility.Visible)
                    {
                        FocusExitButton();
                    }
                    else if (items.Count > 0)
                    {
                        FocusListItem(items.Count - 1); // wrap to bottom
                    }
                    else
                    {
                        FocusSwitchButton(); // wrap to buttons
                    }
                }
                else
                {
                    FocusRunningAppItem(runningAppSelectedIndex - 1);
                }
                break;

            case NavigationTarget.RecentList:
                if (selectedIndex <= 0)
                {
                    // Top of recent → go to RunningApps or CurrentGame
                    if (runningApps.Count > 0)
                    {
                        FocusRunningAppItem(runningApps.Count - 1);
                    }
                    else if (CurrentGameSection.Visibility == Visibility.Visible)
                    {
                        FocusExitButton();
                    }
                    else if (items.Count > 0)
                    {
                        FocusListItem(items.Count - 1); // wrap
                    }
                }
                else
                {
                    FocusListItem(selectedIndex - 1);
                }
                break;
        }
    }

    internal void ControllerNavigateDown()
    {
        if (!enableControllerNavigation)
        {
            return;
        }

        switch (navigationTarget)
        {
            case NavigationTarget.SwitchButton:
                FocusExitButton();
                break;

            case NavigationTarget.ExitButton:
                // Go to RunningApps first, then RecentList
                if (runningApps.Count > 0)
                {
                    FocusRunningAppItem(0);
                }
                else if (items.Count > 0)
                {
                    FocusListItem(0);
                }
                else
                {
                    FocusSwitchButton(); // wrap
                }
                break;

            case NavigationTarget.RunningAppsList:
                if (runningAppSelectedIndex >= runningApps.Count - 1)
                {
                    // End of running apps → go to RecentList or buttons
                    if (items.Count > 0)
                    {
                        FocusListItem(0);
                    }
                    else if (CurrentGameSection.Visibility == Visibility.Visible)
                    {
                        FocusSwitchButton();
                    }
                    else
                    {
                        FocusRunningAppItem(0); // wrap
                    }
                }
                else
                {
                    FocusRunningAppItem(runningAppSelectedIndex + 1);
                }
                break;

            case NavigationTarget.RecentList:
                if (items.Count == 0)
                {
                    FocusSwitchButton();
                    break;
                }
                if (selectedIndex >= items.Count - 1)
                {
                    // End of list → go to buttons or wrap
                    if (CurrentGameSection.Visibility == Visibility.Visible)
                    {
                        FocusSwitchButton();
                    }
                    else if (runningApps.Count > 0)
                    {
                        FocusRunningAppItem(0); // wrap to top
                    }
                    else
                    {
                        FocusListItem(0); // wrap
                    }
                }
                else
                {
                    FocusListItem(selectedIndex + 1);
                }
                break;
        }
    }

    internal void ControllerNavigateLeft()
    {
        if (!enableControllerNavigation)
        {
            return;
        }

        if (navigationTarget == NavigationTarget.ExitButton)
        {
            FocusSwitchButton();
        }
    }

    internal void ControllerNavigateRight()
    {
        if (!enableControllerNavigation)
        {
            return;
        }

        if (navigationTarget == NavigationTarget.SwitchButton)
        {
            FocusExitButton();
        }
    }

    internal void ControllerAccept()
    {
        if (!enableControllerNavigation)
        {
            return;
        }

        switch (navigationTarget)
        {
            case NavigationTarget.RunningAppsList:
                if (runningAppSelectedIndex >= 0 && runningAppSelectedIndex < runningApps.Count)
                {
                    var app = runningApps[runningAppSelectedIndex];
                    app.OnSwitch?.Invoke();
                    Close();
                }
                break;
            case NavigationTarget.RecentList:
                if (selectedIndex >= 0 && selectedIndex < items.Count)
                {
                    var item = items[selectedIndex];
                    item.OnSelect?.Invoke();
                    Close();
                }
                break;
            case NavigationTarget.SwitchButton:
                SwitchBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                break;
            case NavigationTarget.ExitButton:
                ExitBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                break;
        }
    }

    internal void ControllerCancel()
    {
        if (!enableControllerNavigation)
        {
            return;
        }

        Close();
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

    private void FocusListItem(int index)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => FocusListItem(index));
            return;
        }

        if (items.Count == 0)
        {
            FocusSwitchButton();
            return;
        }

        if (index < 0)
        {
            index = 0;
        }
        else if (index >= items.Count)
        {
            index = items.Count - 1;
        }

        navigationTarget = NavigationTarget.RecentList;
        selectedIndex = index;
        RecentList.SelectedIndex = selectedIndex;

        // Scroll the selected item into view
        if (selectedIndex >= 0 && selectedIndex < RecentList.Items.Count)
        {
            RecentList.ScrollIntoView(RecentList.Items[selectedIndex]);
        }

        if (!TryFocusListContainer())
        {
            Dispatcher.BeginInvoke((Action)(() => TryFocusListContainer()), DispatcherPriority.Loaded);
        }
    }

    private bool TryFocusListContainer()
    {
        var container = RecentList.ItemContainerGenerator.ContainerFromIndex(selectedIndex) as ListBoxItem;
        if (container == null)
        {
            return false;
        }

        container.Focus();
        return true;
    }

    private void FocusSwitchButton()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(FocusSwitchButton);
            return;
        }

        navigationTarget = NavigationTarget.SwitchButton;
        if (items.Count > 0 && selectedIndex < 0)
        {
            selectedIndex = 0;
        }
        SwitchBtn.Focus();
    }

    private void FocusExitButton()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(FocusExitButton);
            return;
        }

        navigationTarget = NavigationTarget.ExitButton;
        ExitBtn.Focus();
    }

    private void FocusRunningAppItem(int index)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => FocusRunningAppItem(index));
            return;
        }

        if (runningApps.Count == 0)
        {
            // Fall through to next section
            if (items.Count > 0)
            {
                FocusListItem(0);
            }
            else
            {
                FocusSwitchButton();
            }
            return;
        }

        if (index < 0)
        {
            index = 0;
        }
        else if (index >= runningApps.Count)
        {
            index = runningApps.Count - 1;
        }

        navigationTarget = NavigationTarget.RunningAppsList;
        runningAppSelectedIndex = index;
        RunningAppsList.SelectedIndex = index;

        // Scroll section into view (center the section)
        RunningAppsSection.BringIntoView();

        if (!TryFocusRunningAppContainer())
        {
            Dispatcher.BeginInvoke(() => TryFocusRunningAppContainer(), DispatcherPriority.Loaded);
        }
    }

    private bool TryFocusRunningAppContainer()
    {
        var container = RunningAppsList.ItemContainerGenerator.ContainerFromIndex(runningAppSelectedIndex) as ListBoxItem;
        if (container == null)
        {
            return false;
        }

        container.Focus();
        return true;
    }

    private enum NavigationTarget
    {
        RecentList,
        RunningAppsList,
        SwitchButton,
        ExitButton
    }
}
