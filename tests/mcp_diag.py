"""Diagnose Showdown import legality and AutoFix behavior."""
import base64, json, os, subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SAVE = REPO / "pokemon heartgold.sav"
SERVER = Path(os.environ["USERPROFILE"]) / ".dotnet" / "tools" / "pkhex-mcp.exe"
DOTNET = Path(os.environ["USERPROFILE"]) / ".dotnet"


def mcp():
    env = os.environ.copy(); env["DOTNET_ROOT"] = str(DOTNET)
    p = subprocess.Popen([str(SERVER)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, env=env, bufsize=0)
    state = {"id": 0}
    def send(o): p.stdin.write((json.dumps(o)+"\n").encode()); p.stdin.flush()
    def recv(w):
        while True:
            l = p.stdout.readline()
            if not l: raise RuntimeError("closed")
            try: m = json.loads(l)
            except Exception: continue
            if m.get("id") == w: return m
    def req(method, params=None):
        state["id"] += 1; send({"jsonrpc":"2.0","id":state["id"],"method":method,"params":params or {}})
        return recv(state["id"])
    def call(name, args):
        r = req("tools/call", {"name": name, "arguments": args})
        if "error" in r: return {"_rpc_error": r["error"]}
        return json.loads(r["result"]["content"][0]["text"])
    req("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name":"d","version":"1"}})
    send({"jsonrpc":"2.0","method":"notifications/initialized"})
    return p, call


p, call = mcp()
try:
    data = base64.b64encode(SAVE.read_bytes()).decode()
    call("load_save_file", {"base64Data": data})

    # Empty slot
    boxes = call("get_all_boxes", {})
    used = {(x["box"], x["slot"]) for x in boxes["pokemon"]}
    b, s = next(((bb, ss) for bb in range(18) for ss in range(30) if (bb, ss) not in used))
    print(f"Importing into empty {b}/{s}")

    # Try a simple legal Pikachu set for HGSS
    sd = ("Pikachu @ Light Ball\n"
          "Ability: Static\n"
          "Level: 50\n"
          "- Thunderbolt\n"
          "- Quick Attack\n"
          "- Iron Tail\n"
          "- Volt Tackle")
    imp = call("import_showdown", {"box": b, "slot": s, "showdownSet": sd})
    print("Import:", json.dumps(imp, indent=2))

    lg = call("get_legality_details", {"box": b, "slot": s})
    print("Legality:", json.dumps(lg, indent=2)[:2000])

    # Now check the AutoFix flow on a broken Moltres
    first = boxes["pokemon"][0]
    bb, ss = first["box"], first["slot"]
    print(f"\nBreaking {first['species']} at {bb}/{ss}")
    call("set_level", {"box": bb, "slot": ss, "level": 1})
    before = call("check_legality", {"box": bb, "slot": ss})
    print("Before fix valid:", before.get("valid"))
    fix = call("auto_fix_pokemon", {"box": bb, "slot": ss, "allowNuclearFix": False})
    print("Fix result:", json.dumps(fix, indent=2)[:1500])
    after = call("check_legality", {"box": bb, "slot": ss})
    print("After fix valid:", after.get("valid"))
    if not after.get("valid"):
        print("Remaining results:")
        for r in after.get("results", [])[:20]:
            print(" ", r.get("severity"), r.get("identifier"), r.get("message"))
finally:
    p.stdin.close()
    try: p.wait(timeout=5)
    except Exception: p.kill()
