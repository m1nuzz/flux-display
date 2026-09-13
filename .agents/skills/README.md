# Vendored agent skills

Project-local skills for any `SKILL.md`-compatible agent (opencode, Claude Code,
Copilot CLI, Codex — auto-loaded from `.agents/skills/*/SKILL.md`).
Folder names must match the frontmatter `name` in each `SKILL.md`.

## Sources (all MIT)

| Skill dir | Upstream repo | Commit |
|---|---|---|
| `winui-dev-workflow`, `winui-design`, `winui-code-review`, `winui-ui-testing` | `microsoft/win-dev-skills` (preview v0.x) | `f94bab1` |
| `modern-csharp-coding-standards`, `csharp-concurrency-patterns`, `type-design-performance`, `dependency-injection-patterns`, `dotnet-slopwatch`, `crap-analysis`, `csharp-nullable-reference-types`, `dotnet-project-structure` | `Aaronontheweb/dotnet-skills` | `f4d0d39` |
| `directory-build-organization`, `copy-to-output-directory` | `dotnet/skills` (`dotnet-msbuild` plugin) | `4ecd7d9` |
| `winui3-full-skill` (`skill.md` renamed to `SKILL.md`) | `SudoCode76/winui3-skills` | `7adc9cc` |

## Updating

Re-copy the skill directory from upstream (whole directory — `SKILL.md` files
reference bundled `references/`, `catalog/`, `snippets/` and scripts by
relative path), keep the folder name equal to frontmatter `name`, and bump
the commit hash in the table above.

## Notes

- Cross-skill references (e.g. `winui-code-review` mentioning `BuildAndRun.ps1`)
  resolve to the sibling `winui-dev-workflow/` directory in this folder,
  same as upstream.

- `microsoft/win-dev-skills` is preview v0.x: prefer a release tag when
  refreshing. Its `winui-packaging` (MSIX) and `winui-setup` (one-time
  machine prep) skills are intentionally not vendored — this repo ships
  unpackaged + Inno Setup 6.
- When a skill conflicts with this repo (e.g. targets WindowsAppSDK 1.8+ /
  packaged MSIX, repo pins 1.6 / unpackaged), repo patterns win.
