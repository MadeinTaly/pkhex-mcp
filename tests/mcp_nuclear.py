"""Test nuclear regen: import a legal Pikachu, edit it to break legality, then auto_fix with nuclear."""
import base64, json, os, subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SAVE = REPO / "pokemon heartgold.sav"
SERVER = Path(os.environ["USERPROFILE"]) / ".dotnet" / "tools" / "pkhex-mcp.exe"
DOTNET = Path(os.environ["USERPROFILE"]) / ".dotnet"

env = os.environ.copy(); env["DOTNET_ROOT"] = str(DOTNET)
p = subprocess.Popen([str(SERVER)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, env=env, bufsize=0)
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

req("initialize", {"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"n","version":"1"}})
send({"jsonrpc":"2.0","method":"notifications/initialized"})

data = base64.b64encode(SAVE.read_bytes()).decode()
call("load_save_file", {"base64Data": data})

# Find empty slot
boxes = call("get_all_boxes", {})
used = {(x["box"], x["slot"]) for x in boxes["pokemon"]}
b, s = next(((bb, ss) for bb in range(18) for ss in range(30) if (bb, ss) not in used))

# Import legal Pikachu
sd = "Pikachu @ Light Ball\nAbility: Static\nLevel: 50\n- Thunderbolt\n- Quick Attack\n- Iron Tail\n- Volt Tackle"
imp = call("import_showdown", {"box": b, "slot": s, "showdownSet": sd})
print("Import:", imp)
lg = call("check_legality", {"box": b, "slot": s})
print("Initial legal:", lg.get("valid"))

# Break: set level to 1 (will cause EXP mismatch + maybe encounter)
call("set_level", {"box": b, "slot": s, "level": 1})
lg2 = call("check_legality", {"box": b, "slot": s})
print("After break legal:", lg2.get("valid"))

# Soft fix only
fix1 = call("auto_fix_pokemon", {"box": b, "slot": s, "allowNuclearFix": False})
print("Soft fix:", fix1)
lg3 = call("check_legality", {"box": b, "slot": s})
print("After soft legal:", lg3.get("valid"))

# Break harder, then nuclear
call("set_level", {"box": b, "slot": s, "level": 1})
call("set_moves", {"box": b, "slot": s, "move1": "Splash", "move2": "Splash", "move3": "Splash", "move4": "Splash"})
lg4 = call("check_legality", {"box": b, "slot": s})
print("After hard break legal:", lg4.get("valid"))
fix2 = call("auto_fix_pokemon", {"box": b, "slot": s, "allowNuclearFix": True})
print("Nuclear fix:", fix2)
lg5 = call("check_legality", {"box": b, "slot": s})
print("After nuclear legal:", lg5.get("valid"))

p.stdin.close()
try: p.wait(timeout=5)
except Exception: p.kill()
