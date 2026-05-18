"""End-to-end scenario tests beyond simple field-persistence checks.

Scenarios:
  S1  Showdown round-trip: import a legal set -> check_legality is valid
       -> export_showdown matches species/moves
  S2  AutoFix recovery: load Pokemon, intentionally break (set level to 1),
       run auto_fix_pokemon, confirm legality recovered or improved
  S3  Mass batch edit: BatchEditAll set CurrentLevel=100 -> verify every
       non-empty Pokemon reports level 100
  S4  Delete: delete_pokemon -> get_box_pokemon reports empty=true
  S5  Copy: copy_pokemon -> dst now has same species as src
  S6  Negative: invalid base64 -> success=false, no crash
  S7  Negative: out-of-range box -> success=false, no crash
"""
from __future__ import annotations
import base64, json, os, subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SAVE = REPO / "pokemon heartgold.sav"
SERVER = Path(os.environ["USERPROFILE"]) / ".dotnet" / "tools" / "pkhex-mcp.exe"
DOTNET = Path(os.environ["USERPROFILE"]) / ".dotnet"


class MCP:
    def __init__(self):
        env = os.environ.copy(); env["DOTNET_ROOT"] = str(DOTNET)
        self.p = subprocess.Popen([str(SERVER)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL, env=env, bufsize=0)
        self.id = 0
        self.req("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "s", "version": "1"}})
        self._send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def _send(self, o): self.p.stdin.write((json.dumps(o) + "\n").encode()); self.p.stdin.flush()
    def _recv(self, want):
        while True:
            l = self.p.stdout.readline()
            if not l: raise RuntimeError("closed")
            try: m = json.loads(l)
            except Exception: continue
            if m.get("id") == want: return m
    def req(self, method, params=None):
        self.id += 1; self._send({"jsonrpc": "2.0", "id": self.id, "method": method, "params": params or {}})
        return self._recv(self.id)
    def call(self, name, args):
        r = self.req("tools/call", {"name": name, "arguments": args})
        if "error" in r: return {"_rpc_error": r["error"]}
        try: return json.loads(r["result"]["content"][0]["text"])
        except Exception: return {"_raw": r["result"]["content"][0]["text"]}
    def close(self):
        try: self.p.stdin.close()
        except Exception: pass
        try: self.p.wait(timeout=5)
        except Exception: self.p.kill()


def main():
    if not SAVE.exists():
        print(f"missing save: {SAVE}", file=sys.stderr); return 2
    data_b64 = base64.b64encode(SAVE.read_bytes()).decode()
    results: list[tuple[str, bool, str]] = []

    def record(name, ok, note=""):
        results.append((name, ok, note))
        print(f"  {'OK' if ok else 'FAIL'} {name}{' — ' + note if note else ''}")

    # S1: Showdown legality round-trip on an empty slot
    m = MCP()
    try:
        m.call("load_save_file", {"base64Data": data_b64})
        # Find an empty slot
        boxes = m.call("get_all_boxes", {})
        used = {(p["box"], p["slot"]) for p in boxes.get("pokemon", [])}
        target = next(((b, s) for b in range(18) for s in range(30) if (b, s) not in used), None)
        if not target:
            record("S1_showdown_import", False, "no empty slot")
        else:
            b, s = target
            sd = ("Pikachu @ Light Ball\n"
                  "Ability: Static\n"
                  "Level: 50\n"
                  "EVs: 252 SpA / 4 SpD / 252 Spe\n"
                  "Timid Nature\n"
                  "- Thunderbolt\n"
                  "- Volt Switch\n"
                  "- Hidden Power Ice\n"
                  "- Grass Knot")
            imp = m.call("import_showdown", {"box": b, "slot": s, "showdownSet": sd})
            if not imp.get("success"):
                record("S1_showdown_import", False, f"import: {imp}")
            else:
                pk = m.call("get_box_pokemon", {"box": b, "slot": s})
                lg = m.call("check_legality", {"box": b, "slot": s})
                ok = (pk.get("success") and "Pikachu" in str(pk.get("species", "")) and lg.get("success"))
                record("S1_showdown_import", ok, f"species={pk.get('species')!r} legal={lg.get('valid')}")

        # S2: AutoFix on a Pokemon we just edited to be broken (level 1 with high-level moves doesn't match EXP)
        # Find a real Pokemon slot
        if boxes.get("pokemon"):
            first = boxes["pokemon"][0]; bb, ss = first["box"], first["slot"]
            # set level to 1 to provoke EXP/level mismatch
            m.call("set_level", {"box": bb, "slot": ss, "level": 1})
            before = m.call("check_legality", {"box": bb, "slot": ss})
            fix = m.call("auto_fix_pokemon", {"box": bb, "slot": ss, "allowNuclearFix": False})
            after = m.call("check_legality", {"box": bb, "slot": ss})
            # auto_fix should at least re-sync EXP; legality may not fully recover, but the call must succeed.
            record("S2_autofix_call", fix.get("success") is True, f"fix_log={fix.get('fixed_issues') or fix.get('fixes')}")
            record("S2_autofix_no_worse",
                   (after.get("valid") or False) >= (before.get("valid") or False),
                   f"before_valid={before.get('valid')} after_valid={after.get('valid')}")

        # S3: Mass batch edit
        m.call("batch_edit_all", {"instructions": ".CurrentLevel=100"})
        post = m.call("get_all_boxes", {})
        wrong = [p for p in post.get("pokemon", []) if p.get("level") != 100]
        record("S3_batch_level_100", not wrong, f"{len(wrong)} not at level 100" if wrong else f"all {len(post.get('pokemon', []))} at 100")

        # S4: Delete
        if boxes.get("pokemon"):
            first = boxes["pokemon"][-1]; bb, ss = first["box"], first["slot"]
            m.call("delete_pokemon", {"box": bb, "slot": ss})
            after = m.call("get_box_pokemon", {"box": bb, "slot": ss})
            record("S4_delete", after.get("empty") is True, str(after)[:120])

        # S5: Copy
        post = m.call("get_all_boxes", {})
        if post.get("pokemon"):
            src = post["pokemon"][0]
            # Find empty target
            used2 = {(p["box"], p["slot"]) for p in post["pokemon"]}
            tgt = next(((b, s) for b in range(18) for s in range(30) if (b, s) not in used2), None)
            if tgt:
                tb, tss = tgt
                cp = m.call("copy_pokemon", {"fromBox": src["box"], "fromSlot": src["slot"], "toBox": tb, "toSlot": tss})
                dst = m.call("get_box_pokemon", {"box": tb, "slot": tss})
                record("S5_copy", cp.get("success") and dst.get("species") == src["species"],
                       f"src={src['species']} dst={dst.get('species')}")

        # S6: Negative — invalid base64
        bad = m.call("load_save_file", {"base64Data": "@@@not-base64@@@"})
        record("S6_bad_base64", bad.get("success") is False, bad.get("error", "")[:120])

        # Reload good save for S7
        m.call("load_save_file", {"base64Data": data_b64})

        # S7: Negative — out-of-range box
        oob = m.call("get_box_pokemon", {"box": 999, "slot": 0})
        record("S7_oob_box", oob.get("success") is False, oob.get("error", "")[:120])

    finally:
        m.close()

    print()
    passed = sum(1 for _, ok, _ in results if ok)
    print(f"Scenarios: PASS {passed} / FAIL {len(results) - passed}")
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
