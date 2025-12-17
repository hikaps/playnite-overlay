# AI Agent Guidelines

## Build & Test Commands
- Build: `dotnet build src/OverlayPlugin/OverlayPlugin.csproj`
- Test all: `dotnet test tests/OverlayPlugin.Tests/OverlayPlugin.Tests.csproj`
- Test single: `dotnet test --filter "FullyQualifiedName~TestMethodName"`
- Package: `pwsh tools/pack.ps1` (Windows only)

## Code Style
- 4 spaces, UTF-8, LF line endings, file-scoped namespaces
- PascalCase (public), camelCase (private fields), UPPER_CASE (constants)
- Use `var` when type is apparent; nullable reference types enabled
- Target: .NET Framework 4.7.2 (no `Math.Clamp`, use `Math.Max/Min`)

## Error Handling
- Log with `LogManager.GetLogger()`, catch specific exceptions first
- Never crash Playnite; show user notifications via `api.Notifications`

## Git Workflow
- Branch from `develop`: `feat/`, `fix/`, `docs/`
- Conventional commits: `feat(scope): description`
- Always create PR, never push directly to `main` or `develop`

See [CONTRIBUTING.md](CONTRIBUTING.md) for architecture and detailed docs.
