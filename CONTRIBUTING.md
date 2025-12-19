# Developer Guide

This document provides guidelines for developers working on the Playnite Overlay plugin, including project structure, architecture, build commands, and coding conventions.

## Project Structure & Module Organization

```
playnite-overlay/
├── src/
│   ├── OverlayPlugin/           # Main plugin project
│   │   ├── Input/               # Input handling (hotkeys, controllers)
│   │   │   ├── InputListener.cs
│   │   │   └── OverlayControllerNavigator.cs
│   │   ├── Interop/             # Win32 API interop
│   │   │   ├── HotkeyManager.cs
│   │   │   ├── Monitors.cs
│   │   │   ├── Win32Window.cs
│   │   │   └── XInput.cs
│   │   ├── Models/              # Data models
│   │   │   ├── OverlayItem.cs
│   │   │   └── RunningApp.cs
│   │   ├── Services/            # Core business logic
│   │   │   ├── GameSwitcher.cs
│   │   │   ├── OverlayService.cs
│   │   │   └── RunningAppsDetector.cs
│   │   ├── Settings/            # Plugin settings
│   │   │   ├── OverlaySettings.cs
│   │   │   ├── OverlaySettingsView.xaml
│   │   │   ├── OverlaySettingsView.xaml.cs
│   │   │   └── OverlaySettingsViewModel.cs
│   │   ├── Utils/               # Shared utilities
│   │   │   └── ProcessMatchingUtils.cs
│   │   ├── OverlayPlugin.cs     # Plugin entry point
│   │   ├── OverlayWindow.xaml   # Overlay UI
│   │   └── OverlayWindow.xaml.cs
├── tests/
│   └── OverlayPlugin.Tests/     # Unit tests
│       ├── ComboMaskTests.cs
│       ├── GameSwitcherTests.cs
│       ├── InputListenerTests.cs
│       └── OverlayItemTests.cs
├── extension/
│   └── extension.yaml           # Plugin manifest
├── tools/
│   └── pack.ps1                 # Build and packaging script
└── .github/workflows/           # CI/CD pipelines
```

## Architecture Overview

### Core Components

#### OverlayPlugin.cs (Entry Point)
- Implements Playnite's `GenericPlugin` interface
- Coordinates all services: `InputListener`, `GameSwitcher`, `RunningAppsDetector`, `OverlayService`
- Manages plugin lifecycle (OnGameStarted, OnGameStopped)
- Handles settings application

#### GameSwitcher (Game Management Service)
- **Single source of truth** for active app tracking (simplified from dual tracking in v0.3)
- Manages `activeApp` state with `ActivatedTime` and `TotalPlaytime`
- Process detection with three strategies:
  1. Install directory matching
  2. Process name fuzzy matching
  3. Window title matching
- Game launching and termination (graceful → forceful)
- Session duration formatting (`GetSessionDuration()`)
- Recent games retrieval
- Auto-detection of foreground window (`DetectForegroundApp()`)

#### RunningAppsDetector (Process Detection Service)
- Detects all running applications with visible windows
- Three-tier detection strategy:
  1. **PlayniteGame**: Games tracked by Playnite
  2. **DetectedGame**: Running processes matched to library
  3. **GenericApp**: Other applications (browsers, editors, etc.)
- Filters system processes and launchers
- Fires `AppSwitched` event when user switches apps
- Window validation and switching (`SwitchToApp()`)

#### OverlayService (UI Management)
- Creates and displays WPF overlay window
- Window lifecycle management (show/hide)
- Visibility state tracking

#### InputListener (Input Handling)
- **Split lifecycle management** (v0.3 improvement):
  - Hotkey: Always active
  - Controller: Configurable (always-active or gameplay-only)
- XInput polling for Xbox controllers (100ms interval)
- Hotkey registration with retry logic (up to 10 attempts)
- Supports multiple controller combos:
  - Guide button (via keystroke API)
  - Start+Back, LB+RB (via button mask polling)

### Data Models

