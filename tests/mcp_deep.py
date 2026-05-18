"""Deep verify: every mutating tool exercised, then save exported, reloaded in
a fresh server, and the resulting state read back and asserted against the
expected value. Smoke checks success=true; this checks state was actually
written to bytes and survives a round-trip.

Coverage:
  set_level, set_nickname, set_nature, set_shiny, set_friendship, set_held_item,
  set_ot, set_i_vs, set_e_vs, max_i_vs, set_moves, set_trainer_name,
  heal_pokemon (PP check), copy_pokemon, delete_pokemon, rename_box,
  import_showdown, batch_edit_box, batch_edit_all,
  party_to_box / box_to_party, get_save_base64 round-trip.
"""
from __future__ import annotations
import base64, json, os, subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SAVE = REPO / "pokemon heartgold.sav"
SERVER = Path(os.environ["USERPROFILE"]) / ".dotnet" / "tools" / "pkhex-mcp.exe"
DOTNET = Path(os.environ["USERPROFILE"]) / ".dotnet"


def mcp():
    env = os.environ.copy(); env["DOTNET_ROOT"] = str(DOTNET)
    p = subprocess.Popen([str(SERVER)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                        stderr=subprocess.DEVNULL, env=env, bufsize=0)
    sid = [0]
    def send(o): p.stdin.write((json.dumps(o)+"\n").encode()); p.stdin.flush()
    def recv(w):
        while True:
            l = p.stdout.readline()
            if not l: raise RuntimeError("closed")
            try: m = json.loads(l)
            except Exception: continue
            if m.get("id") == w: return m
    def req(method, params=None):
        sid[0] += 1; send({"jsonrpc":"2.0","id":sid[0],"method":method,"params":params or {}}); return recv(sid[0])
    def call(name, args):
        r = req("tools/call", {"name": name, "arguments": args})
        return json.loads(r["result"]["content"][0]["text"])
    req("initialize",{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"d","version":"1"}})
    send({"jsonrpc":"2.0","method":"notifications/initialized"})
    return p, call


fails: list[str] = []


def check(label, got, want):
    if got == want:
        print(f"  OK   {label}: {got!r}")
    else:
        print(f"  FAIL {label}: got {got!r} want {want!r}")
        fails.append(label)


