# Changelog

All notable changes to the Playnite Overlay plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

---

## [0.5.0] - 2026-01-23

### Added
- **Audio Device Switcher**: Switch default Windows audio output device directly from the overlay
  - Shows dropdown with all active audio output devices
  - Displays current default device with indicator
  - Auto-hides when no audio devices detected or NAudio fails to initialize
  - Uses NAudio 2.2.1 for device enumeration
  - Switches device for all roles (Multimedia, Console, Communications)
- **Show Notifications Toggle**: New setting to enable/disable all overlay notifications (app switching, exit operations, errors).
- **PC Games Only Mode**: Option to disable controller input for non-PC games
  - Useful when emulators have their own overlays (RetroArch, etc.)
  - Works with all controller settings (Controller Always Active enabled or disabled)
  - Keyboard hotkey continues to work for all games regardless of this setting
  - Games without platform metadata are treated as PC games (backward compatible)
  - New setting: `PcGamesOnly` (default: disabled for backward compatibility)
  - **Tip**: Set platform metadata for emulated games to prevent controller conflicts
- **SuccessStory Integration**: Display achievement progress in the NOW PLAYING section
  - Shows achievement progress (X/Y - Z%) when SuccessStory plugin is installed
  - Displays recently unlocked achievements with gold trophy icon
  - Shows locked achievements with lock icon
  - New settings: `ShowAchievements`, `MaxRecentAchievements`, `MaxLockedAchievements`
  - Gracefully hidden when SuccessStory is not installed or game has no achievement data

### Changed
- **Show Generic Apps Default**: Changed default from enabled to disabled. New installations will only show Playnite-tracked games by default.

### Fixed
- **SuccessStory Achievement Detection**: Fixed locked achievements incorrectly showing as unlocked
  - SuccessStory uses `0001-01-01` date to represent locked achievements
  - Plugin now correctly identifies locked vs unlocked achievements

---

## [0.4.3] - 2025-12-22

### Added
- **Hide Running Apps from Recent Games**: Running games no longer appear in both "Running Apps" and "Recent Games" sections
  - Prevents duplicate entries in the overlay UI
  - Recent games list now excludes any games currently shown in Running Apps

### Fixed
- **D-pad and Button Double-Navigation**: Fixed controller inputs triggering multiple times per press
  - Affected users with controllers that register as multiple XInput devices
  - Added per-poll-cycle flag to prevent duplicate navigation events
- **Window Switching Reliability**: Improved app switching behavior
  - Better handling of window focus transitions
  - More reliable foreground window detection
- **System Tray Restore**: Fixed black screen when restoring Playnite from system tray
  - Properly handles minimized-to-tray state
- **Thread-Safe Event Invocation**: Fixed potential race condition in event handlers

### Changed
- **XInput Polling Consolidation**: Refactored controller input handling into single polling loop
  - Cleaner code structure
  - More efficient input processing
- **WPF Simplification**: Refactored overlay to use more native WPF features

---

## [0.4.2] - 2025-12-19

### Fixed
- **Input Blocking in Fullscreen Games**: Keyboard input now properly blocked when overlay is active
  - Use low-level keyboard hook (`WH_KEYBOARD_LL`) to intercept input before it reaches games
  - Added mouse click blocking support
- **Xbox Guide Button Detection**: Fixed Guide button not being detected
  - Use `XInputGetStateEx` (ordinal 100) which includes the Guide button
  - Falls back to standard API on older systems

### Changed
- **Removed Toggle Overlay Menu Item**: The "Overlay → Toggle Overlay" menu item has been removed
  - Overlay can still be toggled via keyboard hotkey (default: Ctrl+Shift+O)
  - Overlay can still be toggled via controller (Guide button or configured combo)

### Removed
- **Dead Code Cleanup**: Removed ~200 lines of unused/duplicate code
  - Removed unused XInput keystroke API
  - Removed legacy OverlayUI project
  - Consolidated duplicate code into `ProcessMatchingUtils`
  - Removed unused `TertiaryText` property from OverlayItem

---

## [0.4.0] - 2025-12-18

### Fixed
- **Fullscreen Games Minimizing**: Overlay no longer steals focus from games
  - Added `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW` extended window styles
  - Use `SetWindowPos` with `SWP_NOACTIVATE` for topmost positioning
  - Overlay now appears on same monitor as the foreground game
- **Button Focus Visibility**: Focus border now appears reliably on all buttons
  - Changed from `IsFocused` to `IsKeyboardFocused` triggers
  - Use `Keyboard.ClearFocus()` when exiting to section level
- **Controller Navigation**: Complete navigation system rewrite
  - Full support for `RunningAppsList` navigation (was previously ignored)
  - Fixed initial focus priority: RunningApps → RecentList → SwitchButton
  - Navigation now flows logically through entire overlay UI
- **Dual Selection Highlight**: Only one item highlighted at a time across lists
- **Nullable Reference Warnings**: Fixed compiler warnings in GameSwitcher and RunningAppsDetector

### Added
- **Force Borderless Mode**: Optional feature for games that still minimize
  - Automatically converts windowed games to borderless fullscreen
  - Configurable delay before applying (default: 3 seconds)
  - Restores original window state when game exits
  - New settings: `ForceBorderlessMode`, `BorderlessDelayMs`
- **Two-Level Navigation System**: Section-first navigation for controller/keyboard
  - Level 1: Navigate between sections (CurrentGame, RunningApps, RecentGames) with Up/Down
  - Level 2: Press Enter/A to drill into section, navigate items with Up/Down
  - Press Escape/B to exit back to section level
- **Keyboard Arrow Navigation**: Arrow keys now work for overlay navigation
  - Up/Down arrows navigate through sections and items
  - Left/Right arrows navigate between buttons
  - Enter key activates the currently selected item
