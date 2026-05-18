"""MCP smoke harness: drives pkhex-mcp over stdio, exercises every tool against a real save."""
from __future__ import annotations
import base64
import json
import os
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DEFAULT_SAVE = REPO / "pokemon heartgold.sav"
SERVER_EXE = Path(os.environ.get("USERPROFILE", "")) / ".dotnet" / "tools" / "pkhex-mcp.exe"
DOTNET_ROOT = Path(os.environ.get("USERPROFILE", "")) / ".dotnet"


class MCPClient:
    def __init__(self, exe: Path):
        env = os.environ.copy()
        env["DOTNET_ROOT"] = str(DOTNET_ROOT)
        self.proc = subprocess.Popen(
            [str(exe)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, env=env, bufsize=0,
        )
        self.id = 0

    def _send(self, obj):
        self.proc.stdin.write((json.dumps(obj) + "\n").encode("utf-8"))
        self.proc.stdin.flush()

    def _recv(self, want_id):
        while True:
            line = self.proc.stdout.readline()
            if not line:
                raise RuntimeError("server closed")
            try:
                msg = json.loads(line.decode("utf-8"))
            except json.JSONDecodeError:
                continue
            if msg.get("id") == want_id:
                return msg

    def request(self, method, params=None):
        self.id += 1
        msg = {"jsonrpc": "2.0", "id": self.id, "method": method}
        if params is not None:
            msg["params"] = params
        self._send(msg)
        return self._recv(self.id)

    def notify(self, method, params=None):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        self._send(msg)

    def call_tool(self, name, args):
        resp = self.request("tools/call", {"name": name, "arguments": args})
        if "error" in resp:
            return {"_rpc_error": resp["error"]}
        content = resp.get("result", {}).get("content", [])
        if not content:
            return {"_empty": True}
        text = content[0].get("text", "")
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return {"_raw": text}

    def close(self):
        try: self.proc.stdin.close()
        except Exception: pass
        try: self.proc.wait(timeout=5)
        except Exception: self.proc.kill()


def main() -> int:
    save = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_SAVE
    if not save.exists():
        print(f"FAIL: save not found: {save}", file=sys.stderr)
        return 2
    data_b64 = base64.b64encode(save.read_bytes()).decode("ascii")
    if not SERVER_EXE.exists():
        print(f"FAIL: server exe missing: {SERVER_EXE}", file=sys.stderr)
        return 2

    client = MCPClient(SERVER_EXE)
    failures: list[tuple[str, str]] = []
    successes: list[str] = []

    try:
        client.request("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "smoke", "version": "1"},
        })
        client.notify("notifications/initialized")

        tools_resp = client.request("tools/list")
        tool_names = sorted(t["name"] for t in tools_resp.get("result", {}).get("tools", []))
        print(f"Discovered {len(tool_names)} tools")

        # Strict order — depend on load_save_file first.
        plan: list[tuple[str, dict]] = [
            ("load_save_file",       {"base64Data": data_b64}),
            ("get_trainer_info",     {}),
            ("get_save_metadata",    {}),
            ("get_box_names",        {}),
            ("count_pokemon",        {}),
            ("count_shinies",        {}),
            ("get_box",              {"box": 0}),
            ("get_all_boxes",        {}),
            ("get_box_pokemon",      {"box": 0, "slot": 0}),
            ("get_pokemon_stats",    {"box": 0, "slot": 0}),
            ("check_legality",       {"box": 0, "slot": 0}),
            ("quick_legality_check", {"box": 0, "slot": 0}),
            ("get_legality_details", {"box": 0, "slot": 0}),
            ("export_showdown",      {"box": 0, "slot": 0}),
            ("export_box_showdown",  {"box": 0}),
            ("export_all_showdown",  {}),
            ("search_pokemon",       {"query": "a"}),
            ("legality_report_all",  {}),
            ("legality_audit",       {}),
            ("get_all_species",      {}),
            ("get_all_moves",        {}),
            ("get_all_items",        {}),
            ("get_all_abilities",    {}),
            ("get_balls",            {}),
            ("get_natures",          {}),
            ("get_valid_balls",      {"box": 0, "slot": 0}),
            ("get_party",            {}),
            ("get_pokedex_stats",    {}),
            ("get_ribbons",          {"box": 0, "slot": 0}),
            ("get_learnable_moves",  {"species": "Pikachu", "gameVersion": "HG"}),
            # Read-only legality suggestions
            ("suggest_moves",        {"box": 0, "slot": 0}),
            ("apply_suggested_moves",{"box": 0, "slot": 0}),
            # Auto-fix (modifying)
            ("auto_fix_pokemon",     {"box": 0, "slot": 0, "allowNuclearFix": False}),
            ("mass_auto_fix",        {"allowNuclearFix": False}),
            # Single-slot mutations on a known-occupied slot we can later overwrite.
            ("set_level",            {"box": 0, "slot": 0, "level": 75}),
            ("set_nickname",         {"box": 0, "slot": 0, "nickname": "Smoke"}),
            ("set_nature",           {"box": 0, "slot": 0, "nature": "Adamant"}),
            ("set_shiny",            {"box": 0, "slot": 0, "shiny": True}),
            ("set_friendship",       {"box": 0, "slot": 0, "value": 200}),
            ("set_held_item",        {"box": 0, "slot": 0, "itemName": "Oran Berry"}),
            ("set_ot",               {"box": 0, "slot": 0, "otName": "RED", "otGender": 0}),
            ("set_i_vs",             {"box": 0, "slot": 0, "hp": 31, "atk": 31, "def": 31, "spa": 31, "spd": 31, "spe": 31}),
            ("set_e_vs",             {"box": 0, "slot": 0, "hp": 252, "atk": 0, "def": 0, "spa": 252, "spd": 4, "spe": 0}),
            ("max_i_vs",             {"box": 0, "slot": 0}),
            ("set_moves",            {"box": 0, "slot": 0, "move1": "Thunderbolt", "move2": "Quick Attack", "move3": "Iron Tail", "move4": ""}),
            ("heal_pokemon",         {"box": 0, "slot": 0}),
            # Slot-to-slot ops (use empty target slot way out)
            ("copy_pokemon",         {"fromBox": 0, "fromSlot": 0, "toBox": 17, "toSlot": 29}),
            ("rename_box",           {"box": 0, "name": "Smoke"}),
            # Use an empty slot for ImportShowdown
            ("import_showdown",      {"box": 17, "slot": 28, "showdownSet": "Pikachu @ Light Ball\nAbility: Static\nLevel: 50\n- Thunderbolt\n- Quick Attack\n- Iron Tail\n- Volt Tackle"}),
            ("heal_party",           {}),
            ("batch_edit_box",       {"box": 17, "instructions": ".CurrentLevel=50"}),
            ("batch_edit_all",       {"instructions": ".CurrentFriendship=100"}),
            ("box_to_party",         {"box": 17, "slot": 29, "partySlot": 5}),
            ("party_to_box",         {"partySlot": 5, "box": 17, "slot": 27}),
            ("delete_pokemon",       {"box": 17, "slot": 27}),
            ("set_trainer_name",     {"name": "SMOKE"}),
            ("get_save_base64",      {}),
        ]

        plan_names = {n for n, _ in plan}
        missing = [n for n in tool_names if n not in plan_names]
        if missing:
            print(f"  no plan for (skip): {missing}")

        for name, args in plan:
            if name not in tool_names:
                failures.append((name, "tool not registered"))
                print(f"  MISS {name}")
                continue
            t0 = time.time()
            try:
                result = client.call_tool(name, args)
            except Exception as e:
                failures.append((name, f"exception: {e}"))
                continue
            dt = (time.time() - t0) * 1000
            if isinstance(result, dict) and result.get("success") is True:
                successes.append(name)
                print(f"  OK   {name} ({dt:.0f}ms)")
            elif isinstance(result, dict) and result.get("success") is False:
                msg = result.get("error", "unknown")
                failures.append((name, f"success=false: {msg}"))
                print(f"  ERR  {name}: {msg}")
            else:
                failures.append((name, f"unexpected: {str(result)[:200]}"))
                print(f"  ???  {name}: {str(result)[:200]}")

        # Round-trip while client still alive: export, re-init separate server, reload, verify trainer.
        print()
        print("Round-trip verify...")
        export = client.call_tool("get_save_base64", {})
        out_b64 = export.get("base64") if isinstance(export, dict) else None
        if not out_b64:
            failures.append(("round_trip_export", "no base64 in result"))
        else:
            client2 = MCPClient(SERVER_EXE)
            try:
                client2.request("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "rt", "version": "1"}})
                client2.notify("notifications/initialized")
                reload = client2.call_tool("load_save_file", {"base64Data": out_b64})
                if not (isinstance(reload, dict) and reload.get("success")):
                    failures.append(("round_trip_reload", str(reload)[:200]))
                else:
                    trainer = client2.call_tool("get_trainer_info", {})
                    ot = (trainer or {}).get("trainer_name") or (trainer or {}).get("ot") or (trainer or {}).get("trainer")
                    if ot and "SMOKE" in str(ot).upper():
                        print(f"  OK   round-trip: trainer name persisted ({ot!r})")
                    else:
                        failures.append(("round_trip_verify", f"trainer info -> {trainer}"))
            finally:
                client2.close()
    finally:
        client.close()

    print()
    print(f"PASS {len(successes)} / FAIL {len(failures)}")
    for n, why in failures:
        print(f"  - {n}: {why}")
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
