using ModelContextProtocol.Server;
using PKHeX.Core;
using PKHeXMCP;
using System.ComponentModel;
using System.Text.Json;

namespace PKHeXMCP.Tools;

[McpServerToolType]
public static class LegalityTools
{
    [McpServerTool, Description("Get all legal moves a Pokemon can learn in its current game version.")]
    public static string GetLearnableMoves(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0) return Error("Slot is empty");
                var strings = GameInfo.GetStrings("en");
                var moves = GameData.GetLearnset(pk.Species, pk.Form, pk.Version)
                    .GetAllMoves()
                    .Where(m => m < strings.movelist.Length)
                    .Select(m => new { id = (int)m, name = strings.movelist[m] })
                    .ToList();
                return JsonSerializer.Serialize(new { success = true, count = moves.Count, moves });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Get detailed legality analysis for a Pokemon, grouped by category.")]
    public static string GetLegalityDetails(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                var la = new LegalityAnalysis(pk);
                var strings = GameInfo.GetStrings("en");
                var grouped = la.Results
                    .GroupBy(r => r.Identifier)
                    .Select(g => new
                    {
                        category = g.Key.ToString(),
                        checks = g.Select(r => new { message = r.Comment, severity = r.Judgement.ToString() }).ToList()
                    }).ToList();
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    species = strings.specieslist[pk.Species],
                    valid = la.Valid,
                    encounter = la.EncounterOriginal?.ToString() ?? "Unknown",
                    categories = grouped
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Suggest a legal moveset for a Pokemon based on its species and game version.")]
    public static string SuggestMoves(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0) return Error("Slot is empty");
                var strings = GameInfo.GetStrings("en");
                var suggestedMoves = MoveListSuggest.GetSuggestedCurrentMoves(pk);
                var result = suggestedMoves
                    .Where(m => m < strings.movelist.Length)
                    .Select(m => new { id = (int)m, name = strings.movelist[m] })
                    .ToList();
                return JsonSerializer.Serialize(new { success = true, species = strings.specieslist[pk.Species], suggested_moves = result });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Apply suggested legal moves to a Pokemon automatically.")]
    public static string ApplySuggestedMoves(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0) return Error("Slot is empty");
                var moves = MoveListSuggest.GetSuggestedCurrentMoves(pk);
                pk.SetMoves(moves);
                pk.SetMaximumPPCurrent();
                sav.SetBoxSlotAtIndex(pk, box, slot);
                var strings = GameInfo.GetStrings("en");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    applied_moves = moves.Where(m => m > 0).Select(m => strings.movelist[m]).ToList()
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Check if a Pokemon's nature, ability, and moves are legal for its species.")]
    public static string QuickLegalityCheck(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0) return Error("Slot is empty");
                var la = new LegalityAnalysis(pk);
                var strings = GameInfo.GetStrings("en");
                var issues = la.Results
                    .Where(r => r.Judgement == Severity.Invalid)
                    .Select(r => r.Comment)
                    .ToList();
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    species = strings.specieslist[pk.Species],
                    valid = la.Valid,
                    issue_count = issues.Count,
                    issues
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    [McpServerTool, Description("Get all valid ball choices for a Pokemon species (based on encounter legality).")]
    public static string GetValidBalls(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0) return Error("Slot is empty");
                var la = new LegalityAnalysis(pk);
                var enc = la.EncounterOriginal;
                var strings = GameInfo.GetStrings("en");
                // Return all balls that exist in the game
                var balls = Enum.GetValues<Ball>()
                    .Where(b => b != Ball.None && b != Ball.Undefined)
                    .Select(b => new { id = (int)b, name = b.ToString() })
                    .ToList();
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    species = strings.specieslist[pk.Species],
                    current_ball = (Ball)pk.Ball,
                    all_balls = balls
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    private static string Error(string msg) => JsonSerializer.Serialize(new { success = false, error = msg });
}
