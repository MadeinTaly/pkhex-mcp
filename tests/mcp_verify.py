"""Deeper verify: edit Pokemon, export, reload, confirm edits persisted exactly.

Differs from smoke test: smoke just checks success=true; verify checks values."""
from __future__ import annotations
import base64
import json
import os
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DEFAULT_SAVE = REPO / "pokemon heartgold.sav"
SERVER_EXE = Path(os.environ["USERPROFILE"]) / ".dotnet" / "tools" / "pkhex-mcp.exe"
DOTNET_ROOT = Path(os.environ["USERPROFILE"]) / ".dotnet"


class MCP:
    def __init__(self):
        env = os.environ.copy()
        env["DOTNET_ROOT"] = str(DOTNET_ROOT)
        self.p = subprocess.Popen(
            [str(SERVER_EXE)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, env=env, bufsize=0)
        self.id = 0
        self._req("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "v", "version": "1"}})
        self._notify("notifications/initialized")

    def _send(self, o):
        self.p.stdin.write((json.dumps(o) + "\n").encode()); self.p.stdin.flush()

    def _recv(self, want):
        while True:
            l = self.p.stdout.readline()
            if not l: raise RuntimeError("closed")
            try: m = json.loads(l)
            except Exception: continue
            if m.get("id") == want: return m

    def _req(self, method, params=None):
        self.id += 1
        self._send({"jsonrpc": "2.0", "id": self.id, "method": method, "params": params or {}})
        return self._recv(self.id)

    def _notify(self, method, params=None):
        m = {"jsonrpc": "2.0", "method": method}
        if params is not None: m["params"] = params
        self._send(m)

    def call(self, name, args):
        r = self._req("tools/call", {"name": name, "arguments": args})
        if "error" in r: return {"_rpc_error": r["error"]}
        c = r["result"]["content"][0]["text"]
        try: return json.loads(c)
        except Exception: return {"_raw": c}

    def close(self):
        try: self.p.stdin.close()
        except Exception: pass
        try: self.p.wait(timeout=5)
        except Exception: self.p.kill()


def check(label, got, want):
    ok = got == want
    print(f"  {'OK' if ok else 'FAIL'} {label}: got {got!r} want {want!r}")
    return ok


def main():
    if not DEFAULT_SAVE.exists():
        print(f"missing save: {DEFAULT_SAVE}", file=sys.stderr); return 2
    data_b64 = base64.b64encode(DEFAULT_SAVE.read_bytes()).decode()
    fails = 0

    m = MCP()
    try:
        m.call("load_save_file", {"base64Data": data_b64})

        # Find first occupied slot
        boxes = m.call("get_all_boxes", {})
        if not boxes.get("success"):
            print("get_all_boxes failed"); return 1
        if not boxes.get("pokemon"):
            print("no pokemon in save"); return 1
        first = boxes["pokemon"][0]
        b, s = first["box"], first["slot"]
        original_species = first["species"]
        print(f"Editing {original_species} at box {b} slot {s}")

        edits = [
            ("set_level",      {"box": b, "slot": s, "level": 88}),
            ("set_nickname",   {"box": b, "slot": s, "nickname": "Verifier"}),
            ("set_friendship", {"box": b, "slot": s, "value": 200}),
            ("set_i_vs",       {"box": b, "slot": s, "hp": 30, "atk": 29, "def": 28, "spa": 27, "spd": 26, "spe": 25}),
            ("set_e_vs",       {"box": b, "slot": s, "hp": 252, "atk": 0, "def": 0, "spa": 252, "spd": 4, "spe": 0}),
            # set_nature edits PID in Gen<8, so it MUST run before set_shiny (which also touches PID).
            ("set_nature",     {"box": b, "slot": s, "nature": "Timid"}),
            ("set_shiny",      {"box": b, "slot": s, "shiny": True}),
            ("set_ot",         {"box": b, "slot": s, "otName": "VERIFY", "otGender": 0}),
            ("set_trainer_name", {"name": "TESTER"}),
        ]
        for name, args in edits:
            r = m.call(name, args)
            if not r.get("success"):
                print(f"  edit {name} failed: {r}"); fails += 1

        # Export & reload in a fresh server
        exp = m.call("get_save_base64", {})
        out_b64 = exp.get("base64")
        if not out_b64:
            print("export failed"); return 1
    finally:
        m.close()

    m2 = MCP()
    try:
        m2.call("load_save_file", {"base64Data": out_b64})
        trainer = m2.call("get_trainer_info", {})
        if not check("trainer_name", trainer.get("trainer_name"), "TESTER"): fails += 1

        pk = m2.call("get_box_pokemon", {"box": b, "slot": s})
        if not check("level",     pk.get("level"),     88):          fails += 1
        if not check("nickname",  pk.get("nickname"),  "Verifier"):  fails += 1
        if not check("shiny",     pk.get("shiny"),     True):        fails += 1
        if not check("nature",    pk.get("nature"),    "Timid"):     fails += 1
        if not check("ot",        pk.get("ot"),        "VERIFY"):    fails += 1
        ivs = pk.get("ivs", {})
        if not check("ivs",       (ivs.get("hp"), ivs.get("atk"), ivs.get("def"), ivs.get("spa"), ivs.get("spd"), ivs.get("spe")),
                                  (30, 29, 28, 27, 26, 25)):           fails += 1
        evs = pk.get("evs", {})
        if not check("evs",       (evs.get("hp"), evs.get("atk"), evs.get("def"), evs.get("spa"), evs.get("spd"), evs.get("spe")),
                                  (252, 0, 0, 252, 4, 0)):             fails += 1

        # Friendship is exposed via get_pokemon_stats
        stats = m2.call("get_pokemon_stats", {"box": b, "slot": s})
        if not check("friendship", stats.get("friendship"), 200):    fails += 1
    finally:
        m2.close()

    print(f"\nVerify result: {'PASS' if fails == 0 else f'FAIL ({fails})'}")
    return 0 if fails == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
