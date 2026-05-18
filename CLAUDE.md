# CLAUDE.md — pkhex-mcp

Project memory for Claude Code working in this repo.

## What this is

MCP (Model Context Protocol) server that exposes [PKHeX.Core](https://github.com/kwsch/PKHeX) to Claude over stdio. Single .NET 9 console executable, published as a `dotnet tool` named `pkhex-mcp`.

Source layout:

```
src/PKHeX.MCP/
    Program.cs            # AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()
    SaveContext.cs        # ReaderWriterLockSlim-guarded SaveFile holder (singleton DI)
    PKHeX.MCP.csproj      # net9.0, pinned PKHeX.Core 25.5.18
    Tools/
        SaveFileTools.cs    # Load/Save/ImportShowdown/Export, Search, CheckLegality
        BoxTools.cs         # GetBox*/CountPokemon/BatchEdit*/RenameBox/etc.
        PokemonEditTools.cs # set_level/set_nature/set_moves/set_ivs/.../heal_pokemon
        TrainerTools.cs     # Trainer info, party ops, pokedex, ribbons
        LegalityTools.cs    # quick_check/details/suggest_moves/apply_suggested
        GameDataTools.cs    # get_all_species|moves|items|abilities|balls|natures
        AutoFixTools.cs     # auto_fix_pokemon, mass_auto_fix, legality_audit

tests/                    # Python stdio harness (see "Testing")
    Probe/                # one-shot .NET console for PKHeX.Core reflection lookups
```

57 MCP tools at the time of writing. SDK rewrites method names to snake_case
(`LoadSaveFile` in C# becomes `load_save_file` over MCP). Use `tools/list` to
get the live, authoritative names + input schemas.

## Toolchain quirks on this machine

- .NET 9 SDK is **per-user** at `C:\Users\Gabriel\.dotnet`. Program Files only
  has the .NET 10 runtime. Consequences:
  - `dotnet` is not on PATH by default — prepend `C:/Users/Gabriel/.dotnet`
    in any test/build shell, or run `dotnet.exe` by absolute path.
  - The packed global tool (`~/.dotnet/tools/pkhex-mcp.exe`) needs
    `DOTNET_ROOT=C:\Users\Gabriel\.dotnet` to find the 9.0 runtime — otherwise
    it errors "You must install or update .NET to run this application".
    The MCP entry in `~/.claude.json` sets it via the `env` block.
- Solution file at the repo root contains multiple projects; always pass the
  csproj explicitly to avoid MSB1011:
  `dotnet build src/PKHeX.MCP/PKHeX.MCP.csproj`.

## Build / install loop

After editing a Tool, rebuild + repack + reinstall the global tool, otherwise
the running MCP server keeps the old binary:

```bash
export PATH="/c/Users/Gabriel/.dotnet:$PATH"
cd /d/Dev/pkhex-mcp
dotnet build -c Release src/PKHeX.MCP/PKHeX.MCP.csproj
dotnet pack  -c Release -o ./nupkg src/PKHeX.MCP/PKHeX.MCP.csproj
dotnet tool uninstall -g pkhex-mcp
dotnet tool install   -g pkhex-mcp --add-source ./nupkg --version 0.1.0
```

Claude Code's MCP host caches the server process per session. After
reinstalling the tool, **restart Claude Code** (or use a fresh session) to
pick up the new binary.

## Testing

Python stdio harness under `tests/`. Drives the installed `pkhex-mcp` over
stdio and feeds it the `pokemon heartgold.sav` fixture in the repo root.
Save files are `.gitignore`d — drop your own file with that name (or pass
the path as `sys.argv[1]` where supported).

| Script | Purpose |
|---|---|
| `run_all.py` | Runs every `mcp_*.py` and reports overall pass/fail. |
| `mcp_smoke.py` | Calls every registered tool with valid args, asserts `success=true`, round-trips trainer name through save export+reload. |
| `mcp_verify.py` | Edits ~9 fields, exports save, reloads in fresh server, asserts each field persisted exactly. |
| `mcp_scenarios.py` | Showdown→legal, AutoFix soft/nuclear recovery, mass batch edit, copy/delete, negatives (bad base64, OOB box). |
| `mcp_stress.py` | Bulk imports 10 diverse Gen4-legal Showdown sets, asserts all 10 land legal. |
| `mcp_nuclear.py` | Manual diagnostic for AutoFix nuclear regen on a hard-broken Pikachu. |
| `mcp_massfix.py` | `mass_auto_fix(allowNuclearFix=true)` on the entire save with before/after legality counts. |
| `mcp_deep.py` | Round-trip verify for every mutating tool (held_item, moves, batch_edit_box, copy, rename_box, ...). |
| `dump_schema.py NAME ...` | Prints input-schema property names for the given tool names. Useful when adapting param naming. |
| `Probe/` | Tiny .NET console that lists `PKHeX.Core` types/methods matching a pattern via reflection — used for API-drift discovery. |

Run from repo root:

```bash
python tests/run_all.py
```

## PKHeX.Core 25.5.18 — known API quirks

This codebase migrated from a pre-25.5 version. Pitfalls if you keep editing:

- **Property renames on `PKM`**: `OT_Name` → `OriginalTrainerName`,
  `OT_Gender` → `OriginalTrainerGender`, `Stat_HP` → `Stat_HPCurrent`,
  `MaxHP` → `Stat_HPMax`.
- **Box names**: `sav.GetBoxName(i)` no longer exists on `SaveFile`. Cast to
  `IBoxDetailName` first (the relevant SAV subclasses implement it).
- **Nature on Gen < 8**: `pk.Nature = n` is a no-op for box-stored data
  because nature is derived from `PID % 25`. Use `pk.SetPIDNature(n)` — note
  this re-rolls the PID, which also kills any pre-existing shiny/gender.
  Therefore: **set nature before set_shiny**.
- **Batch edit parser**: `StringInstruction.GetFilters` parses `=Prop=Val` /
  `!Prop=Val` filters; `StringInstruction.GetInstructions` parses
  `.Prop=Val` modifications. The two are NOT interchangeable. Modifications
  flow through `BatchEditing.TryModify(pk, filters, modifications)`.
- **Move suggestions**: `MoveListSuggest.GetSuggestedCurrentMoves(la,
  Span<ushort>, MoveSourceType)` and
  `MoveSetApplicator.GetSuggestedRelearnMoves(la, Span<ushort>,
  IEncounterTemplate?)` are extension methods on `LegalityAnalysis` and
  require pre-allocated `Span<ushort>` buffers; the parameterless overloads
  of the old API are gone.
- **PP**: there's no `pk.SetMaximumPPCurrent()`. Loop the 4 slots and assign
  `pk.MoveN_PP = pk.GetMovePP(pk.MoveN, pk.MoveN_PPUps)`.
- **Ball enum**: `Ball.Undefined` is gone; use `Ball.None` (which is `0`).
- **Cross-gen encounters**: `EncounterMovesetGenerator.GenerateEncounters`
  can return encounters whose `ConvertToPKM(sav)` produces a lower-gen PKM
  (e.g. a PK3 against a Gen4 save). Run it through
  `EntityConverter.ConvertToType(pk, sav.BlankPKM.GetType(), out _)` before
  `sav.SetBoxSlotAtIndex` or you get
  "PKM Format needs to be PKHeX.Core.PK4 when setting to this Save File."
- **`sav.Version` is sometimes an umbrella** (HGSS, BW, ...). Feed
  `GameUtil.GetVersionsWithinRange(probe, sav.Generation)` to the encounter
  generator instead of `new[] { sav.Version }`.
- **Auto-legalizing a ShowdownSet**: `sav.GetLegalFromSet(template)` no
  longer exists — that helper lived in the PKHeX-Plugins (AutoMod) package,
  not in core. The poor man's substitute lives in `SaveFileTools.cs`
  `ImportShowdown` and `AutoFixTools.cs` `RegenerateLegal`: generate
  encounters → `ConvertToPKM(sav)` → `EntityConverter.ConvertToType` →
  carefully overlay `ApplySetDetails` and revert if it breaks legality.

## Conventions in this repo

- All Tool methods return a JSON-encoded `string` (`System.Text.Json`).
  Successful: `{"success": true, ...}`. Failure: `{"success": false,
  "error": "..."}`. Don't throw out of a Tool — wrap in try/catch and
  return an Error object. Callers (Python tests, Claude) rely on
  `success` being a boolean.
- Names in C# are PascalCase; the MCP SDK exposes them as snake_case
  automatically. Keep the C# name expressive — that drives the tool name.
- `[Description("...")]` attributes on parameters become the JSON-schema
  `description` field — write them as you'd want an LLM to read them.
- Box / slot indices are always 0-based in the public API.

## Claude Code MCP integration

Two places register this server:

1. **Per-project, in repo**: `.mcp.json` declares the server with command
   `pkhex-mcp` (must be on PATH or in `~/.dotnet/tools`). Any sibling Claude
   Code session opened in this repo picks it up automatically.

2. **Per-user, in `~/.claude.json`** under
   `"D:/Dev/pkhex-mcp" → mcpServers → pkhex`: full path to the exe plus
   `env.DOTNET_ROOT` for the per-user .NET 9 runtime.

The currently running Claude Code session will **not** see the server until
it restarts — MCP servers are spawned at session init. Future sessions in
this directory will load it transparently.

## License

GPL-3.0-or-later (matches PKHeX.Core).
