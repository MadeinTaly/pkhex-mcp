using ModelContextProtocol.Server;
using PKHeX.Core;
using PKHeXMCP;
using System.ComponentModel;
using System.Text.Json;

namespace PKHeXMCP.Tools;

[McpServerToolType]
public static class SaveFileTools
{
    [McpServerTool, Description("Load a Pokémon save file from base64-encoded bytes. Returns trainer info and save metadata.")]
    public static string LoadSaveFile(SaveContext ctx, [Description("Base64-encoded save file bytes")] string base64Data)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Data);
            var sav = SaveUtil.GetVariantSAV(bytes);
            if (sav is null)
                return JsonSerializer.Serialize(new { success = false, error = "Unrecognized save file format" });
            ctx.SetSave(sav);
            return JsonSerializer.Serialize(new
            {
                success = true,
                game = sav.Version.ToString(),
                trainer = sav.OT,
                tid = sav.TID16,
                sid = sav.SID16,
                box_count = sav.BoxCount,
                slot_count = sav.BoxSlotCount,
                playtime_hours = sav.PlayedHours,
                language = sav.Language
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Get Pokémon data from a specific box and slot in the loaded save file.")]
    public static string GetBoxPokemon(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index within box (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0)
                    return JsonSerializer.Serialize(new { success = true, empty = true });
                var strings = GameInfo.GetStrings("en");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    species = strings.specieslist[pk.Species],
                    species_id = pk.Species,
                    nickname = pk.Nickname,
                    level = pk.CurrentLevel,
                    nature = pk.Nature.ToString(),
                    ability = strings.abilitylist[pk.Ability],
                    gender = pk.Gender == 0 ? "M" : pk.Gender == 1 ? "F" : "N",
                    shiny = pk.IsShiny,
                    is_egg = pk.IsEgg,
                    ball = (Ball)pk.Ball,
                    ot = pk.OriginalTrainerName,
                    moves = new[]
                    {
                        strings.movelist[pk.Move1],
                        strings.movelist[pk.Move2],
                        strings.movelist[pk.Move3],
                        strings.movelist[pk.Move4],
                    },
                    ivs = new { hp = pk.IV_HP, atk = pk.IV_ATK, def = pk.IV_DEF, spa = pk.IV_SPA, spd = pk.IV_SPD, spe = pk.IV_SPE },
                    evs = new { hp = pk.EV_HP, atk = pk.EV_ATK, def = pk.EV_DEF, spa = pk.EV_SPA, spd = pk.EV_SPD, spe = pk.EV_SPE },
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }) ?? JsonSerializer.Serialize(new { success = false, error = "No save file loaded" });
    }

    [McpServerTool, Description("Check the legality of a Pokémon at the given box/slot position.")]
    public static string CheckLegality(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                var la = new LegalityAnalysis(pk);
                var results = la.Results.Select(r => new
                {
                    identifier = r.Identifier.ToString(),
                    message = r.Comment,
                    severity = r.Judgement.ToString()
                });
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    valid = la.Valid,
                    results = results
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }) ?? JsonSerializer.Serialize(new { success = false, error = "No save file loaded" });
    }

    [McpServerTool, Description("Import a Pokémon Showdown set string into a box slot.")]
    public static string ImportShowdown(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot,
        [Description("Pokémon Showdown set text")] string showdownSet)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null)
                return JsonSerializer.Serialize(new { success = false, error = "No save file loaded" });
            try
            {
                var template = new ShowdownSet(showdownSet);
                if (template.Species == 0)
                    return JsonSerializer.Serialize(new { success = false, error = "Species not found in Showdown set" });

                // Build a stub PKM so EncounterMovesetGenerator can match it.
                var probe = sav.BlankPKM;
                probe.ApplySetDetails(template);

                // Generate legal encounters matching this species+moves in the save's game.
                var moves = new ushort[] { (ushort)template.Moves[0], (ushort)template.Moves[1], (ushort)template.Moves[2], (ushort)template.Moves[3] };
                // sav.Version can be an umbrella (HGSS, BW, ...). Split into concrete games for generator.
                var versions = GameUtil.GetVersionsWithinRange(probe, sav.Generation).ToArray();
                if (versions.Length == 0) versions = new[] { sav.Version };
                var encounters = EncounterMovesetGenerator.GenerateEncounters(probe, sav, moves, versions).Take(8).ToList();

                PKM pk;
                string method;
                if (encounters.Count > 0)
                {
                    // Encounter-based: produces a save-compatible, fully-populated PKM.
                    pk = encounters[0].ConvertToPKM(sav);
                    // Overlay Showdown-driven attributes (IVs/EVs/Nature/Moves/Item/Ball/Nickname/Level)
                    pk.ApplySetDetails(template);
                    method = "encounter";
                }
                else
                {
                    // No encounter matched: best-effort blank + trainer inheritance.
                    pk = sav.BlankPKM;
                    pk.ApplySetDetails(template);
                    if (pk.OriginalTrainerName.Length == 0) pk.OriginalTrainerName = sav.OT;
                    pk.TID16 = sav.TID16;
                    pk.SID16 = sav.SID16;
                    pk.Language = sav.Language;
                    pk.Version = sav.Version;
                    method = "fallback_no_encounter";
                }

                sav.SetBoxSlotAtIndex(pk, box, slot);
                var la = new LegalityAnalysis(pk);
                return JsonSerializer.Serialize(new { success = true, species = pk.Species, method, legal = la.Valid });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        });
    }

    [McpServerTool, Description("Export a Pokémon from the save as a Showdown set string.")]
    public static string ExportShowdown(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                var strings = GameInfo.GetStrings("en");
                var set = ShowdownParsing.GetShowdownText(pk);
                return JsonSerializer.Serialize(new { success = true, showdown = set });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }) ?? JsonSerializer.Serialize(new { success = false, error = "No save file loaded" });
    }

    [McpServerTool, Description("Get the current save file serialized as base64 (to download the modified save).")]
    public static string GetSaveBase64(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var data = sav.Write();
                return JsonSerializer.Serialize(new { success = true, base64 = Convert.ToBase64String(data) });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }) ?? JsonSerializer.Serialize(new { success = false, error = "No save file loaded" });
    }

    [McpServerTool, Description("Search all boxes for Pokémon matching a species name or nickname.")]
    public static string SearchPokemon(SaveContext ctx,
        [Description("Species name or nickname to search for (case-insensitive)")] string query)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var strings = GameInfo.GetStrings("en");
                var results = new List<object>();
                for (int b = 0; b < sav.BoxCount; b++)
                {
                    for (int s = 0; s < sav.BoxSlotCount; s++)
                    {
                        var pk = sav.GetBoxSlotAtIndex(b, s);
                        if (pk.Species == 0) continue;
                        var speciesName = strings.specieslist[pk.Species];
                        if (speciesName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            pk.Nickname.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new { box = b, slot = s, species = speciesName, nickname = pk.Nickname, shiny = pk.IsShiny, level = pk.CurrentLevel });
                        }
                    }
                }
                return JsonSerializer.Serialize(new { success = true, count = results.Count, matches = results });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }) ?? JsonSerializer.Serialize(new { success = false, error = "No save file loaded" });
    }
}
