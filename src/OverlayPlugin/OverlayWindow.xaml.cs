using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Collections.Generic;

namespace PlayniteOverlay;

public partial class OverlayWindow : Window
{
    private readonly Action onSwitch;
    private readonly Action onExit;
    private readonly List<OverlayItem> items;
    private bool isClosing;

    public OverlayWindow(Action onSwitch, Action onExit, string title, IEnumerable<OverlayItem> items)
    {
        InitializeComponent();
        this.onSwitch = onSwitch;
        this.onExit = onExit;
        this.items = new List<OverlayItem>(items);

        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Playnite Overlay" : title;

        // Set cover image if any from first item (approximation); could be set to current game's cover if desired
        if (this.items.Count > 0 && !string.IsNullOrWhiteSpace(this.items[0].ImagePath))
        {
            try
            {
                CoverImage.Source = new BitmapImage(new System.Uri(this.items[0].ImagePath, System.UriKind.RelativeOrAbsolute));
            }
            catch { /* ignore invalid URIs */ }
        }

        RecentList.ItemsSource = this.items;
        RecentList.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler(OnRecentPlayClick));

        SwitchBtn.Click += (_, __) =>
        {
            // Handover strategy: trigger Playnite restore first, keep overlay up
            // until a visible window from this process appears or foreground changes.
            try { SwitchBtn.IsEnabled = false; } catch { }
            try { this.onSwitch(); } catch { }

            IntPtr overlayHwnd = IntPtr.Zero;
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                overlayHwnd = helper.Handle;
            }
            catch { }

            var started = DateTime.UtcNow;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            EventHandler? tick = null;
            tick = (_, ____) =>
            {
                var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
                var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var fg = Win32Window.GetForeground();
                var anyVisible = Win32Window.GetVisibleWindowForProcess(pid);
                bool foregroundMoved = fg != IntPtr.Zero && overlayHwnd != IntPtr.Zero && fg != overlayHwnd;

                if (foregroundMoved || anyVisible != IntPtr.Zero || elapsed > 1500)
                {
                    timer.Stop();
                    timer.Tick -= tick;
                    // Now close overlay (fade out will run)
                    this.Close();
                }
            };
            timer.Tick += tick;
            timer.Start();
        };
        ExitBtn.Click += (_, __) => this.onExit();
        Backdrop.MouseLeftButtonDown += (_, __) => this.Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) this.Close(); };
        Loaded += (_, __) =>
        {
            Activate(); Focus(); Keyboard.Focus(this);
            try
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180)))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                RootCard.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            catch { }
        };
        Closing += OnClosingWithFade;
    }

    private void OnRecentPlayClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button btn && btn.CommandParameter is OverlayItem item)
        {
            item.OnSelect?.Invoke();
            Close();
        }
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
            var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(120)))
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
            try
            {
                var backAnim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(120)))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };
                Backdrop.BeginAnimation(UIElement.OpacityProperty, backAnim);
            }
            catch { }
        }
        catch
        {
            // If animation fails, detach and close immediately
            this.Closing -= OnClosingWithFade;
            this.Close();
        }
    }
}
