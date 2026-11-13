# Contributing to Flux Display

Flux Display is a solo-maintained project. Issues and pull requests are welcome, but every contribution of code, documentation, translations, artwork, or other materials must be covered by the [Contributor License Agreement](CLA.md).

## Before your pull request can be merged

1. Read [CLA.md](CLA.md). It assigns the copyright in your contribution to the maintainer; you keep a non-exclusive license back to use your own contribution anywhere.
2. Sign it by commenting exactly this phrase on your pull request:

   ```text
   I have read the CLA Document and I hereby sign the CLA
   ```

3. Wait for the `CLAAssistant` check to pass.

Pull requests without a signed CLA cannot be merged. Signing is required once per GitHub account.

## Scope

The maintainer decides what lands in the project. Changes that affect the product direction — UI, display control paths, packaging, release automation, licensing — should be discussed in an issue before code is written.

## Quality gates

Run the gates that apply to your change before opening the pull request:

```text
dotnet restore FluxDisplay.sln
dotnet build FluxDisplay.sln -c Release -p:Platform=x64
dotnet test tests/FluxDisplay.Core.Tests/FluxDisplay.Core.Tests.csproj -c Release
```

For UI, display-control, or packaging changes, verify the behavior on Windows 11 and describe what you tested. Screenshots or a short recording help.

## Code rules

- English comments, documentation, and user-facing strings.
- C# 12 / .NET 8 / WinUI 3 on the unpackaged build with the Inno Setup installer. Do not introduce another UI toolkit, and do not migrate packaging to MSIX.
- Keep source files under 600 lines; split by responsibility instead.
- Keep changes focused. Unrelated refactors and formatting-only churn in an existing area make a pull request harder to review.

## Commit messages

Use concise English [Conventional Commits](https://www.conventionalcommits.org/) messages, for example `fix(display): ...` or `feat(settings): ...`.
