using ModelContextProtocol.Server;
using PKHeX.Core;
using PKHeXMCP;
using System.ComponentModel;
using System.Text.Json;

namespace PKHeXMCP.Tools;

[McpServerToolType]
public static class PokemonEditTools
{
    [McpServerTool, Description("Set IVs (Individual Values) for a Pokemon. Each IV must be 0-31.")]
    public static string SetIVs(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("HP IV (0-31)")] int hp, [Description("Attack IV (0-31)")] int atk,
        [Description("Defense IV (0-31)")] int def, [Description("Sp.Atk IV (0-31)")] int spa,
        [Description("Sp.Def IV (0-31)")] int spd, [Description("Speed IV (0-31)")] int spe)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.IV_HP = hp; pk.IV_ATK = atk; pk.IV_DEF = def;
                pk.IV_SPA = spa; pk.IV_SPD = spd; pk.IV_SPE = spe;
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Set EVs (Effort Values) for a Pokemon. Total must not exceed 510, each max 252.")]
    public static string SetEVs(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("HP EV")] int hp, [Description("Attack EV")] int atk,
        [Description("Defense EV")] int def, [Description("Sp.Atk EV")] int spa,
        [Description("Sp.Def EV")] int spd, [Description("Speed EV")] int spe)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.EV_HP = hp; pk.EV_ATK = atk; pk.EV_DEF = def;
                pk.EV_SPA = spa; pk.EV_SPD = spd; pk.EV_SPE = spe;
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Set the nature of a Pokemon.")]
    public static string SetNature(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("Nature name (e.g. Adamant, Modest, Timid)")] string nature)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                if (!Enum.TryParse<Nature>(nature, true, out var n)) return Error($"Unknown nature: {nature}");
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.Nature = n;
                pk.StatNature = n;
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Set the level of a Pokemon (1-100).")]
    public static string SetLevel(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("Level (1-100)")] int level)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.CurrentLevel = (byte)level;
                pk.EXP = Experience.GetEXP((byte)level, pk.PersonalInfo.EXPGrowth);
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Set the nickname of a Pokemon. Pass empty string to clear nickname.")]
    public static string SetNickname(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("Nickname (empty to clear)")] string nickname)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (string.IsNullOrEmpty(nickname))
                {
                    pk.IsNicknamed = false;
                    pk.Nickname = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, pk.Format);
                }
                else
                {
                    pk.IsNicknamed = true;
                    pk.Nickname = nickname;
                }
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Make a Pokemon shiny or non-shiny.")]
    public static string SetShiny(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("True to make shiny, false to remove shiny")] bool shiny)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (shiny) pk.SetShiny();
                else pk.PID = EntityPID.GetRandomPID(Util.Rand, pk.Species, pk.Gender, pk.Version, pk.Nature, pk.Form, pk.PID);
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Set the moves of a Pokemon by move name.")]
    public static string SetMoves(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("Move 1 name")] string move1,
        [Description("Move 2 name (empty to leave empty)")] string move2 = "",
        [Description("Move 3 name (empty to leave empty)")] string move3 = "",
        [Description("Move 4 name (empty to leave empty)")] string move4 = "")
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var strings = GameInfo.GetStrings("en");
                int FindMove(string name)
                {
                    if (string.IsNullOrEmpty(name)) return 0;
                    for (int i = 0; i < strings.movelist.Length; i++)
                        if (strings.movelist[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
                    return -1;
                }
                int m1 = FindMove(move1), m2 = FindMove(move2), m3 = FindMove(move3), m4 = FindMove(move4);
                if (m1 < 0) return Error($"Move not found: {move1}");
                if (m2 < 0) return Error($"Move not found: {move2}");
                if (m3 < 0) return Error($"Move not found: {move3}");
                if (m4 < 0) return Error($"Move not found: {move4}");
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.Move1 = (ushort)m1; pk.Move2 = (ushort)m2; pk.Move3 = (ushort)m3; pk.Move4 = (ushort)m4;
                // Restore PP to max for each move
                pk.Move1_PP = pk.GetMovePP((ushort)m1, pk.Move1_PPUps);
                pk.Move2_PP = pk.GetMovePP((ushort)m2, pk.Move2_PPUps);
                pk.Move3_PP = pk.GetMovePP((ushort)m3, pk.Move3_PPUps);
                pk.Move4_PP = pk.GetMovePP((ushort)m4, pk.Move4_PPUps);
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Set the held item of a Pokemon by item name.")]
    public static string SetHeldItem(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("Item name (empty to remove held item)")] string itemName)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var strings = GameInfo.GetStrings("en");
                int itemId = 0;
                if (!string.IsNullOrEmpty(itemName))
                {
                    for (int i = 0; i < strings.itemlist.Length; i++)
                        if (strings.itemlist[i].Equals(itemName, StringComparison.OrdinalIgnoreCase)) { itemId = i; break; }
                    if (itemId == 0) return Error($"Item not found: {itemName}");
                }
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.HeldItem = itemId;
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Set all IVs to 31 (perfect) for a Pokemon.")]
    public static string MaxIVs(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.IV_HP = pk.IV_ATK = pk.IV_DEF = pk.IV_SPA = pk.IV_SPD = pk.IV_SPE = 31;
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Heal a Pokemon to full HP and restore all PP.")]
    public static string HealPokemon(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.Stat_HPCurrent = pk.Stat_HPMax;
                // Restore all PP to max
                pk.Move1_PP = pk.GetMovePP(pk.Move1, pk.Move1_PPUps);
                pk.Move2_PP = pk.GetMovePP(pk.Move2, pk.Move2_PPUps);
                pk.Move3_PP = pk.GetMovePP(pk.Move3, pk.Move3_PPUps);
                pk.Move4_PP = pk.GetMovePP(pk.Move4, pk.Move4_PPUps);
                pk.Status_Condition = 0;
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Copy a Pokemon from one box/slot to another.")]
    public static string CopyPokemon(SaveContext ctx,
        [Description("Source box (0-based)")] int fromBox, [Description("Source slot (0-based)")] int fromSlot,
        [Description("Destination box (0-based)")] int toBox, [Description("Destination slot (0-based)")] int toSlot)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(fromBox, fromSlot);
                sav.SetBoxSlotAtIndex(pk, toBox, toSlot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Delete (clear) a Pokemon from a box slot.")]
    public static string DeletePokemon(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var blank = sav.BlankPKM;
                sav.SetBoxSlotAtIndex(blank, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Set the OT (Original Trainer) name of a Pokemon.")]
    public static string SetOT(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("OT name")] string otName,
        [Description("OT gender (0=Male, 1=Female)")] int otGender = 0)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.OriginalTrainerName = otName;
                pk.OriginalTrainerGender = (byte)otGender;
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Set the friendship/happiness value of a Pokemon (0-255).")]
    public static string SetFriendship(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot,
        [Description("Friendship value (0-255)")] int value)
    {
        return ctx.WithWrite(sav =>
        {
            if (sav is null) return Error("No save file loaded");
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                pk.CurrentFriendship = (byte)value;
                sav.SetBoxSlotAtIndex(pk, box, slot);
                return Ok();
            }
            catch (Exception ex) { return Error(ex.Message); }
        });
    }

    [McpServerTool, Description("Get detailed stats of a Pokemon including calculated battle stats.")]
    public static string GetPokemonStats(SaveContext ctx,
        [Description("Box index (0-based)")] int box, [Description("Slot index (0-based)")] int slot)
    {
        return ctx.WithRead(sav =>
        {
            try
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0) return JsonSerializer.Serialize(new { success = true, empty = true });
                var strings = GameInfo.GetStrings("en");
                pk.ResetPartyStats();
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    species = strings.specieslist[pk.Species],
                    nickname = pk.Nickname,
                    level = pk.CurrentLevel,
                    nature = pk.Nature.ToString(),
                    ability = strings.abilitylist[pk.Ability],
                    held_item = pk.HeldItem > 0 ? strings.itemlist[pk.HeldItem] : "None",
                    shiny = pk.IsShiny,
                    friendship = pk.CurrentFriendship,
                    gender = pk.Gender == 0 ? "M" : pk.Gender == 1 ? "F" : "N",
                    ot = pk.OriginalTrainerName,
                    ball = (Ball)pk.Ball,
                    language = pk.Language,
                    exp = pk.EXP,
                    moves = new[]
                    {
                        new { name = strings.movelist[pk.Move1], pp = pk.Move1_PP, max_pp = pk.Move1_PPUps },
                        new { name = strings.movelist[pk.Move2], pp = pk.Move2_PP, max_pp = pk.Move2_PPUps },
                        new { name = strings.movelist[pk.Move3], pp = pk.Move3_PP, max_pp = pk.Move3_PPUps },
                        new { name = strings.movelist[pk.Move4], pp = pk.Move4_PP, max_pp = pk.Move4_PPUps },
                    },
                    ivs = new { hp = pk.IV_HP, atk = pk.IV_ATK, def = pk.IV_DEF, spa = pk.IV_SPA, spd = pk.IV_SPD, spe = pk.IV_SPE },
                    evs = new { hp = pk.EV_HP, atk = pk.EV_ATK, def = pk.EV_DEF, spa = pk.EV_SPA, spd = pk.EV_SPD, spe = pk.EV_SPE },
                    stats = new { hp = pk.Stat_HPCurrent, atk = pk.Stat_ATK, def = pk.Stat_DEF, spa = pk.Stat_SPA, spd = pk.Stat_SPD, spe = pk.Stat_SPE },
                });
            }
            catch (Exception ex) { return Error(ex.Message); }
        }) ?? Error("No save file loaded");
    }

    private static string Ok(object? extra = null) =>
        JsonSerializer.Serialize(new { success = true });

    private static string Error(string msg) =>
        JsonSerializer.Serialize(new { success = false, error = msg });
}
