## Code Organization

- Keep source files under 600 lines (excluding blanks/comments). If a file exceeds 600 lines, split it by responsibility into focused modules/files and re-export the public API where needed.

## Agent Skills

Project-local skills for this stack (C# 12 / .NET 8 / WinUI 3, unpackaged + Inno Setup 6) are vendored in `.agents/skills/` and load on demand when a task matches their description. See `.agents/skills/README.md` for upstream sources, pinned commits, and the update procedure.

- `microsoft/win-dev-skills` (official, preview v0.x): `winui-dev-workflow` (build/run/iterate), `winui-design` (XAML/Fluent), `winui-code-review` (pre-commit review), `winui-ui-testing` (UI Automation). `winui-packaging` (MSIX) and `winui-setup` (one-time machine prep) intentionally not vendored — this repo ships unpackaged + Inno Setup.
- `Aaronontheweb/dotnet-skills`: `modern-csharp-coding-standards`, `csharp-concurrency-patterns`, `type-design-performance`, `dependency-injection-patterns`, `csharp-nullable-reference-types`, `dotnet-project-structure`; quality gates `dotnet-slopwatch` (after large LLM-authored changes), `crap-analysis` (after test changes).
- `dotnet/skills` (official .NET team, `dotnet-msbuild` plugin): `directory-build-organization`, `copy-to-output-directory`.
- `SudoCode76/winui3-skills`: `winui3-full-skill` (~70 WinUI 3 controls with XAML + C# examples, MVVM + DI, `x:Bind`).

Routing: XAML/controls/theming → `winui-design` (+ `winui3-full-skill` for control examples); build errors/launch → `winui-dev-workflow`; C# style/concurrency → `dotnet-skills` C# skills; MSBuild/props/output layout → `dotnet-msbuild` skills; new UI behavior → `winui-ui-testing` + `winui-code-review`. When a skill conflicts with this repo (e.g. targets WindowsAppSDK 1.8+/packaged MSIX, repo pins 1.6/unpackaged), repo patterns win — note the conflict instead of migrating.