#### RunningApp
```csharp
public sealed class RunningApp
{
    public string Title { get; set; }
    public string? ImagePath { get; set; }
    public IntPtr WindowHandle { get; set; }
    public Guid? GameId { get; set; }
    public int ProcessId { get; set; }
    public AppType Type { get; set; }  // PlayniteGame | DetectedGame | GenericApp
    public Action? OnSwitch { get; set; }
    public DateTime ActivatedTime { get; set; }  // New in v0.3
    public ulong TotalPlaytime { get; set; }  // For Playnite games (seconds)
}
```

#### OverlayItem
- Factory methods for different item types:
  - `FromRunningApp()`: For NOW PLAYING section
  - `FromRecentGame()`: For RECENT GAMES section
  - `FromGame()`: Legacy/test compatibility
- Private `GetBestImagePath()` helper (duplicated across components for encapsulation)

#### OverlaySettings
- `EnableCustomHotkey`: bool (default: true)
- `CustomHotkey`: string (default: "Ctrl+Alt+O")
- `UseControllerToOpen`: bool (default: true)
- `ControllerCombo`: string (default: "Guide")
- `ControllerAlwaysActive`: bool (default: false) - New in v0.3
- `ShowGenericApps`: bool (default: true) - New in v0.3
- `MaxRunningApps`: int (default: 10, range: 1-50) - New in v0.3

## Build, Test, and Development Commands

### Prerequisites
- .NET 6.0 SDK or higher (for building)
- .NET Framework 4.7.2 runtime (target framework)
- Visual Studio 2022 or VS Code with C# extension
- Windows OS (for WPF and Win32 APIs)

### Build Commands

**Restore dependencies:**
```bash
dotnet restore src/OverlayPlugin/OverlayPlugin.csproj
```

**Build debug:**
```bash
dotnet build src/OverlayPlugin/OverlayPlugin.csproj --configuration Debug
```

**Build release:**
```bash
dotnet build src/OverlayPlugin/OverlayPlugin.csproj --configuration Release
```

**Run tests:**
```bash
dotnet test tests/OverlayPlugin.Tests/OverlayPlugin.Tests.csproj
```

**Package plugin:**
```powershell
# Windows PowerShell
.\tools\pack.ps1
```
This creates `playnite-overlay.pext` in `extension/` directory.

### Development Workflow

1. **Make changes** in `src/OverlayPlugin/`
2. **Build** with `dotnet build`
3. **Run tests** with `dotnet test`
4. **Test manually** in Playnite:
   - Copy `bin/Debug/net472/` contents to Playnite's extensions folder
   - Or use extension dev mode: `Playnite.DesktopApp.exe --dev`
5. **Package** with `pack.ps1` before release

## Coding Style & Naming Conventions

