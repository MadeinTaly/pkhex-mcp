using ModelContextProtocol.Server;
using PKHeX.Core;
using PKHeXMCP;
using System.ComponentModel;
using System.Text.Json;

namespace PKHeXMCP.Tools;

[McpServerToolType]
public static class BoxTools
{
    [McpServerTool, Description("Get all Pokemon in a specific box.")]
    public static string GetBox(SaveContext ctx,
        [Description("Box index (0-based)")] int box)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var strings = GameInfo.GetStrings("en");
                var slots = new List<object>();
                for (int s = 0; s < sav.BoxSlotCount; s++)
                {
                    var pk = sav.GetBoxSlotAtIndex(box, s);
                    if (pk.Species == 0) { slots.Add(new { slot = s, empty = true }); continue; }
                    slots.Add(new
                    {
                        slot = s,
                        species = strings.specieslist[pk.Species],
                        species_id = pk.Species,
                        nickname = pk.Nickname,
                        level = pk.CurrentLevel,
                        shiny = pk.IsShiny,
                        is_egg = pk.IsEgg,
                        gender = pk.Gender == 0 ? "M" : pk.Gender == 1 ? "F" : "N",
                        nature = pk.Nature.ToString(),
                        ability = strings.abilitylist[pk.Ability],
                    });
                }
                var boxName = $"Box {box + 1}";
                if (sav is IBoxDetailName b)
                    boxName = b.GetBoxName(box);
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    box,
                    box_name = boxName,
                    pokemon = slots
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Get all Pokemon across all boxes. Returns a compact summary.")]
    public static string GetAllBoxes(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var strings = GameInfo.GetStrings("en");
                var all = new List<object>();
                var boxDetailName = sav as IBoxDetailName;
                for (int b = 0; b < sav.BoxCount; b++)
                {
                    var boxName = $"Box {b + 1}";
                    if (boxDetailName != null)
                        boxName = boxDetailName.GetBoxName(b);
                    for (int s = 0; s < sav.BoxSlotCount; s++)
                    {
                        var pk = sav.GetBoxSlotAtIndex(b, s);
                        if (pk.Species == 0) continue;
                        all.Add(new
                        {
                            box = b, slot = s,
                            box_name = boxName,
                            species = strings.specieslist[pk.Species],
                            nickname = pk.Nickname,
                            level = pk.CurrentLevel,
                            shiny = pk.IsShiny,
                            is_egg = pk.IsEgg,
                        });
                    }
                }
                return JsonSerializer.Serialize(new { success = true, total = all.Count, pokemon = all });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Count all shinies across all boxes.")]
    public static string CountShinies(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var strings = GameInfo.GetStrings("en");
                var shinies = new List<object>();
                for (int b = 0; b < sav.BoxCount; b++)
                    for (int s = 0; s < sav.BoxSlotCount; s++)
                    {
                        var pk = sav.GetBoxSlotAtIndex(b, s);
                        if (pk.Species == 0 || !pk.IsShiny) continue;
                        shinies.Add(new { box = b, slot = s, species = strings.specieslist[pk.Species], nickname = pk.Nickname, level = pk.CurrentLevel });
                    }
                return JsonSerializer.Serialize(new { success = true, count = shinies.Count, shinies });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Get box names for all boxes in the save.")]
    public static string GetBoxNames(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var names = new List<object>();
                for (int i = 0; i < sav.BoxCount; i++)
                {
                    string boxName = i.ToString();
                    if (sav is IBoxDetailName b)
                        boxName = b.GetBoxName(i);
                    names.Add(new { box = i, name = boxName });
                }
                return JsonSerializer.Serialize(new { success = true, boxes = names });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Rename a box.")]
    public static string RenameBox(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("New box name")] string name)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                if (sav is IBoxDetailName b)
                {
                    b.SetBoxName(box, name);
                    return Ok();
                }
                return Error("Box renaming not supported for this game format");
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        });
    }

    [McpServerTool, Description("Export all Pokemon in a box as Showdown sets.")]
    public static string ExportBoxShowdown(SaveContext ctx,
        [Description("Box index (0-based)")] int box)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var sets = new List<string>();
                for (int s = 0; s < sav.BoxSlotCount; s++)
                {
                    var pk = sav.GetBoxSlotAtIndex(box, s);
                    if (pk.Species == 0) continue;
                    sets.Add(ShowdownParsing.GetShowdownText(pk));
                }
                return JsonSerializer.Serialize(new { success = true, box, count = sets.Count, sets });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Export ALL Pokemon from all boxes as Showdown sets.")]
    public static string ExportAllShowdown(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var sets = new List<object>();
                for (int b = 0; b < sav.BoxCount; b++)
                    for (int s = 0; s < sav.BoxSlotCount; s++)
                    {
                        var pk = sav.GetBoxSlotAtIndex(b, s);
                        if (pk.Species == 0) continue;
                        sets.Add(new { box = b, slot = s, showdown = ShowdownParsing.GetShowdownText(pk) });
                    }
                return JsonSerializer.Serialize(new { success = true, count = sets.Count, sets });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Run legality check on all Pokemon in all boxes. Returns a summary with valid/invalid counts.")]
    public static string LegalityReportAll(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var strings = GameInfo.GetStrings("en");
                var illegal = new List<object>();
                int total = 0, valid = 0;
                for (int b = 0; b < sav.BoxCount; b++)
                    for (int s = 0; s < sav.BoxSlotCount; s++)
                    {
                        var pk = sav.GetBoxSlotAtIndex(b, s);
                        if (pk.Species == 0) continue;
                        total++;
                        var la = new LegalityAnalysis(pk);
                        if (la.Valid) { valid++; continue; }
                        illegal.Add(new
                        {
                            box = b, slot = s,
                            species = strings.specieslist[pk.Species],
                            nickname = pk.Nickname,
                            issues = la.Results.Where(r => r.Judgement == Severity.Invalid)
                                               .Select(r => r.Comment).ToList()
                        });
                    }
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    total, valid,
                    invalid_count = total - valid,
                    illegal_pokemon = illegal
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Apply batch edit instructions to all Pokemon in all boxes. Instructions use PKHeX batch format: .Property=Value")]
    public static string BatchEditAll(SaveContext ctx,
        [Description("Batch instructions, one per line. Example: .CurrentLevel=100")] string instructions)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var lines = instructions.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var parsed = StringInstruction.GetFilters((IReadOnlyList<string>)lines);
                int modified = 0;
                for (int b = 0; b < sav.BoxCount; b++)
                    for (int s = 0; s < sav.BoxSlotCount; s++)
                    {
                        var pk = sav.GetBoxSlotAtIndex(b, s);
                        if (pk.Species == 0) continue;
                        BatchEditing.TryModify(pk, parsed, parsed);
                        sav.SetBoxSlotAtIndex(pk, b, s);
                        modified++;
                    }
                return JsonSerializer.Serialize(new { success = true, modified });
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Apply batch edit instructions to a specific box only.")]
    public static string BatchEditBox(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Batch instructions, one per line. Example: .CurrentLevel=100")] string instructions)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var lines = instructions.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var parsed = StringInstruction.GetFilters((IReadOnlyList<string>)lines);
                int modified = 0;
                for (int s = 0; s < sav.BoxSlotCount; s++)
                {
                    var pk = sav.GetBoxSlotAtIndex(box, s);
                    if (pk.Species == 0) continue;
                    BatchEditing.TryModify(pk, parsed, parsed);
                    sav.SetBoxSlotAtIndex(pk, box, s);
                    modified++;
                }
                return JsonSerializer.Serialize(new { success = true, box, modified });
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Count total Pokemon in all boxes, including eggs.")]
    public static string CountPokemon(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                int total = 0, eggs = 0;
                for (int b = 0; b < sav.BoxCount; b++)
                    for (int s = 0; s < sav.BoxSlotCount; s++)
                    {
                        var pk = sav.GetBoxSlotAtIndex(b, s);
                        if (pk.Species == 0) continue;
                        total++;
                        if (pk.IsEgg) eggs++;
                    }
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    total,
                    eggs,
                    non_eggs = total - eggs,
                    box_capacity = sav.BoxCount * sav.BoxSlotCount
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    private static string Ok() => JsonSerializer.Serialize(new { success = true });
    private static string Error(string msg) => JsonSerializer.Serialize(new { success = false, error = msg });
}
