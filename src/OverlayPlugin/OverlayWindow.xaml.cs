using System;
using System.Windows;

namespace PlayniteOverlay;

public partial class OverlayWindow : Window
{
    private readonly Action onSwitch;
    private readonly Action onExit;

    public OverlayWindow(Action onSwitch, Action onExit)
    {
        InitializeComponent();
        this.onSwitch = onSwitch;
        this.onExit = onExit;

        SwitchBtn.Click += (_, __) => this.onSwitch();
        ExitBtn.Click += (_, __) => this.onExit();
        MouseDown += (_, e) => { if (e.ChangedButton == System.Windows.Input.MouseButton.Left) this.DragMove(); };
    }
}