### C# Style
- **Indentation**: 4 spaces (no tabs)
- **File encoding**: UTF-8
- **Line endings**: Unix (LF) - configured in `.editorconfig`
- **Namespace**: File-scoped namespace declarations (C# 10+)
- **Nullable**: Enabled for reference types

### Naming Conventions
- **Public types/members**: PascalCase (`GameSwitcher`, `ActiveApp`)
- **Private fields**: camelCase (`activeApp`, `pollTimer`)
- **Constants**: UPPER_CASE (`GracefulExitTimeoutMs`, `PollIntervalMs`)
- **Interfaces**: IPascalCase (`IPlayniteAPI`)
- **Async methods**: Suffix with `Async` (if truly async)

### Code Patterns
- **Favor async APIs**: Use `async/await` for I/O operations
- **Avoid blocking UI thread**: Use `Dispatcher.Invoke()` or `Dispatcher.BeginInvoke()`
- **MVVM in WPF**: Use `ObservableObject` from CommunityToolkit.Mvvm
- **Dispose pattern**: Implement `IDisposable` for unmanaged resources
- **Null safety**: Use nullable reference types and null-conditional operators

### Error Handling
- **Log everything**: Use `LogManager.GetLogger()` and appropriate levels (Debug, Info, Warn, Error)
- **Try-catch patterns**: 
  - Catch specific exceptions first (Win32Exception, ArgumentException)
  - Log with context (`logger.Error(ex, "Failed to ...")`)
  - Show user-friendly notifications via `api.Notifications`
- **Graceful degradation**: Plugin should never crash Playnite

## Testing Guidelines

### Framework
- **xUnit**: Primary test framework
- **Moq**: Not currently used, but available for mocking
- **Coverage**: Target ≥70% for core services (GameSwitcher, RunningAppsDetector)

### Test Organization
- **Test names**: `MethodName_Scenario_ExpectedBehavior`
  - Example: `ResolveComboMask_StartBack_ReturnsCorrectMask`
- **Test location**: `tests/OverlayPlugin.Tests/`
- **Test files**: Match source file names (e.g., `GameSwitcher.cs` → `GameSwitcherTests.cs`)

### What to Test
- **Core logic**: GameSwitcher, RunningAppsDetector (high priority)
- **Input parsing**: Combo mask resolution, hotkey parsing
- **Data models**: OverlayItem factory methods
- **UI**: Lightweight smoke tests where feasible (WPF testing is complex)

### Running Tests
```bash
# Run all tests
dotnet test

# Run with coverage (if configured)
dotnet test /p:CollectCoverage=true
```

CI publishes coverage artifacts via GitHub Actions.

## Commit & Pull Request Guidelines

### Commit Messages
Follow [Conventional Commits](https://www.conventionalcommits.org/):

**Format:** `<type>(<scope>): <description>`

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `chore`: Maintenance (dependencies, cleanup)
- `refactor`: Code restructuring without behavior change
- `test`: Adding or updating tests
- `perf`: Performance improvements

**Scopes (examples):**
- `input`: InputListener, controller/hotkey handling
- `detection`: RunningAppsDetector, app detection logic
- `ui`: Overlay window, XAML, visual changes
- `tracking`: GameSwitcher, active app tracking
- `settings`: Configuration and settings

**Examples:**
```
feat(detection): add auto-detection of foreground app
fix(input): hotkey now works at all times
docs: update README with v0.3 features
chore: remove unused System.IO import
```

### Pull Request Process

1. **Branch naming**: `feat/description`, `fix/description`, `docs/description`
2. **Target branch**: 
   - Features → `develop`
   - Hotfixes → `main` (then merge back to develop)
3. **PR description should include**:
   - Summary of changes
   - Linked issues (if applicable)
   - Test results (unit tests passed, manual testing done)
   - Breaking changes (if any)
4. **Keep PRs focused**: One feature/fix per PR
5. **Update docs**: If behavior changes, update README.md or CONTRIBUTING.md

### Git Workflow
```
main (stable, tagged releases)
  ↑
develop (integration branch)
  ↑
feat/split-layout-ui (feature branches)
```

### Branch Protection Rules

**IMPORTANT: Never commit directly to `main` or `develop` branches.**

All changes must go through a Pull Request, even single-line fixes:

1. **Create a feature branch first**:
   ```bash
   git checkout develop
   git pull origin develop
   git checkout -b feat/my-feature   # or fix/my-fix, docs/my-docs
   ```

2. **Make changes and commit** to the feature branch

3. **Push and create a PR**:
   ```bash
   git push -u origin feat/my-feature
   gh pr create --base develop --title "feat(scope): description"
   ```

4. **Wait for CI to pass** before merging

5. **Merge via GitHub** (squash merge preferred for feature branches)

**Why?**
- PRs enable code review and CI validation before integration
- Direct pushes bypass CI checks and can break the build
- History stays clean with squash merges
- Easier to revert changes if needed

### Quick Reference: PR Workflow

| Step | Command |
|------|---------|
| Create branch | `git checkout -b fix/description` |
| Commit | `git commit -m "fix(scope): message"` |
| Push | `git push -u origin fix/description` |
| Create PR | `gh pr create --base develop` |
| Merge | `gh pr merge --squash --delete-branch` |

## Architecture Decisions & History

### v0.3 Major Refactoring: Single Tracking

**Before (v0.2):**
- Dual tracking: `CurrentGame` (Playnite tracking) + `ActiveApp` (user focus)
- Complex state management with edge cases
- ~100 lines of conditional logic in `ToggleOverlay()`

**After (v0.3):**
- Single tracking: `ActiveApp` only (single source of truth)
- Added `ActivatedTime` and `TotalPlaytime` to `RunningApp`
- Simplified `ToggleOverlay()` by 33%
- Auto-detection fills the gap (detects foreground app automatically)

**Rationale:** Simpler mental model, fewer bugs, easier to maintain.

### v0.3 Feature: Split Input Lifecycle

**Problem:** Hotkey stopped working after game exit because controller and hotkey were tied together.

**Solution:** 
- Hotkey: Always active (registered on plugin load)
- Controller: Configurable (always-active OR gameplay-only)
- New setting: `ControllerAlwaysActive`

**Result:** Hotkey works at all times, controller behavior is user-controlled.

## Security & Safety Considerations

### Process Interaction
- **Never inject into game processes**: Overlay runs as separate WPF window (topmost, transparent backdrop)
- **Process termination**: 
  1. Try graceful (`CloseMainWindow()`, 3s timeout)
  2. Fall back to forceful (`Kill()`, 1s timeout)
  3. Skip launcher processes (Steam, Epic, etc.)
- **Access denied handling**: Suggest running Playnite as administrator

### Controller Input
- **Debounce Guide button**: Use XInput keystroke API to avoid double-triggers
- **Allow opt-out**: All input features are configurable
- **No injection required**: XInput polling is external to game processes

### API Usage
- **Match Playnite's runtime**: .NET Framework 4.7.2 (same as Playnite)
- **SDK version**: PlayniteSDK 6.12.0 (pinned in .csproj)
- **Win32 APIs**: Only for window management (GetForegroundWindow, SetForegroundWindow, etc.)

## Dependencies

### NuGet Packages
- **PlayniteSDK**: 6.12.0 (Playnite plugin API)
- **CommunityToolkit.Mvvm**: 8.4.0 (MVVM helpers)
- **xUnit**: 2.9.3 (testing framework)
- **Microsoft.NET.Test.Sdk**: 17.14.1 (test runner)
- **Coverlet.collector**: 6.0.4 (code coverage)

### Target Frameworks
- **Plugin**: net472 (.NET Framework 4.7.2)
- **Tests**: net6.0 (for modern testing features)

## Troubleshooting Development Issues

### Build Errors
- **"WindowsDesktop SDK not found"**: Requires Windows SDK, cannot build on macOS/Linux
- **"PlayniteSDK not found"**: Run `dotnet restore`
- **XAML errors**: Ensure VS has WPF workload installed

### Runtime Issues
- **Hotkey not registering**: Another app may be using the same hotkey (retry logic handles this)
- **Games not detected**: Check install directory, process name, and window title matching logic
- **Access denied on Kill()**: User needs to run Playnite as administrator

### Testing Issues
- **UI tests failing**: WPF requires STA thread, some tests may be environment-dependent
- **Process tests on CI**: Mock Process objects or skip on non-Windows

## Performance Considerations

- **Controller polling**: 100ms interval (10 polls/sec) - acceptable for input responsiveness
- **Process enumeration**: Only on overlay open (not continuous) - minimal impact
- **Image loading**: Async with try-catch, cached by WPF
- **Window animations**: 200ms fade-in, 60ms fade-out - smooth without lag

## Future Enhancements (Ideas)

- Customizable overlay themes
- Gamepad button remapping UI
- Playtime statistics in overlay
- Window position persistence
- Multi-monitor support improvements
- Configurable recent games count
- Game grouping/favorites in overlay
- Voice command integration (Cortana/Alexa)

---

For user-facing documentation, see [README.md](README.md).
