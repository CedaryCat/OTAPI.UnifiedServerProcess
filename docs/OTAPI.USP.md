# OTAPI Unified Server Process

**OTAPI.USP** is a secondary IL patch built on top of [OTAPI](https://github.com/SignatureBeef/Open-Terraria-API), enabling **multiple isolated Terraria server instances to run within a single process**.

Unlike standard OTAPI builds, this package introduces a **breaking change** by redirecting access to static fields within the Terraria server to their **instance-scoped equivalents**. This change allows each server instance to operate in a fully isolated execution context—enabling true multi-server concurrency within a single host process.

## Key Features

- 🧩 Built on top of OTAPI (IL-patched `TerrariaServer.exe`)
- ⚠️ Breaking change: static server state is redirected to instance-level context
- 🧵 Supports multiple Terraria server instances running in parallel threads
- 💡 Enables advanced hosting models (e.g., high-density server clusters, custom orchestration, dynamic scaling)

## Compatibility

- Terraria: **1.4.5.5**
- OTAPI: `[INJECT_OTAPI_VERSION]`

## Important Notes

- This package is not drop-in compatible with standard OTAPI mods.
- Due to static field redirection, code that relies on traditional static access (e.g., `Main.time`, `Netplay`) must be adapted to use the appropriate instance context.
- For mod development or integration, consult the API documentation or source examples on GitHub.

## Licensing

This package follows the licensing terms of OTAPI and the original Terraria server executable. See `COPYING.txt` for details.

