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

| Tool | Description |
|------|-------------|
| `LoadSaveFile` | Load a save file from base64 bytes |
| `GetBoxPokemon` | Read Pokemon data from box/slot |
| `CheckLegality` | Run legality analysis on a Pokemon |
| `ImportShowdown` | Import a Showdown set into a box slot |
| `ExportShowdown` | Export a Pokemon as Showdown set text |
| `GetSaveBase64` | Download the modified save as base64 |
| `SearchPokemon` | Search boxes by species name or nickname |
| `GetAllSpecies` | List all species with IDs |
| `GetAllMoves` | List all moves with IDs |
| `GetAllItems` | List all items with IDs |
| `GetAllAbilities` | List all abilities with IDs |

## Example prompts

```
"Load this save file and list all my shinies"
"Check legality of the Pokemon in box 1 slot 0"
"Import this Showdown set into box 3 slot 5"
"Export my box 1 as Showdown sets"
"Search for all my Charizard"
```

## License

GPL-3.0-or-later. This project uses [PKHeX.Core](https://github.com/kwsch/PKHeX) by kwsch, also GPL-3.0.
