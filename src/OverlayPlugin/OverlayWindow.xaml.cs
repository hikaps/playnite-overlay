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

        SwitchBtn.Click += (_, __) => this.onSwitch();
        ExitBtn.Click += (_, __) => this.onExit();
        Backdrop.MouseLeftButtonDown += (_, __) => this.Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) this.Close(); };
        Loaded += (_, __) => { Activate(); Focus(); Keyboard.Focus(this); };
    }

    private void OnRecentPlayClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button btn && btn.CommandParameter is OverlayItem item)
        {
            item.OnSelect?.Invoke();
            Close();
        }
    }
}
