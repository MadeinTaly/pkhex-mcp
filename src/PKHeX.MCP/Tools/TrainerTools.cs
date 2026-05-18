using ModelContextProtocol.Server;
using PKHeX.Core;
using PKHeXMCP;
using System.ComponentModel;
using System.Text.Json;

namespace PKHeXMCP.Tools;

[McpServerToolType]
public static class TrainerTools
{
    [McpServerTool, Description("Get detailed trainer and save file information.")]
    public static string GetTrainerInfo(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    trainer_name = sav.OT,
                    game = sav.Version.ToString(),
                    generation = sav.Generation,
                    language = ((LanguageID)sav.Language).ToString(),
                    tid = sav.TID16,
                    sid = sav.SID16,
                    playtime = $"{sav.PlayedHours}h {sav.PlayedMinutes}m {sav.PlayedSeconds}s",
                    box_count = sav.BoxCount,
                    box_slot_count = sav.BoxSlotCount,
                    party_count = sav.PartyCount,
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Set the trainer name in the save file.")]
    public static string SetTrainerName(SaveContext ctx,
        [Description("New trainer (OT) name")] string name)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try { sav.OT = name; return Ok(); }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Get all party Pokemon (the 6 Pokemon the trainer is currently carrying).")]
    public static string GetParty(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var strings = GameInfo.GetStrings("en");
                var party = new List<object>();
                for (int i = 0; i < sav.PartyCount; i++)
                {
                    var pk = sav.GetPartySlotAtIndex(i);
                    party.Add(new
                    {
                        slot = i,
                        species = strings.specieslist[pk.Species],
                        nickname = pk.Nickname,
                        level = pk.CurrentLevel,
                        hp = pk.Stat_HP,
                        max_hp = pk.MaxHP,
                        status = pk.Status_Condition == 0 ? "Healthy" : pk.Status_Condition.ToString(),
                        shiny = pk.IsShiny,
                        nature = pk.Nature.ToString(),
                        ability = strings.abilitylist[pk.Ability],
                        held_item = pk.HeldItem > 0 ? strings.itemlist[pk.HeldItem] : "None",
                        moves = new[]
                        {
                            strings.movelist[pk.Move1], strings.movelist[pk.Move2],
                            strings.movelist[pk.Move3], strings.movelist[pk.Move4],
                        }
                    });
                }
                return JsonSerializer.Serialize(new { success = true, party_count = sav.PartyCount, party });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Heal all party Pokemon to full HP and restore all PP.")]
    public static string HealParty(SaveContext ctx)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                for (int i = 0; i < sav.PartyCount; i++)
                {
                    var pk = sav.GetPartySlotAtIndex(i);
                    pk.Stat_HP = pk.MaxHP;
                    pk.SetMaximumPPCurrent();
                    pk.Status_Condition = 0;
                    sav.SetPartySlotAtIndex(pk, i);
                }
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Move a box Pokemon to a party slot (replaces what's there).")]
    public static string BoxToParty(SaveContext ctx,
        [Description("Source box index (0-based)")] int box,
        [Description("Source slot index (0-based)")] int slot,
        [Description("Party slot index (0-5)")] int partySlot)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0) return Error("Source slot is empty");
                pk.ResetPartyStats();
                sav.SetPartySlotAtIndex(pk, partySlot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Move a party Pokemon to a box slot.")]
    public static string PartyToBox(SaveContext ctx,
        [Description("Party slot index (0-5)")] int partySlot,
        [Description("Destination box index (0-based)")] int box,
        [Description("Destination slot index (0-based)")] int slot)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetPartySlotAtIndex(partySlot);
                if (pk.Species == 0) return Error("Party slot is empty");
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Get the Pokedex completion statistics (seen and caught counts).")]
    public static string GetPokedexStats(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                if (sav is not IPokedex dex)
                    return Error("This save type does not support Pokedex access");

                int seen = 0, caught = 0;
                int max = sav.MaxSpeciesID;
                for (int i = 1; i <= max; i++)
                {
                    if (dex.GetSeen(i)) seen++;
                    if (dex.GetCaught(i)) caught++;
                }
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    seen,
                    caught,
                    total_species = max,
                    seen_percent = Math.Round(seen * 100.0 / max, 1),
                    caught_percent = Math.Round(caught * 100.0 / max, 1)
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Get all ribbons on a Pokemon.")]
    public static string GetRibbons(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0) return Error("Slot is empty");
                var ribbons = RibbonInfo.GetRibbonResults(pk)
                    .Where(r => r.HasRibbon)
                    .Select(r => r.Name)
                    .ToList();
                return JsonSerializer.Serialize(new { success = true, count = ribbons.Count, ribbons });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Get save file metadata: file format, checksums valid, generation.")]
    public static string GetSaveMetadata(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    game_version = sav.Version.ToString(),
                    generation = sav.Generation,
                    save_type = sav.GetType().Name,
                    max_species_id = sav.MaxSpeciesID,
                    max_move_id = sav.MaxMoveID,
                    max_item_id = sav.MaxItemID,
                    max_ability_id = sav.MaxAbilityID,
                    box_count = sav.BoxCount,
                    box_slot_count = sav.BoxSlotCount,
                    has_party = sav.HasParty,
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Get a list of all nature names with their stat modifiers.")]
    public static string GetNatures()
    {
        try
        {
            var natures = Enum.GetValues<Nature>()
                .Where(n => (int)n < 25)
                .Select(n =>
                {
                    int i = (int)n;
                    string raised = i % 5 == i / 5 ? "None" : new[] { "Attack", "Defense", "Speed", "Sp.Atk", "Sp.Def" }[i / 5];
                    string lowered = i % 5 == i / 5 ? "None" : new[] { "Attack", "Defense", "Speed", "Sp.Atk", "Sp.Def" }[i % 5];
                    return new { id = i, name = n.ToString(), raised, lowered };
                }).ToList();
            return JsonSerializer.Serialize(new { success = true, natures });
        }
        catch (Exception ex) { return Error(ex.Message); }
    }

    [McpServerTool, Description("Get a list of all ball types with their IDs.")]
    public static string GetBalls()
    {
        try
        {
            var balls = Enum.GetValues<Ball>()
                .Where(b => b != Ball.None && b != Ball.Undefined)
                .Select(b => new { id = (int)b, name = b.ToString() })
                .ToList();
            return JsonSerializer.Serialize(new { success = true, balls });
        }
        catch (Exception ex) { return Error(ex.Message); }
    }

    private static string Ok() => JsonSerializer.Serialize(new { success = true });
    private static string Error(string msg) => JsonSerializer.Serialize(new { success = false, error = msg });
}
