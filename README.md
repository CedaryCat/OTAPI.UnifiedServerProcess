# OTAPI Unified Server Process (USP)

OTAPI Unified Server Process (USP) is a re‑engineered Terraria server core that converts global static state into per‑server context. This lets you run multiple fully isolated server instances in a single process, while exposing a cleaner, safer API for networking, world data, and gameplay systems.

USP compiles a patched Terraria core into two assemblies:
- OTAPI.dll — the rewritten core with context‑bound systems
- OTAPI.Runtime.dll — runtime hooks (On.*) generated for extension points


## Install via NuGet (recommended)
Most consumers can add USP directly from NuGet, which provides patched `OTAPI.dll` and `OTAPI.Runtime.dll` binaries alongside XML docs and symbols.

```
dotnet add package OTAPI.USP
```

You only need a local source build when experimenting with patches or contributing upstream changes.


## Why USP
- Multiple servers, one process: Each server instance owns its own RootContext. No shared singletons; no cross‑instance bleed‑through.
- Safer APIs: Critical systems (Main, Netplay, NetMessage, Collision, etc.) are accessed through a context rather than static globals.
- Protocol ergonomics: TrProtocol provides strong‑typed packet models and a source generator for fast serializers.
- World data revamp: TileProvider replaces legacy ITile/Tile with TileData/RefTileData and TileCollection for predictable references and better memory access patterns.
- Server‑first trimming: Non‑server code paths are aggressively removed to simplify execution on dedicated servers.


## Quick Start
1) Restore and build
- `dotnet restore src/OTAPI.UnifiedServerProcess.sln`
- `dotnet build src/OTAPI.UnifiedServerProcess.sln -c Release`

2) Produce OTAPI.dll / OTAPI.Runtime.dll
- `dotnet run -c Debug -p src/OTAPI.UnifiedServerProcess`
- Output: `src/OTAPI.UnifiedServerProcess/bin/<Config>/net9.0/output/`

3) Try the Global Network demo (multi‑server in one process)
- `dotnet run -c Debug -p src/OTAPI.UnifiedServerProcess.GlobalNetwork`
- Entrypoint: `src/OTAPI.UnifiedServerProcess.GlobalNetwork/Program.cs`
- The demo starts two servers and a simple router on one port.


## Architecture at a Glance
- RootContext: Per‑server root that holds the instance‑bound systems you previously accessed as statics (e.g., Main, Netplay, NetMessage, Collision). See `src/OTAPI.UnifiedServerProcess/Mods/RootContext.cs` and `src/OTAPI.UnifiedServerProcess/Core/PatchingLogic.cs`.
- Context‑bound systems: USP’s patching pass rewrites static state and call sites to live under the context. From a mod/plugin perspective you use `ctx.Main`, `ctx.Netplay`, `ctx.NetMessage`, `ctx.Collision`, etc., instead of global `Terraria.*` statics.
- GlobalNetwork sample: Demonstrates sharing global sockets while routing per‑client processing to the correct server context. See `src/OTAPI.UnifiedServerProcess.GlobalNetwork/Network/Router.cs` and `src/OTAPI.UnifiedServerProcess.GlobalNetwork/Servers/ServerContext.cs`.
- TrProtocol: Strong‑typed packet models under `src/TrProtocol/NetPackets/*` plus a source generator for fast (de)serialization.
- TileProvider: Replaces ITile/Tile with TileData + RefTileData and a context‑aware TileCollection. See `src/OTAPI.UnifiedServerProcess/Mods/TileProviderMod.cs`.


## Working with RootContext
RootContext is the per‑server entry point. Derive from it (or use the provided `ServerContext`) to configure world metadata, networking, and gameplay systems per instance instead of touching static `Terraria.*` members.

```csharp
public class MyServer : RootContext
{
    public MyServer(string name, string worldPath) : base(name)
    {
        Main.ActiveWorldFileData = WorldFile.GetAllMetadata(worldPath, false);
        Netplay.ListenPort = -1; // let a global router own the port if needed
    }
}

var ctx = new MyServer("World-A", "/path/to/World-A.wld");
ctx.Console.WriteLine($"[{ctx.Name}] booting…");
ctx.Main.gameMode = 3;
```

Tile interactions stay performant by working with `TileCollection` and `RefTileData`:

```csharp
var tiles = ctx.Main.tile;

for (int x = 0; x < ctx.Main.maxTilesX; x++)
for (int y = 0; y < ctx.Main.maxTilesY; y++)
{
    ref var tile = ref tiles[x, y];
    if (tile.active)
    {
        tile.type = 1;
    }
}
```


## Why TSAPI/TShock Don’t Work on USP
USP is not a drop‑in for the previous OTAPI stacks. Launchers like TSAPI and plugins like TShock assume a single global server (static state) and a specific set of hook signatures and types. USP breaks those assumptions by design:

- Static → Context: Most global singletons are rewritten into per‑server members. Code that calls static `Terraria.Main`, `Terraria.Netplay`, `Terraria.Collision`, etc., will not interact with the correct server unless rewritten to use the context (e.g., `ctx.Main`, `ctx.Netplay`).
- Hook signatures changed: Runtime hooks now live on context‑scoped systems. Many On.* hooks include or imply a `root` (RootContext) and operate on instance members. Old hook points and Harmony patches targeting static methods often no longer match.
- World/tile API changed: Legacy ITile/Tile arrays are replaced by TileData/RefTileData and TileCollection. Any code that takes hard dependencies on the old tile storage or expects reference semantics from the old types will break.
- Network paths adjusted: USP’s pruning and routing alter how `NetMessage` and `Netplay` progress work is scheduled and dispatched. Code that assumed vanilla update order or directly touched static buffers will not function correctly.
- Server‑only trimming: USP removes client‑only branches and narrows the execution graph for dedicated servers, which invalidates some hook locations expected by older frameworks.

Result: TSAPI/TShock would need targeted porting to the USP context model. Running them “as‑is” on USP is unsupported.


## Intended Audience
This project is intended for developers who integrate OTAPI with a unified server process, server operators evaluating such integrations, and contributors who want to explore or extend USP.
Note: This is a development/engineering project; APIs and features may evolve over time.
- Plugin authors who need multi‑server isolation and strong context semantics.
- Server operators wanting to scale multiple worlds in one process with consistent resource usage.
- Developers who want a robust, typed protocol and a safer world data model.


## USP Ecosystem Projects
- [UnifierTSL](https://github.com/CedaryCat/UnifierTSL): A modern launcher that embeds USP, providing an upgraded startup experience and bundling TShock plugins already ported to the unified context model.


## Key References
- RootContext: `src/OTAPI.UnifiedServerProcess/Mods/RootContext.cs`
- Patching pipeline: `src/OTAPI.UnifiedServerProcess/Core/PatchingLogic.cs`
- GlobalNetwork components: `Program.cs`, `Network/Router.cs`, `Servers/ServerContext.cs` under `src/OTAPI.UnifiedServerProcess.GlobalNetwork`
- TileProvider: `Mods/TileProviderMod.cs`
- TrProtocol: models and generators under `src/TrProtocol` and `src/TrProtocol.SerializerGenerator`

External resources for deeper technical detail:
- DeepWiki: `https://deepwiki.com/CedaryCat/OTAPI.UnifiedServerProcess`
- Developer Guide: `docs/Developer-Guide.md`