- **Button Focus Visual Feedback**: White border on all focused buttons
- **Diagnostic Logging**: Debug logging for controller input events

### Changed
- **Controller Always Active**: Now defaults to `true` (works without game running)
- **Navigation Flow**: Improved controller/keyboard navigation UX
  - Section-level navigation with visual border feedback
  - Item-level navigation within sections
  - Automatic scroll-into-view for selected items

---

## [0.3.0] - 2025-12-15

### Added
- **Active App Tracking System**: Plugin now tracks which game/app you're currently focused on
  - Session duration display ("Playing for 2h 15m")
  - Total playtime tracking for Playnite games
  - Single source of truth for active app state
- **Auto-Detection Feature**: Automatically detects foreground app when opening overlay
  - Smart detection when no active app is set
  - Auto-switches to new app after exiting current one
  - Uses Win32 APIs (`GetForegroundWindow`, `GetWindowThreadProcessId`)
- **Running Apps Detection**: Three-tier detection system for all running applications
  - Playnite-tracked games (launched through Playnite)
  - Detected games (manually launched, matched to library)
  - Generic apps (browsers, editors, etc.) - optional
  - Configurable visibility and limits
- **Three-Section Overlay UI**: New layout with distinct sections
  - NOW PLAYING: Shows active game with session info
  - RUNNING APPS: Lists all detected running apps with "Switch" buttons
  - RECENT GAMES: Shows 5 most recently played games
- **New Settings**:
  - `ShowGenericApps`: Toggle visibility of non-game applications (default: true)
  - `MaxRunningApps`: Limit number of running apps displayed (1-50, default: 10)
  - `ControllerAlwaysActive`: Keep controller input active even when not gaming (default: false)
- **Split Input Lifecycle**: Hotkey and controller now have independent lifecycles
  - Hotkey always active (works even when not gaming)
  - Controller configurable (always-active or gameplay-only)

### Changed
- **Simplified Architecture**: Refactored from dual tracking to single tracking
  - Removed `CurrentGame`/`CurrentGameStartTime` (Playnite tracking)
  - Unified to `ActiveApp` only as single source of truth
  - Added `ActivatedTime` and `TotalPlaytime` to `RunningApp` model
  - Reduced `ToggleOverlay()` complexity by ~33% (~100 lines simpler)
- **Improved Process Detection**: Three-strategy matching system
  - Install directory matching (primary)
  - Process name fuzzy matching (secondary)
  - Window title matching (tertiary)
- **Enhanced Error Handling**: Better crash prevention and user feedback
  - Race condition protection when processes exit during operations
  - Graceful degradation when access denied
  - Clear notification messages for all operations

### Fixed
- **Hotkey Lifecycle Bug**: Hotkey now works at all times, not just during gameplay
  - Previously stopped working after game exit due to tied lifecycle
  - Split from controller input lifecycle for independent operation
- **.NET Framework 4.7.2 Compatibility**: Resolved all compatibility issues
  - Removed WPF features not available in .NET Framework 4.7.2
  - Tested and verified on target runtime
- **Race Condition Crashes**: Fixed crash when process exits before switch operation
  - Added window existence validation (`IsWindow()`)
  - Proper error handling in `SwitchToApp()`
- **Process Termination Edge Cases**: Improved game exit logic
  - Better handling of launcher processes (Steam, Epic, etc.)
  - Graceful → forceful termination with proper timeouts (3s → 1s)
  - Admin permission handling with clear user guidance

### Developer Changes
- **Code Cleanup**: Removed unused imports and dead code
  - Removed `using System.IO;` from GameSwitcher (unused)
  - Verified all methods are called and imports are needed
- **Test Coverage**: Added comprehensive unit tests
  - `ComboMaskTests`: Controller combo mask resolution
  - `GameSwitcherTests`: Core game management logic
  - `InputListenerTests`: Input handling
  - `OverlayItemTests`: Data model factory methods
- **CI/CD Pipelines**: Added GitHub Actions workflows
  - `ci.yml`: Main CI pipeline for feature branches
  - `ci-develop.yml`: Develop branch pipeline
  - `ci-debug.yml`: Debug diagnostics pipeline
  - `release-main.yml`: Production release pipeline
- **Documentation**: Complete documentation overhaul
  - New `README.md`: User-friendly guide
  - Updated `AGENTS.md`: Comprehensive developer guide
  - New `CHANGELOG.md`: Version history tracking

### Technical Details
- **Architecture Refactoring**: Single tracking implementation
  - Before: Dual tracking with `CurrentGame` + `ActiveApp` (complex state)
  - After: Single `ActiveApp` tracking (simple, clear mental model)
  - Result: Fewer edge cases, easier maintenance, better UX
- **Service Responsibilities**:
  - `GameSwitcher`: Active app management, process detection, game launching/termination
  - `RunningAppsDetector`: Multi-app detection, window validation, switching
  - `OverlayService`: UI lifecycle management
  - `InputListener`: Input handling with split hotkey/controller lifecycle
- **Data Models**:
  - `RunningApp`: Added `ActivatedTime` (DateTime) and `TotalPlaytime` (ulong seconds)
  - `OverlayItem`: Factory methods for different UI sections
  - `AppType`: Enum for three-tier classification (PlayniteGame | DetectedGame | GenericApp)

### Known Issues
- None at release

---

## [0.2.0] - Previous Release

(Version 0.2.0 and earlier are not documented in this changelog. See git history for details.)

---

## Legend

- **Added**: New features
- **Changed**: Changes in existing functionality
- **Deprecated**: Soon-to-be removed features
- **Removed**: Removed features
- **Fixed**: Bug fixes
- **Security**: Vulnerability fixes
