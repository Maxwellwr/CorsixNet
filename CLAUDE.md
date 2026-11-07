# CorsixNet

C# / .NET 10 reimplementation of Theme Hospital, modelled on CorsixTH
(reference checkout: `C:\repos\github\CorsixTH`). Read `ARCHITECTURE.md` first.

## Rules

- `CorsixNet.Sim` (and any project with `CorsixNetDeterministic=true`) must stay
  deterministic. The analyzer (`CNET0001`–`CNET0003`) enforces part of this. Do not
  suppress its diagnostics. Restructure the code instead.
- The simulation must not reference the client or MonoGame.
- When porting logic from CorsixTH, keep behaviour faithful and note the source file.
  CorsixTH is MIT-licensed. Never use disassembly of the original game binaries.
- Format readers get unit tests with synthetic data. Add real-data tests too, via
  `RealGameData.Require()`, which skips when the game is not installed.
- Package versions live in `Directory.Packages.props`. Shared build settings live in
  `Directory.Build.props`. Warnings are errors.

## Commands

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```
