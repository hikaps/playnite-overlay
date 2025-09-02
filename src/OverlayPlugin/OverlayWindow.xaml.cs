using System;
using System.Windows;
using System.Windows.Input;

namespace PlayniteOverlay;

public partial class OverlayWindow : Window
{
    private readonly Action onSwitch;
    private readonly Action onExit;

    public OverlayWindow(Action onSwitch, Action onExit, string title)
    {
        InitializeComponent();
        this.onSwitch = onSwitch;
        this.onExit = onExit;

        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Playnite Overlay" : title;

        SwitchBtn.Click += (_, __) => this.onSwitch();
        ExitBtn.Click += (_, __) => this.onExit();
        Backdrop.MouseLeftButtonDown += (_, __) => this.Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) this.Close(); };
        Loaded += (_, __) => { Activate(); Focus(); Keyboard.Focus(this); };
    }
}
