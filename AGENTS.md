# Repository Guidelines

## Project Structure & Module Organization
- Source:  (e.g., , ).
- Tests:  (unit tests for non-UI logic).
- Assets:  (icons, screenshots, themes).
- Packaging:  (scripts) and  (manifest, packaged output).
- Plugin entry implements Playnite’s  and composes services: , , .

## Build, Test, and Development Commands
- : restore and build all projects.
- : run unit tests with coverage (if configured).
- : build and create  package under .
- : launch Playnite in extension dev mode for rapid iteration.

## Coding Style & Naming Conventions
- C# with 4-space indentation; UTF-8 files; Unix line endings.
- Public types/members: PascalCase; fields/private members: camelCase; constants: UPPER_CASE.
- Favor async APIs; avoid blocking UI thread. Use MVVM in WPF ().
- Lint/format:  + optional ; keep warnings clean.

## Testing Guidelines
- Framework:  (logic/services) and lightweight UI smoke tests where feasible.
- Coverage: target ≥70% for core services (, ).
- Test names:  and place in .
- Run locally via ; CI should publish coverage artifacts if enabled.

## Commit & Pull Request Guidelines
- Commits: Conventional Commits (, , , ). Scope examples: , , .
- PRs: include summary, linked issues, before/after screenshots or short GIF of the overlay, and test results.
- Keep PRs focused; update docs (this file, README) when changing behavior or commands.

## Security & Configuration Tips
- Match Playnite’s runtime (e.g., .NET 6+) and pin  version.
- Do not inject into game processes; overlay runs as a separate WPF window (topmost, transparent).
- Handle controller input safely (debounce Guide button; allow opt-out in settings).
