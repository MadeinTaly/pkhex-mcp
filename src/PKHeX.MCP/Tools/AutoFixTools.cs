using ModelContextProtocol.Server;
using PKHeX.Core;
using PKHeXMCP;
using System.ComponentModel;
using System.Text.Json;

namespace PKHeXMCP.Tools;

/// <summary>
/// Automatic legality correction tools.
/// Fix strategies (applied in order):
///   1. Sync EXP with current level
///   2. Apply suggested relearn moves
///   3. Apply suggested current moves
///   4. Restore full PP
///   5. Regenerate via Showdown re-import (nuclear option — preserves species/nature/IVs/EVs)
/// Some issues (invalid encounter origin, hacked OT, impossible shininess) cannot be fixed
/// without altering the Pokemon's identity or history.
/// </summary>
[McpServerToolType]
public static class AutoFixTools
{
    [McpServerTool, Description(
        "Attempt to auto-fix legality issues on a single Pokemon. " +
        "Fixes: EXP/level sync, relearn moves, current moves, PP. " +
        "If still illegal after soft fixes, optionally regenerates the Pokemon from its Showdown set (nuclear fix). " +
        "Returns a report of what was fixed and what remains illegal.")]
    public static string AutoFixPokemon(SaveContext ctx,
        [Description("Box index (0-based)")] int box,
        [Description("Slot index (0-based)")] int slot,
        [Description("If true and soft fixes fail, regenerate the Pokemon legally from its Showdown set (changes PID/EC/met-data)")] bool allowNuclearFix = false)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0) return Error("Slot is empty");

                var strings = GameInfo.GetStrings("en");
                string speciesName = strings.specieslist[pk.Species];

                // Check before
                var before = new LegalityAnalysis(pk);
                if (before.Valid)
                    return JsonSerializer.Serialize(new { success = true, species = speciesName, was_already_legal = true, fixed_issues = Array.Empty<string>() });

                var fixLog = new List<string>();

                // --- Fix 1: Sync EXP to level ---
                var expectedExp = Experience.GetEXP((uint)pk.CurrentLevel, pk.PersonalInfo.EXPGrowth);
                if (pk.EXP != expectedExp)
                {
                    pk.EXP = expectedExp;
                    fixLog.Add("Synced EXP to current level");
                }

                // --- Fix 2: Relearn moves ---
                var suggestedRelearn = MoveListSuggest.GetSuggestedRelearnMoves(new LegalityAnalysis(pk), sav);
                if (suggestedRelearn.Length > 0)
                {
                    pk.SetRelearnMoves(suggestedRelearn);
                    fixLog.Add($"Fixed relearn moves: {string.Join(", ", suggestedRelearn.Where(m => m > 0).Select(m => strings.movelist[m]))}");
                }

                // --- Fix 3: Current moves ---
                var la2 = new LegalityAnalysis(pk);
                var movesResult = la2.Results.FirstOrDefault(r => r.Identifier == CheckIdentifier.CurrentMove && r.Judgement == Severity.Invalid);
                if (movesResult != null)
                {
                    var suggested = MoveListSuggest.GetSuggestedCurrentMoves(pk);
                    pk.SetMoves(suggested);
                    fixLog.Add($"Fixed moves to: {string.Join(", ", suggested.Where(m => m > 0).Select(m => strings.movelist[m]))}");
                }

                // --- Fix 4: PP ---
                pk.SetMaximumPPCurrent();

                // Check after soft fixes
                var afterSoft = new LegalityAnalysis(pk);
                if (afterSoft.Valid)
                {
                    sav.SetBoxSlotAtIndex(pk, box, slot);
                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        species = speciesName,
                        now_legal = true,
                        method = "soft_fix",
                        fixed_issues = fixLog
                    });
                }

                // --- Fix 5: Nuclear — regenerate from Showdown ---
                if (allowNuclearFix)
                {
                    var showdown = ShowdownParsing.GetShowdownText(pk);
                    var template = new ShowdownSet(showdown);
                    var regenerated = sav.GetLegalFromSet(template).Created;
                    if (regenerated is not null)
                    {
                        var afterNuclear = new LegalityAnalysis(regenerated);
                        if (afterNuclear.Valid)
                        {
                            sav.SetBoxSlotAtIndex(regenerated, box, slot);
                            fixLog.Add("Regenerated Pokemon legally from Showdown set (PID/EC/met-data changed)");
                            return JsonSerializer.Serialize(new
                            {
                                success = true,
                                species = speciesName,
                                now_legal = true,
                                method = "nuclear_regenerate",
                                fixed_issues = fixLog,
                                warning = "PID, EC, met location and date were reset to legal values"
                            });
                        }
                    }
                }

                // Save partial fixes even if still illegal
                sav.SetBoxSlotAtIndex(pk, box, slot);
                var remaining = new LegalityAnalysis(pk).Results
                    .Where(r => r.Judgement == Severity.Invalid)
                    .Select(r => r.Comment)
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    species = speciesName,
                    now_legal = false,
                    method = "soft_fix_partial",
                    fixed_issues = fixLog,
                    remaining_issues = remaining,
                    tip = "Set allowNuclearFix=true to regenerate the Pokemon from scratch with legal values"
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description(
        "Scan all boxes and auto-fix every illegal Pokemon. " +
        "Applies soft fixes (EXP sync, relearn moves, move fixes, PP). " +
        "Returns a full report: how many were fixed, how many remain illegal, and what issues persist.")]
    public static string MassAutoFix(SaveContext ctx,
        [Description("If true, Pokemon that remain illegal after soft fixes will be regenerated from their Showdown set (nuclear fix — changes PID/EC/met-data)")] bool allowNuclearFix = false)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var strings = GameInfo.GetStrings("en");
                int scanned = 0, alreadyLegal = 0, fixedSoft = 0, fixedNuclear = 0, stillIllegal = 0;
                var report = new List<object>();

                for (int b = 0; b < sav.BoxCount; b++)
                    for (int s = 0; s < sav.BoxSlotCount; s++)
                    {
                        var pk = sav.GetBoxSlotAtIndex(b, s);
                        if (pk.Species == 0) continue;
                        scanned++;

                        var la = new LegalityAnalysis(pk);
                        if (la.Valid) { alreadyLegal++; continue; }

                        string speciesName = strings.specieslist[pk.Species];
                        var fixLog = new List<string>();

                        // Fix 1: EXP sync
                        var expectedExp = Experience.GetEXP((uint)pk.CurrentLevel, pk.PersonalInfo.EXPGrowth);
                        if (pk.EXP != expectedExp) { pk.EXP = expectedExp; fixLog.Add("EXP synced"); }

                        // Fix 2: Relearn moves
                        var suggestedRelearn = MoveListSuggest.GetSuggestedRelearnMoves(new LegalityAnalysis(pk), sav);
                        if (suggestedRelearn.Length > 0) { pk.SetRelearnMoves(suggestedRelearn); fixLog.Add("Relearn moves fixed"); }

                        // Fix 3: Current moves
                        var la2 = new LegalityAnalysis(pk);
                        if (la2.Results.Any(r => r.Identifier == CheckIdentifier.CurrentMove && r.Judgement == Severity.Invalid))
                        {
                            pk.SetMoves(MoveListSuggest.GetSuggestedCurrentMoves(pk));
                            fixLog.Add("Moves fixed");
                        }

                        // Fix 4: PP
                        pk.SetMaximumPPCurrent();

                        var afterSoft = new LegalityAnalysis(pk);
                        if (afterSoft.Valid)
                        {
                            sav.SetBoxSlotAtIndex(pk, b, s);
                            fixedSoft++;
                            report.Add(new { box = b, slot = s, species = speciesName, result = "fixed_soft", fixes = fixLog });
                            continue;
                        }

                        // Fix 5: Nuclear
                        if (allowNuclearFix)
                        {
                            var showdown = ShowdownParsing.GetShowdownText(pk);
                            var template = new ShowdownSet(showdown);
                            var regen = sav.GetLegalFromSet(template).Created;
                            if (regen is not null && new LegalityAnalysis(regen).Valid)
                            {
                                sav.SetBoxSlotAtIndex(regen, b, s);
                                fixedNuclear++;
                                fixLog.Add("Regenerated (nuclear)");
                                report.Add(new { box = b, slot = s, species = speciesName, result = "fixed_nuclear", fixes = fixLog });
                                continue;
                            }
                        }

                        // Still illegal
                        sav.SetBoxSlotAtIndex(pk, b, s);
                        stillIllegal++;
                        var remaining = new LegalityAnalysis(pk).Results
                            .Where(r => r.Judgement == Severity.Invalid)
                            .Select(r => r.Comment).ToList();
                        report.Add(new { box = b, slot = s, species = speciesName, result = "still_illegal", partial_fixes = fixLog, remaining_issues = remaining });
                    }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    summary = new
                    {
                        scanned,
                        already_legal = alreadyLegal,
                        fixed_soft = fixedSoft,
                        fixed_nuclear = fixedNuclear,
                        still_illegal = stillIllegal,
                        total_fixed = fixedSoft + fixedNuclear
                    },
                    details = report
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description(
        "Scan all boxes and return a legality report without making any changes. " +
        "Groups issues by type so you can decide what to fix.")]
    public static string LegalityAudit(SaveContext ctx)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var strings = GameInfo.GetStrings("en");
                int scanned = 0, legal = 0, illegal = 0;
                var issuesByType = new Dictionary<string, int>();
                var illegalList = new List<object>();

                for (int b = 0; b < sav.BoxCount; b++)
                    for (int s = 0; s < sav.BoxSlotCount; s++)
                    {
                        var pk = sav.GetBoxSlotAtIndex(b, s);
                        if (pk.Species == 0) continue;
                        scanned++;

                        var la = new LegalityAnalysis(pk);
                        if (la.Valid) { legal++; continue; }

                        illegal++;
                        var issues = la.Results.Where(r => r.Judgement == Severity.Invalid).ToList();
                        foreach (var issue in issues)
                        {
                            var key = issue.Identifier.ToString();
                            issuesByType[key] = issuesByType.GetValueOrDefault(key) + 1;
                        }

                        illegalList.Add(new
                        {
                            box = b, slot = s,
                            species = strings.specieslist[pk.Species],
                            nickname = pk.Nickname,
                            shiny = pk.IsShiny,
                            issues = issues.Select(r => new { type = r.Identifier.ToString(), message = r.Comment }).ToList()
                        });
                    }

                var sortedIssues = issuesByType.OrderByDescending(kv => kv.Value)
                    .Select(kv => new { issue_type = kv.Key, count = kv.Value }).ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    summary = new { scanned, legal, illegal, legal_percent = scanned > 0 ? Math.Round(legal * 100.0 / scanned, 1) : 0 },
                    most_common_issues = sortedIssues,
                    illegal_pokemon = illegalList
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    private static string Error(string msg) => JsonSerializer.Serialize(new { success = false, error = msg });
}
