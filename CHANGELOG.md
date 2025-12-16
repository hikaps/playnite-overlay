# Changelog

All notable changes to the Playnite Overlay plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- **Controller Toggle Not Working**: Changed `ControllerAlwaysActive` default to `true`
  - Controller now works out-of-box without requiring a game to be running
  - Previously defaulted to `false`, requiring game to be active for controller input
- **Controller Navigation Incomplete**: Complete rewrite of navigation system
  - Added full support for `RunningAppsList` navigation (was previously ignored)
  - Implemented Up/Down navigation through all sections (CurrentGame → RunningApps → RecentGames → Buttons)
  - Added Left/Right navigation between SwitchBtn and ExitBtn
  - Fixed initial focus priority: RunningApps (if visible) → RecentList → SwitchButton
  - Navigation now flows logically through entire overlay UI

### Added
- **Keyboard Arrow Navigation**: Arrow keys now work for overlay navigation
  - Up/Down arrows navigate through all sections (same as D-pad)
  - Left/Right arrows navigate between buttons
  - Enter/Space activate focused items (default WPF behavior)
  - Works regardless of controller settings
- **Diagnostic Logging**: Added debug logging for controller input events
  - Logs when Guide button is pressed
  - Logs when controller combos (Start+Back, LB+RB) are detected
  - Helps troubleshoot controller detection issues

### Changed
- **Scroll-Free UI**: Removed all scrolling for better controller experience
  - Removed outer ScrollViewer from overlay (no more nested scrolling)
  - Disabled scrolling on RunningAppsList and RecentList
  - Changed `MaxRunningApps` default from 10 to 4
  - All content now fits on screen without scrolling
- **Navigation Flow**: Improved controller navigation UX
  - Down: SwitchBtn → ExitBtn → RunningApps → RecentGames → (wrap to SwitchBtn)
  - Up: Reverse of Down navigation
  - Left/Right: Navigate between SwitchBtn ↔ ExitBtn

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
