# Developing on OTAPI Unified Server Process (USP)

## Quick Start
- Install: Use the NuGet package `OTAPI.USP` to get `OTAPI.dll` and `OTAPI.Runtime.dll`.
- Build: Restore and build locally if you need to work from source.
- Run: See the Build from Source section for commands.

## Table of Contents
- [OTAPI Ecosystem and USP](#otapi-ecosystem-and-usp)
- [Install via NuGet (recommended)](#install-via-nuget-recommended)
- [Build from Source (secondary)](#build-from-source-secondary)
- [Core Concepts](#core-concepts)
- [Creating and Using a Context](#creating-and-using-a-context)
- [World Data: TileProvider](#world-data-tileprovider)
- [Protocol: TrProtocol (IL-merged)](#protocol-trprotocol-il-merged)
- [Collision (pre-patch ref parameters for performance)](#collision-pre-patch-ref-parameters-for-performance)
- [Networking in Multi-Server: GlobalNetwork](#networking-in-multi-server-globalnetwork)
- [Hooks and Extensions](#hooks-and-extensions)
- [Compatibility Note (USP vs. TSAPI/TShock)](#compatibility-note-usp-vs-tsapitshock)
- [Next Steps](#next-steps)
- [References](#references)

This guide explains how to build against USP’s context‑bound server core, how to migrate code from older OTAPI/TShock style statics, and how to leverage TrProtocol, TileProvider, and Collision efficiently. It also clarifies where USP sits in the OTAPI ecosystem.


## OTAPI Ecosystem and USP
- Historical flow: Launchers like TSAPI (TShock.Launcher) start the server, TShock runs as a plugin; both depend on the OTAPI NuGet package that ships `OTAPI.dll` and `OTAPI.Runtime.dll`.
- What USP does: `OTAPI.UnifiedServerProcess` performs a second IL patch over an existing OTAPI.dll and regenerates a new `OTAPI.Runtime.dll`, while keeping the original assembly names for compatibility with tooling and habits.


## Install via NuGet (recommended)
Use the published package `OTAPI.USP` which includes both `OTAPI.dll` and `OTAPI.Runtime.dll`.

```
cd YourProject
dotnet add package OTAPI.USP
```

After restore/build, the assemblies are available from NuGet; you don’t need to build USP locally. If you do build USP, note that `OTAPI.dll` and `OTAPI.Runtime.dll` are emitted to the build configuration’s `output` folder under the project bin.

## Build from Source (secondary)
```
cd OTAPI.UnifiedServerProcess
dotnet restore src/OTAPI.UnifiedServerProcess.sln
dotnet build src/OTAPI.UnifiedServerProcess.sln -c Release
dotnet run -c Debug -p src/OTAPI.UnifiedServerProcess
# Output: src/OTAPI.UnifiedServerProcess/bin/Debug/net9.0/output/OTAPI.dll (+ OTAPI.Runtime.dll)
```


## Core Concepts
### Root Context
- The per‑server root object. Systems that used to be static on Terraria (e.g., `Main`, `Netplay`, `NetMessage`) are accessed through the context instance.
### Context-Aware Hooks
- On.* hooks operate with or within the context. Signatures and targets may differ from older OTAPI.
### Multi-Server Isolation
- Avoid direct static access to `Terraria.*` when correctness across multiple servers matters. Prefer context-bound access to maintain isolation.


## Creating and Using a Context
Minimal example based on `ServerContext`:
```csharp
using UnifiedServerProcess;
using Terraria;

public class MyServer : RootContext
{
    public MyServer(string name, string worldPath) : base(name)
    {
        Main.ActiveWorldFileData = WorldFile.GetAllMetadata(worldPath, false);
        Main.maxNetPlayers = byte.MaxValue;
        Netplay.ListenPort = -1; // Let a global router own the port if needed
    }
}

// Create and use the context
var ctx = new MyServer("World-A", "/path/to/World-A.wld");
ctx.Console.WriteLine($"[{ctx.Name}] server booting...");
// Access instance‑bound systems through the context
ctx.Main.gameMode = 3;
```

Migrating static access:
```csharp
// Before
Terraria.Main.player[i].active = true;
Terraria.NetMessage.SendData(25, -1, -1, NetworkText.Empty);

// After (context‑bound)
ctx.Main.player[i].active = true;
ctx.NetMessage.SendData(25, -1, -1, Terraria.Localization.NetworkText.Empty);
```


### World Data: TileProvider
USP replaces legacy `ITile`/`Tile` usage with `TileData` (struct), `RefTileData` (ref‑like holder), and a context‑bound `TileCollection`.

### Ref-Friendly Iteration:
```csharp
// Iterate tiles with ref access to minimize copies
var tiles = ctx.Main.tile; // TileCollection

for (int x = 0; x < ctx.Main.maxTilesX; x++)
for (int y = 0; y < ctx.Main.maxTilesY; y++)
{
    ref TileData tile = ref tiles[x, y] // ref TileData
    if (tile.active) {
        tile.type = 1; // Example write
    }
}
```

Passing stable handles across APIs:
```csharp
Tuple<int, int, RefTileData> heapStorage = new(0, 0, default) // heap object
heapStorage.Item1 = 100;
heapStorage.Item2 = 200;
heapStorage.Item3 = ctx.Main.tile.GetRefTile(100, 200); // storage reference on heap safety

ref tile = ref heapStorage.Item3.Data; // RefTileData -> TileData
```

Reference: `src/OTAPI.UnifiedServerProcess/Mods/TileProviderMod.cs`


## Protocol: TrProtocol (IL-Merged)
USP IL‑merges `TrProtocol` into the server core and merges public type metadata with OTAPI types. This enables seamless serialization for shared models. For example, `Terraria.DataStructures.PlayerDeathReason` gains generated read/write that operate directly on unmanaged pointers.

- Read/Write behavior
- The pack entities provide basic pointer-based deserialization: `void ReadContent(ref void* ptr)` and `void WriteContent(ref void* ptr)`.
- For a packet instance, the packet type is fixed. Therefore deserialization begins from the position dictated by the type metadata: normal packets read starting at the 2-byte length + 1-byte type header; Module packets read from 2-byte length + 1-byte type + 2-byte ModuleType.
- WriteContent writes the type information into the buffer as part of serialization.

### Serialize/Deserialize Example
```csharp
unsafe
{
    byte* buf = stackalloc byte[256];
    void* ptr = buf + 2;

    var p = new TrProtocol.NetPackets.SpawnPlayer(_PlayerSlot: 1, ...);
    p.WriteContent(ref ptr);
    short len = (short)((byte*)ptr - buf);
    *((short*)buf) = len;
    
    void* rptr = buf + 2;
    var p2 = new TrProtocol.NetPackets.SpawnPlayer(ref rptr); // 

    void* rptr = buf + 2;
    var p3 = new TrProtocol.NetPackets.SpawnPlayer(); // 
    p3.ReadContent(ref rptr);
}
```
- ILengthAware and IExtraData
  - ILengthAware packets know their binary length and thus participate in contexts where trailing or compressed data exists (e.g., `ExtraData`). Such packets explicitly implement `ReadContent(ref void* ptr, void* end_ptr)` to read up to the end boundary, and `WriteContent(ref void* ptr)` to write payloads. The plain `ReadContent(ref void* ptr)` API is hidden via explicit interface implementation to avoid surface API that ignores the boundary.
  - IExtraData extends ILengthAware and adds an `ExtraData` byte[] property. The generator will add this property on types that implement `IExtraData` and handle copying any remaining bytes into it during deserialization.
  - For example, `TileSection` (which uses compression) relies on knowing its data range and thus participates in the length-aware path.
  - These rules are enforced by the generated serializers. They explicitly hide the non-boundary `ReadContent` via explicit implementation like:
    ``` csharp
    /// <summary>
    /// This operation is not supported and always throws a System.NotSupportedException.
    /// </summary>
    [Obsolete]
    void IBinarySerializable.ReadContent(ref void* ptr) => throw new NotSupportedException();
    ```
    
    // Public surface
    void ReadContent(ref void* ptr, void* end_ptr);
- ISideSpecific and server/client roles
  - Data packets implementing ISideSpecific require the correct execution role before de/serialization. The IsServerSide property must reflect the current context; there is no extra parameter for ReadContent/WriteContent to indicate the role. Some packets use attributes such as [A2BOnly] to designate end-specific logic.
  - NetTextModule demonstrates this with two fields:
    ``` csharp
    [C2SOnly]
    public TextC2S? TextC2S;
    [S2COnly]
    public TextS2C? TextS2C;
    ```
  - Developers manually ensure `IsServerSide` is set correctly on ISideSpecific packets. The source generator does not modify that behavior.

- Constructors and ergonomics
  - Packets expose convenient constructors generated by the source generator. The simple constructor maps to the public fields/properties (a direct one-to-one assignment).
  - For ISideSpecific packets, an additional constructor variant accepts a boolean `isServerSide` to set `IsServerSide` accordingly when desired.
  - If a type uses `InitDefaultValue` on fields, the generator will also provide a second constructor that prioritizes parameter order and default values, easing usage for common initialization.
  - Example: WorldData
    ```csharp
    public partial struct WorldData : INetPacket
    {
        public readonly MessageID Type => MessageID.WorldData;
        [InitDefaultValue] public int Time;
        [InitDefaultValue] public BitsByte DayAndMoonInfo;
        [InitDefaultValue] public byte MoonPhase;
        public short MaxTileX;
        public short MaxTileY;
        public short SpawnX;
        public short SpawnY;
        // …
    }
    ```
    The generator will produce two constructors, for example:
    - `public WorldData(int _Time, BitsByte _DayAndMoonInfo, byte _MoonPhase, short _MaxTileX, ...)` and
    - `public WorldData(short _MaxTileX, short _MaxTileY, ..., int _Time = default, BitsByte _DayAndMoonInfo = default, byte _MoonPhase = default, ...)`
    to simplify user code.

- Example usage remains valid, with the generated constructors reducing boilerplate.

## Collision (pre-patch ref parameters for performance)
`PatchCollision` transforms `Terraria.Collision` static fields (e.g., `up`, `down`, `left`, `right`, etc.) into method `ref` parameters and updates all call sites. These fields were temporary variables and outputs; rewriting them early avoids deep context field dereferences and improves performance (stack allocation, clearer data flow, fewer indirections). Because of this, Collision internals are no longer runtime‑modified fields and are not converted into context instances.

Usage pattern (illustrative):

```csharp
using Microsoft.Xna.Framework;
using Terraria;

float up = 0, down = 0, left = 0, right = 0;
var vel = new Vector2(4, 0);
var pos = new Vector2(100, 200);

// Signatures vary by method; pass ref outputs as required
// e.g., Collision.TileCollision(pos, vel, width, height, fallThrough, canJump, gravDir, ref up, ref down, ref left, ref right);
```

Reference: `src/OTAPI.UnifiedServerProcess/Core/PatchCollision.cs`

## Networking in Multi‑Server: GlobalNetwork
`GlobalNetwork/Network/Router` demonstrates shared sockets and per‑client routing to the correct server:
- Global arrays: `Router.globalClients`, `Router.globalMsgBuffers` are shared.
- Per‑client context: `GetClientCurrentlyServer(i)` returns the owning context.
- Scheduling: Only processes messages for clients belonging to the current server’s context.
- Transfer: `Router.ServerTransfer(byte plr, ServerContext to)` moves a player by swapping `Main.player[plr]`, resetting sections, and sending join/leave sync.

Reference: `src/OTAPI.UnifiedServerProcess.GlobalNetwork/Network/Router.cs`, `src/OTAPI.UnifiedServerProcess.GlobalNetwork/Servers/ServerContext.cs`


## Hooks and Extensions
- Runtime hooks are in `OTAPI.Runtime.dll` (On.* style), targeting context‑bound or updated systems. Review signatures against your USP build.
- Prefer hooking the context versions over legacy static targets.


## Compatibility Note (USP vs. TSAPI/TShock)
USP’s context model is incompatible with frameworks that depend on global static state and legacy tile/network APIs. Porting requires:
- Rewriting static accesses to context members
- Updating hook targets/signatures to the context versions
- Migrating world/tile interactions to `TileData`/`RefTileData`/`TileCollection`
- Adapting to USP’s pruned server execution paths


## Next Steps
- Explore the demo in `src/OTAPI.UnifiedServerProcess.GlobalNetwork`.
- Design APIs that pass or encapsulate `RootContext` explicitly.
- Use TrProtocol for custom packets and TileProvider for world operations to maximize performance and clarity.
