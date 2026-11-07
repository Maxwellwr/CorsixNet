# CorsixNet

A reimplementation of Theme Hospital in C# / .NET, modelled on
[CorsixTH](https://github.com/CorsixTH/CorsixTH), with deterministic lockstep
multiplayer and mod support as design goals. It needs the data files from an
original copy of the game (CD, GOG or EA).

Early development: see [ARCHITECTURE.md](ARCHITECTURE.md) for the design and roadmap.

## Building

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet test
```

Tests that read original game data are skipped unless the game is found. Detection
checks common install folders. Set `CORSIXNET_TH_PATH` to point at a copy explicitly.

## Running

```bash
dotnet run --project src/CorsixNet.Client
```

Optionally pass the game folder as the first argument.

## License

[MIT](LICENSE). Theme Hospital is a trademark of Electronic Arts; CorsixNet is not
affiliated with or endorsed by EA or Bullfrog. Original game data is not included.
