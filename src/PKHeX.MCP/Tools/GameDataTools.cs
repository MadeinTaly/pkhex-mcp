using ModelContextProtocol.Server;
using PKHeX.Core;
using System.ComponentModel;
using System.Text.Json;

namespace PKHeXMCP.Tools;

[McpServerToolType]
public static class GameDataTools
{
    [McpServerTool, Description("Get list of all Pokémon species with their IDs.")]
    public static string GetAllSpecies()
    {
        var strings = GameInfo.GetStrings("en");
        var list = strings.specieslist
            .Select((name, id) => new { id, name })
            .Where(x => x.id > 0 && !string.IsNullOrWhiteSpace(x.name))
            .ToList();
        return JsonSerializer.Serialize(new { success = true, count = list.Count, species = list });
    }

    [McpServerTool, Description("Get list of all moves with their IDs.")]
    public static string GetAllMoves()
    {
        var strings = GameInfo.GetStrings("en");
        var list = strings.movelist
            .Select((name, id) => new { id, name })
            .Where(x => x.id > 0 && !string.IsNullOrWhiteSpace(x.name))
            .ToList();
        return JsonSerializer.Serialize(new { success = true, count = list.Count, moves = list });
    }

    [McpServerTool, Description("Get list of all items with their IDs.")]
    public static string GetAllItems()
    {
        var strings = GameInfo.GetStrings("en");
        var list = strings.itemlist
            .Select((name, id) => new { id, name })
            .Where(x => x.id > 0 && !string.IsNullOrWhiteSpace(x.name))
            .ToList();
        return JsonSerializer.Serialize(new { success = true, count = list.Count, items = list });
    }

    [McpServerTool, Description("Get list of all abilities with their IDs.")]
    public static string GetAllAbilities()
    {
        var strings = GameInfo.GetStrings("en");
        var list = strings.abilitylist
            .Select((name, id) => new { id, name })
            .Where(x => x.id > 0 && !string.IsNullOrWhiteSpace(x.name))
            .ToList();
        return JsonSerializer.Serialize(new { success = true, count = list.Count, abilities = list });
    }
}
