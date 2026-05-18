import base64, json, os, subprocess, sys
from pathlib import Path
SAVE = Path("D:/Dev/pkhex-mcp/pokemon heartgold.sav")
SERVER = Path(os.environ["USERPROFILE"]) / ".dotnet" / "tools" / "pkhex-mcp.exe"
DOTNET = Path(os.environ["USERPROFILE"]) / ".dotnet"
env = os.environ.copy(); env["DOTNET_ROOT"] = str(DOTNET)
p = subprocess.Popen([str(SERVER)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, env=env, bufsize=0)
sid = [0]
def send(o): p.stdin.write((json.dumps(o)+"\n").encode()); p.stdin.flush()
def recv(w):
    while True:
        l = p.stdout.readline()
        try: m = json.loads(l)
        except Exception: continue
        if m.get("id") == w: return m
def req(method, params=None):
    sid[0] += 1; send({"jsonrpc":"2.0","id":sid[0],"method":method,"params":params or {}}); return recv(sid[0])
def call(name, args):
    return json.loads(req("tools/call", {"name": name, "arguments": args})["result"]["content"][0]["text"])
req("initialize",{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"o","version":"1"}})
send({"jsonrpc":"2.0","method":"notifications/initialized"})

data = base64.b64encode(SAVE.read_bytes()).decode()
call("load_save_file", {"base64Data": data})

# Pick empty slot
boxes = call("get_all_boxes", {})
used = {(x["box"], x["slot"]) for x in boxes["pokemon"]}
b, s = next(((bb, ss) for bb in range(18) for ss in range(30) if (bb, ss) not in used))

set_name = sys.argv[1] if len(sys.argv) > 1 else "Tyranitar"
SD = {
    "Tyranitar": "Tyranitar @ Choice Band\nAbility: Sand Stream\nLevel: 50\n- Crunch\n- Stone Edge\n- Earthquake\n- Pursuit",
    "Salamence": "Salamence @ Life Orb\nAbility: Intimidate\nLevel: 50\n- Dragon Claw\n- Earthquake\n- Fire Fang\n- Crunch",
    "Snorlax":   "Snorlax @ Leftovers\nAbility: Thick Fat\nLevel: 50\n- Body Slam\n- Earthquake\n- Crunch\n- Curse",
    "Heatran":   "Heatran @ Choice Scarf\nAbility: Flash Fire\nLevel: 50\n- Fire Blast\n- Earth Power\n- Dragon Pulse\n- Flamethrower",
}[set_name]
print("Importing", set_name)
imp = call("import_showdown", {"box": b, "slot": s, "showdownSet": SD})
print("Import:", imp)
lg = call("get_legality_details", {"box": b, "slot": s})
for cat in lg.get("categories", []):
    for c in cat["checks"]:
        if c["severity"] == "Invalid":
            print(f"  INVALID [{cat['category']}] {c['message']}")
p.stdin.close()
p.wait(timeout=5)
