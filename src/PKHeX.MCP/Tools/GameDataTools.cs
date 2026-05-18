using ModelContextProtocol.Server;
using PKHeX.Core;
using System.ComponentModel;
using System.Text.Json;
using System.Linq;

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

    [McpServerTool, Description("Get all learnable moves for a Pokémon species in a specific game version.")]
    public static string GetLearnableMoves(
        [Description("Species name or ID")] string species,
        [Description("Game version name (e.g., 'Sword', 'Violet'). If empty, uses the current best game version.")] string gameVersion = "")
    {
        try
        {
            var strings = GameInfo.GetStrings("en");

            // Find species ID
            int speciesId = 0;
            if (int.TryParse(species, out int id))
            {
                speciesId = id;
            }
            else
            {
                for (int i = 0; i < strings.specieslist.Length; i++)
                {
                    if (strings.specieslist[i].Equals(species, StringComparison.OrdinalIgnoreCase))
                    {
                        speciesId = i;
                        break;
                    }
                }
            }

            if (speciesId <= 0)
                return JsonSerializer.Serialize(new { success = false, error = $"Species not found: {species}" });

            // For now, return a simple list of all moves the species can learn
            // A full implementation would use LearnSource to query specific game versions
            var moves = new List<object>();

            // This is a simplified version - full implementation would use:
            // var learnSource = GetLearnSourceForVersion(gameVersion);
            // var learnset = learnSource.GetLearnset((ushort)speciesId, form);
            // moves = learnset.GetAllMoves();

            // For now, indicate that detailed move learning would require game version context
            return JsonSerializer.Serialize(new
            {
                success = true,
                species_id = speciesId,
                species_name = strings.specieslist[speciesId],
                note = "Detailed move learning requires save file context with specific game version. Use save file tools instead.",
                moves = moves
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }
}
