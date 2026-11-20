# Contributing

This is a proprietary HTREASURE LTD product. Contributions are made by HTREASURE LTD staff and contractors.

## Setting up

1. Visual Studio 2026 with *.NET desktop development*, or the .NET SDK 10 or later. The SDK builds the .NET Framework
   projects through `Microsoft.NETFramework.ReferenceAssemblies`; version 10 is needed because the build also compiles
   and runs the HTREASURE standards CLI (`net10.0`).
2. Clone with submodules:
   ```bash
   git clone -c core.longpaths=true --recurse-submodules https://github.com/HTREASURE-LTD/HTREASURE-FileHashChecker.git
   ```
3. Build and test: see [docs/BUILD.md](docs/BUILD.md).

## Rules

- Follow the HTREASURE standards ([docs/HTREASURE-STANDARDS.md](docs/HTREASURE-STANDARDS.md)). Do not edit files under
  `external/`; change the company repositories and update the submodule pointer instead.
- Keep business logic out of Windows Forms event handlers: put it in `HTFileHashChecker.Core` (or `.Windows`) and test it.
- C# 7.3, `net461` and `net48`. Both targets must build with zero warnings (warnings are errors). Check any API you use
  exists in .NET Framework 4.6.1, or guard it with `#if NET47_OR_GREATER`.
- Add tests for every change; all tests must pass on `net48` and `net462`.
- Never commit build output, certificates, private keys, passwords, tokens or user data. `.gitignore` covers the usual
  cases; check `git status` before committing.
- Use your own Git identity. Do not add AI assistants as authors or co-authors.
- Do not force-push or rewrite published history.
- Update `CHANGELOG.md` and the relevant documentation in the same change.

## Commit messages

A short summary line in the imperative ("Add BSD tag export"), a blank line, then what changed and why.
