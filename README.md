# pkhex-mcp

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com/download/dotnet/9.0)

MCP (Model Context Protocol) server that exposes [PKHeX.Core](https://github.com/kwsch/PKHeX) functionality to AI assistants like Claude.

Load a Pokemon save file, inspect your boxes, check legality, import Showdown sets, and export modified saves — all through natural language.

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Installation

### As a dotnet tool (global)

```bash
dotnet tool install -g pkhex-mcp
```

### From source

```bash
git clone https://github.com/MadeinTaly/pkhex-mcp
cd pkhex-mcp
dotnet run --project src/PKHeX.MCP/
```

## Claude Code configuration

Add to your `.claude/settings.json`:

```json
{
  "mcpServers": {
    "pkhex": {
      "command": "pkhex-mcp",
      "description": "PKHeX save file editor"
    }
  }
}
```

Or if running from source:

```json
{
  "mcpServers": {
    "pkhex": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/pkhex-mcp/src/PKHeX.MCP/"]
    }
  }
}
```

## Available tools

57 MCP tools at the moment, grouped roughly as:

| Group | Tools |
|---|---|
| Save I/O | `load_save_file`, `get_save_base64`, `get_save_metadata` |
| Trainer | `get_trainer_info`, `set_trainer_name`, `get_pokedex_stats` |
| Boxes | `get_box`, `get_all_boxes`, `get_box_names`, `rename_box`, `count_pokemon`, `count_shinies`, `search_pokemon` |
| Pokemon read | `get_box_pokemon`, `get_pokemon_stats`, `get_ribbons` |
| Pokemon edit | `set_level`, `set_nickname`, `set_nature`, `set_shiny`, `set_friendship`, `set_held_item`, `set_ot`, `set_i_vs`, `set_e_vs`, `max_i_vs`, `set_moves`, `heal_pokemon`, `delete_pokemon`, `copy_pokemon` |
| Slot moves | `box_to_party`, `party_to_box` |
| Showdown | `import_showdown`, `export_showdown`, `export_box_showdown`, `export_all_showdown` |
| Legality | `check_legality`, `quick_legality_check`, `get_legality_details`, `legality_report_all`, `legality_audit` |
| Auto-fix | `auto_fix_pokemon`, `mass_auto_fix`, `suggest_moves`, `apply_suggested_moves` |
| Party | `get_party`, `heal_party` |
| Batch | `batch_edit_box`, `batch_edit_all` |
| Reference data | `get_all_species`, `get_all_moves`, `get_all_items`, `get_all_abilities`, `get_balls`, `get_natures`, `get_valid_balls`, `get_learnable_moves` |

Call `tools/list` over MCP to get the full live list with input schemas.

## Example prompts

```
"Load this save file and list all my shinies"
"Check legality of the Pokemon in box 1 slot 0"
"Import this Showdown set into box 3 slot 5"
"Export my box 1 as Showdown sets"
"Search for all my Charizard"
```

## Development

Build from a fresh clone:

```bash
dotnet build -c Release src/PKHeX.MCP/PKHeX.MCP.csproj
dotnet pack  -c Release src/PKHeX.MCP/PKHeX.MCP.csproj -o ./nupkg
dotnet tool install -g pkhex-mcp --add-source ./nupkg
```

If the global tool fails with `You must install or update .NET to run this application` (because the runtime is in a per-user directory), point `DOTNET_ROOT` at the SDK install directory before launching the server.

### Tests

`tests/` ships a Python harness that drives the installed `pkhex-mcp` over stdio against a real save file (default path: `pokemon heartgold.sav` in the repo root — provide your own; save files are `.gitignore`d).

```bash
python tests/run_all.py
```

What each script does:

| Script | Purpose |
|---|---|
| `mcp_smoke.py` | Calls every registered MCP tool (57 at time of writing) with valid args, asserts `success=true`, then round-trips the save (export → reload → check trainer name persisted). |
| `mcp_verify.py` | Edits a real Pokemon (level/nickname/shiny/nature/OT/IVs/EVs/friendship/trainer name), exports the save, reloads in a fresh server, asserts every field persisted with the expected value. |
| `mcp_scenarios.py` | Showdown import → legality, AutoFix recovery, mass batch edit, copy/delete, negative paths (bad base64 / out-of-range box), nuclear regen. |
| `mcp_nuclear.py` | Manual diagnostic for the AutoFix nuclear regen path. |
| `mcp_stress.py` | Bulk-imports a list of Gen4-legal Showdown sets and asserts each one ends up legal. |
| `dump_schema.py NAME ...` | Prints the input-schema property names for given tools (handy when adapting to PKHeX.Core API drift). |
| `Probe/` | One-shot .NET console that lists matching PKHeX.Core types/methods via reflection. |

## Continuing in a new Claude Code session

The MCP server is registered both in this repo's `.mcp.json` and in the
per-user `~/.claude.json`, so a fresh Claude Code session opened in
`D:\Dev\pkhex-mcp` picks up the `pkhex_*` tools automatically. The
currently running session needs a restart to see them — MCP servers are
spawned at session init.

See `CLAUDE.md` in the repo root for project memory: build/install loop,
API-drift quirks, conventions, and a map of the codebase.

## License

GPL-3.0-or-later. This project uses [PKHeX.Core](https://github.com/kwsch/PKHeX) by kwsch, also GPL-3.0.