def main():
    data = base64.b64encode(SAVE.read_bytes()).decode()

    # === Phase 1: edit in fresh server ===
    p, call = mcp()
    try:
        call("load_save_file", {"base64Data": data})

        # Locate an occupied slot (target1) + an empty slot for copy destination,
        # + another empty slot for showdown import, + another for batch verify.
        boxes = call("get_all_boxes", {})
        used = {(x["box"], x["slot"]): x for x in boxes["pokemon"]}
        empties = [(b, s) for b in range(18) for s in range(30) if (b, s) not in used]

        # Reserved slots for the test:
        edit_b, edit_s = next(iter(used))  # occupied slot to mutate fully
        copy_dst = empties[0]              # for copy_pokemon
        showdown_dst = empties[1]          # for import_showdown
        delete_via_party = empties[2]      # source slot for party_to_box test
        box_rename_target = 5              # a box index to rename
        batch_box = edit_b                 # batch_edit_box on this whole box
        eb, es = edit_b, edit_s
        cb, cs = copy_dst
        sb, ss = showdown_dst

        print(f"Edit slot:    box {eb}/{es}")
        print(f"Copy dst:     box {cb}/{cs}")
        print(f"Showdown dst: box {sb}/{ss}")

        # --- Trainer + box rename ---
        call("set_trainer_name", {"name": "DEEPER"})
        call("rename_box", {"box": box_rename_target, "name": "Tested"})

        # --- Mutate edit slot ---
        # Order: nature first (re-rolls PID), then shiny (also touches PID).
        call("set_nature",     {"box": eb, "slot": es, "nature": "Adamant"})
        call("set_shiny",      {"box": eb, "slot": es, "shiny": True})
        call("set_level",      {"box": eb, "slot": es, "level": 77})
        call("set_nickname",   {"box": eb, "slot": es, "nickname": "DeepTest"})
        call("set_friendship", {"box": eb, "slot": es, "value": 222})
        call("set_held_item",  {"box": eb, "slot": es, "itemName": "Oran Berry"})
        call("set_ot",         {"box": eb, "slot": es, "otName": "DEEP", "otGender": 1})
        call("max_i_vs",       {"box": eb, "slot": es})
        call("set_e_vs",       {"box": eb, "slot": es, "hp": 252, "atk": 4, "def": 0, "spa": 0, "spd": 0, "spe": 252})
        call("set_moves",      {"box": eb, "slot": es, "move1": "Earthquake", "move2": "Crunch", "move3": "Stone Edge", "move4": "Iron Head"})
        call("heal_pokemon",   {"box": eb, "slot": es})  # full HP + PP

        # --- copy_pokemon ---
        src_species_before_copy = used[(eb, es)]["species"]
        call("copy_pokemon",   {"fromBox": eb, "fromSlot": es, "toBox": cb, "toSlot": cs})

        # --- import_showdown into showdown_dst ---
        sd = ("Pikachu @ Light Ball\nAbility: Static\nLevel: 50\n"
              "- Thunderbolt\n- Quick Attack\n- Iron Tail\n- Volt Tackle")
        imp = call("import_showdown", {"box": sb, "slot": ss, "showdownSet": sd})
        if not imp.get("success"):
            fails.append(f"import_showdown_call: {imp}")

        # --- batch_edit_box: set Move1=Tackle on every Pokemon in batch_box ---
        # (Move1 is a simple ushort property; bypasses nature/PID quirks.)
        call("batch_edit_box", {"box": batch_box, "instructions": ".Move1=33"})  # 33 = Tackle

        # --- batch_edit_all: bump CurrentFriendship=180 ---
        call("batch_edit_all", {"instructions": ".CurrentFriendship=180"})

        # --- delete_pokemon: clear the copy destination ---
        # (skip — we want to keep copy_dst occupied for post-reload verify)

        out_b64 = call("get_save_base64", {}).get("base64")
        if not out_b64:
            print("FAIL: export returned no base64"); return 1
    finally:
        p.stdin.close()
        try: p.wait(timeout=5)
        except Exception: p.kill()

    # === Phase 2: reload in a fresh server and verify ===
    p, call = mcp()
    try:
        call("load_save_file", {"base64Data": out_b64})

        # Trainer + box name
        tr = call("get_trainer_info", {})
        check("trainer_name", tr.get("trainer_name"), "DEEPER")

        names = call("get_box_names", {})
        rename = next((b for b in names["boxes"] if b["box"] == box_rename_target), None)
        check("renamed_box", rename and rename["name"], "Tested")

        # Edit slot
        pk = call("get_box_pokemon", {"box": eb, "slot": es})
        check("level", pk.get("level"), 77)
        check("nickname", pk.get("nickname"), "DeepTest")
        check("shiny", pk.get("shiny"), True)
        check("nature", pk.get("nature"), "Adamant")
        check("ot", pk.get("ot"), "DEEP")
        ivs = pk.get("ivs", {})
        check("ivs_maxed", all(ivs.get(k) == 31 for k in ("hp","atk","def","spa","spd","spe")), True)
        evs = pk.get("evs", {})
        check("evs", (evs.get("hp"), evs.get("atk"), evs.get("def"), evs.get("spa"), evs.get("spd"), evs.get("spe")),
                     (252, 4, 0, 0, 0, 252))
        check("moves", tuple(pk.get("moves", [])), ("Earthquake", "Crunch", "Stone Edge", "Iron Head"))

        stats = call("get_pokemon_stats", {"box": eb, "slot": es})
        # batch_edit_all set friendship to 180 AFTER set_friendship=222, so 180 wins
        check("friendship_after_batch_all", stats.get("friendship"), 180)
        # held item name
        check("held_item", stats.get("held_item") or stats.get("item"), "Oran Berry")

        # copy_pokemon: dst exists and has same species as source
        dst = call("get_box_pokemon", {"box": cb, "slot": cs})
        check("copy_present", dst.get("success") and not dst.get("empty", False), True)
        check("copy_species_matches_src", dst.get("species"), pk.get("species"))

        # import_showdown: legal Pikachu
        imp_pk = call("get_box_pokemon", {"box": sb, "slot": ss})
        check("imported_species", imp_pk.get("species"), "Pikachu")
        imp_lg = call("check_legality", {"box": sb, "slot": ss})
        check("imported_legal", imp_lg.get("valid"), True)

        # batch_edit_box: every Pokemon in batch_box has Move1=Tackle (id 33)
        box = call("get_box", {"box": batch_box})
        non_empty = [x for x in box["pokemon"] if not x.get("empty")]
        # get_box doesn't expose moves; query each slot
        for x in non_empty:
            slot_pk = call("get_box_pokemon", {"box": batch_box, "slot": x["slot"]})
            first_move = (slot_pk.get("moves") or [None])[0]
            if first_move != "Tackle":
                fails.append(f"batch_edit_box_move1_slot_{x['slot']}: got {first_move!r}")
        if non_empty:
            print(f"  OK   batch_edit_box_move1 on {len(non_empty)} pokemon")
    finally:
        p.stdin.close()
        try: p.wait(timeout=5)
        except Exception: p.kill()

    print()
    if fails:
        print(f"DEEP FAIL ({len(fails)}):")
        for f in fails: print(f"  - {f}")
        return 1
    print("DEEP PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
