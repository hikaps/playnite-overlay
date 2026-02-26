# Playnite Overlay

A powerful Playnite plugin that adds an in-game overlay for quick game switching and management. Open the overlay with a hotkey or Xbox controller button to seamlessly switch between running games, manage your current session, and browse your recent games.

## Features

### 🎮 Quick Access Overlay
- Open with **Ctrl+Alt+O** (customizable) or **Xbox Guide button**
- Beautiful, non-intrusive overlay UI
- Smooth fade-in/fade-out animations
- Controller and keyboard navigation support

### 📊 Three-Section Layout

#### NOW PLAYING
- Shows your currently active game or app
- Displays session duration ("Playing for 2h 15m")
- Shows total playtime for Playnite games
- Quick "Switch to Playnite" button
- "Exit Game" button to close the current app

#### RUNNING APPS
- Automatically detects all running games and applications
- Three-tier detection system:
  - **Playnite Games**: Games launched through Playnite
  - **Detected Games**: Manually launched games in your library
  - **Generic Apps**: Any other running applications (optional)
- One-click switching between running apps
- Purple "Switch" buttons for quick access

#### RECENT GAMES
- Shows your 5 most recently played games
- Displays last played time ("2h ago", "yesterday", etc.)
- Click to launch games instantly
- Automatically excludes currently running game

### 🔍 Smart Auto-Detection
- Automatically detects which app you're focused on
- Auto-updates when you exit a game and switch to another
- No manual tracking needed - just use your apps normally

### ⚙️ Flexible Configuration
- **Hotkey Customization**: Set any keyboard shortcut
- **Controller Options**:
  - Guide button (default)
  - Start+Back
  - LB+RB
- **Running Apps Settings**:
  - Show/hide generic applications (browsers, editors, etc.)
  - Limit maximum number of apps displayed (1-50)
- **Controller Always Active**: Keep controller input active even when not gaming

## Installation

### From Playnite Extension Manager (Recommended)
1. Open Playnite
2. Go to **Add-ons** → **Browse**
3. Search for "Playnite Overlay"
4. Click **Install**
5. Restart Playnite

### Manual Installation
1. Download the latest `.pext` file from [Releases](https://github.com/hikaps/playnite-overlay/releases)
2. Drag and drop the file into Playnite
3. Restart Playnite

## Usage

### Opening the Overlay

**Keyboard:**
- Press **Ctrl+Alt+O** (or your custom hotkey)

**Xbox Controller:**
- Press the **Guide button** (or your configured combo)

### Navigating the Overlay

**Keyboard:**
- **Arrow keys**: Navigate between items
- **Enter**: Select/activate item
- **Escape**: Close overlay
- **Click**: Mouse support for all actions

**Xbox Controller:**
- **D-Pad/Left Stick**: Navigate
- **A Button**: Select/activate
- **B Button**: Close overlay

### Switching Games
1. Open the overlay
2. Navigate to the game you want (in RUNNING APPS or RECENT GAMES)
3. Press Enter/A or click the item
4. Overlay closes and game activates

### Exiting a Game
1. Open the overlay
2. Click **"Exit Game"** button in NOW PLAYING section
3. Current game closes gracefully
4. Automatically switches to next focused app

## Configuration

Access settings through: **Playnite Menu** → **Add-ons** → **Extension settings** → **Playnite Overlay**

### Hotkey Settings
- **Enable Custom Hotkey**: Toggle keyboard shortcut
- **Custom Hotkey**: Set your preferred key combination (default: Ctrl+Alt+O)

### Controller Settings
- **Use Controller to Open**: Enable/disable controller input
- **Controller Combo**: Choose trigger combination:
  - Guide (default)
  - Start+Back
  - LB+RB
- **Controller Always Active**: Keep controller input active even when not in a game

### Running Apps Detection
- **Show Generic Apps**: Include non-game applications in RUNNING APPS section
- **Max Running Apps**: Limit number of apps displayed (1-50, default: 10)

### Shortcuts
Configure custom shortcut buttons in the overlay that can run scripts or simulate keyboard presses:
- **Add shortcuts** in settings (up to 10)
- **Shortcuts appear** as buttons in a SHORTCUTS section in the overlay
- **Click to execute** the configured action

#### Action Types

| Type | Description | Use Case |
|------|-------------|----------|
| **CommandLine** | Run a script or executable with arguments | Launch PowerShell scripts, batch files, external tools |
| **SendInput** | Simulate a keyboard hotkey press | Trigger tools that only respond to hotkeys (Steam, OBS, etc.) |

#### CommandLine Examples

**Run a PowerShell script:**
- Label: `Backup Saves`
- Action: `CommandLine`
- Command: `powershell.exe`
- Arguments: `-ExecutionPolicy Bypass -File "C:\Scripts\backup-saves.ps1"`

**Run a batch file:**
- Label: `Clean Temp`
- Action: `CommandLine`
- Command: `C:\Scripts\clean-temp.bat`
- Arguments: (leave empty)

**Launch external tool:**
- Label: `Notepad`
- Action: `CommandLine`
- Command: `notepad.exe`
- Arguments: (leave empty)

#### SendInput Examples (Screenshot/Recording)

For tools that use hotkeys instead of command-line arguments:

**Steam Screenshot:**
- Label: `Screenshot`
- Action: `SendInput`
- Hotkey: `F12`

**OBS Toggle Recording:**
- Label: `Record`
- Action: `SendInput`
- Hotkey: `F9` (or your OBS hotkey setting)

**GeForce Experience Screenshot:**
- Label: `Screenshot`
- Action: `SendInput`
- Hotkey: `Alt+F1`

**Xbox Game Bar Recording:**
- Label: `Record`
- Action: `SendInput`
- Hotkey: `Win+Alt+R`

> **Tip:** For SendInput, set the hotkey to match what's configured in the target application. Check Steam/OBS/GeForce Experience settings to find or customize their screenshot/recording hotkeys.

## Requirements

- **Playnite**: Version compatible with PlayniteSDK 6.12.0+
- **.NET Framework**: 4.7.2 or higher
- **Operating System**: Windows
- **Optional**: Xbox-compatible controller for controller features

## How It Works

The plugin uses a three-tier detection strategy to identify running applications:

1. **Playnite-Tracked Games**: Games launched through Playnite are automatically tracked
2. **Library Matching**: Running processes are matched against your Playnite library by:
   - Install directory
   - Process name fuzzy matching
   - Window title matching
3. **Generic Detection**: Any running app with a visible window (optional)

The overlay runs as a separate WPF window (topmost, click-through background) and never injects into game processes, ensuring maximum compatibility and safety.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history and release notes.

## Links

- **GitHub**: [hikaps/playnite-overlay](https://github.com/hikaps/playnite-overlay)
- **Issues**: [Report a bug](https://github.com/hikaps/playnite-overlay/issues)
- **Releases**: [Download latest version](https://github.com/hikaps/playnite-overlay/releases)

## Support

If you encounter issues:
1. Check the [Issues](https://github.com/hikaps/playnite-overlay/issues) page
2. Ensure you meet the requirements (especially .NET Framework 4.7.2)
3. Try running Playnite as administrator (for game termination features)
4. Create a new issue with:
   - Playnite version
   - Plugin version
   - Steps to reproduce
   - Expected vs actual behavior

---

## AI Disclosure

This project was developed with assistance from AI tools for code generation, documentation, and debugging.

---

**Built with ❤️ for the Playnite community**
