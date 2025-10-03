using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using PlayniteOverlay.Models;

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
        }
        catch
        {
            // If animation fails, detach and close immediately
            this.Closing -= OnClosingWithFade;
            this.Close();
        }
    }
}
