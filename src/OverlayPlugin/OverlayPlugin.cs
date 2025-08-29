using System;
using System.Threading;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace PlayniteOverlay;

public class OverlayPlugin : GenericPlugin
{
    public static readonly Guid PluginId = new ("11111111-2222-3333-4444-555555555555");

    private readonly ILogger logger;
    private readonly InputListener input;
    private readonly OverlayService overlay;
    private readonly GameSwitcher switcher;

    public OverlayPlugin(IPlayniteAPI api) : base(api)
    {
        logger = LogManager.GetLogger();
        input = new InputListener();
        overlay = new OverlayService();
        switcher = new GameSwitcher(api);

        input.ToggleRequested += (_, __) => ToggleOverlay();
    }

    public override Guid Id => PluginId;

    public override void OnGameStarted(OnGameStartedEventArgs args)
    {
        input.Start();
    }

    public override void OnGameStopped(OnGameStoppedEventArgs args)
    {
        input.Stop();
        overlay.Hide();
    }

    private void ToggleOverlay()
    {
        if (overlay.IsVisible)
        {
            overlay.Hide();
        }
        else
        {
            overlay.Show(() =>
            {
                // Example callbacks; wire to UI commands in OverlayUI
                switcher.SwitchToNextRecommended();
            },
            () =>
            {
                switcher.ExitCurrent();
            });
        }
    }
}

public class InputListener
{
    private Timer? timer;
    public event EventHandler? ToggleRequested;

    public void Start()
    {
        timer ??= new Timer(_ =>
        {
            // TODO: Poll XInputGetKeystroke for Guide button.
            // Placeholder for development without native bindings.
        }, null, 0, 50);
    }

    public void Stop()
    {
        timer?.Dispose();
        timer = null;
    }

    // For keyboard fallback during dev/testing
    public void TriggerToggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);
}

public class OverlayService
{
    public bool IsVisible { get; private set; }

    public void Show(Action onSwitch, Action onExit)
    {
        IsVisible = true;
        // TODO: Launch WPF overlay window via OverlayUI with provided callbacks.
    }

    public void Hide()
    {
        IsVisible = false;
        // TODO: Close overlay window and restore focus to game.
    }
}

public class GameSwitcher
{
    private readonly IPlayniteAPI api;
    public GameSwitcher(IPlayniteAPI api) => this.api = api;

    public void SwitchToNextRecommended()
    {
        // TODO: Use api.Database.Games and api.StartGame(game) to switch.
    }

    public void ExitCurrent()
    {
        // TODO: Ask Playnite to stop current game or kill process with confirmation.
    }
}

