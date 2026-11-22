# GangsOfBharat.Shared

Game core shared by the Unity client and the .NET game server. Both sides compile the same `.cs` files, so the protocol, catalogs, movement code and localization keys can never drift apart.

## How Unity uses this folder

Unity does not reference the `.csproj` or the built DLL. It compiles the source directly:

- The Unity project (`client/GangsOfBharat.Unity/`) includes this folder through an assembly definition (`GangsOfBharat.Shared.asmdef`) placed next to a symlink or a copy of the folder under `Assets/`. A copy step in CI keeps it in sync when symlinks are not available (Windows).
- `GangsOfBharat.Shared.csproj` exists only so the server, tests and bots can build it with the .NET 8 SDK.

## Rules for code in this folder

Unity's C# compiler and its .NET Standard 2.1 profile are older than .NET 8, so code here must stay compatible:

- C# 9 (`<LangVersion>9.0</LangVersion>`), no nullable annotations, no implicit or global usings.
- No records, file-scoped namespaces, `init` setters, `required` members or raw string literals.
- Only `System`, `System.Collections.Generic`, `System.Globalization`, `System.Text` and `System.Net`. No `Span<T>`-heavy APIs or BCL types newer than .NET Standard 2.1.
- No NuGet packages and no `unsafe` code.
- No allocations in hot paths (serialization, grid queries): reuse buffers and pass caller-owned lists.
- Money is `long` rupees, never `float`.
- Player-facing strings go through `Localization/LocalizationTable` keys with both `en` and `hi` entries.
- Vehicle brands are parodies only (Marutha, Mahindar, Tatva, Ranga Rover, Mercedez, BMV, Lambor-Gini, Ferraro).

## Contents

| Folder | Contents |
| --- | --- |
| `Serialization/` | `BitWriter`, `BitReader`, quantization helpers and the world bounds (8192 m x 8192 m, about 0.8 cm position precision) |
| `Net/` | `Protocol` (message types and channels) and `ReliableChannel` (sequence numbers, acks, resends) |
| `World/` | `SpatialHashGrid` (64 m cells) and `InterestManager` (near/mid/far tiers, nearest-N cap) |
| `Gameplay/` | Vehicle, job, faction and business catalogs, and the deterministic `Movement` step |
| `Localization/` | English and Hindi strings for server-sent UI text |
