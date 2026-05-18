"""Stress test: import many legal Showdown sets, run mass auto-fix, check legality of entire save."""
import base64, json, os, subprocess, sys, time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SAVE = REPO / "pokemon heartgold.sav"
SERVER = Path(os.environ["USERPROFILE"]) / ".dotnet" / "tools" / "pkhex-mcp.exe"
DOTNET = Path(os.environ["USERPROFILE"]) / ".dotnet"

# 20 sets known to be Gen4-legal (no Gen5+ moves, no Hidden Power constraints relaxed)
SETS = """
Pikachu @ Light Ball
Ability: Static
Level: 50
- Thunderbolt
- Quick Attack
- Iron Tail
- Volt Tackle
===
Tyranitar @ Choice Band
Ability: Sand Stream
Level: 50
- Crunch
- Stone Edge
- Earthquake
- Pursuit
===
Salamence @ Life Orb
Ability: Intimidate
Level: 50
- Dragon Claw
- Earthquake
- Fire Fang
- Crunch
===
Metagross @ Leftovers
Ability: Clear Body
Level: 50
- Meteor Mash
- Earthquake
- Bullet Punch
- Pursuit
===
Garchomp @ Yache Berry
Ability: Sand Veil
Level: 50
- Dragon Claw
- Earthquake
- Fire Fang
- Stone Edge
===
Dragonite @ Lum Berry
Ability: Inner Focus
Level: 50
- Dragon Dance
- Outrage
- Earthquake
- Fire Punch
===
Gyarados @ Life Orb
Ability: Intimidate
Level: 50
- Waterfall
- Earthquake
- Ice Fang
- Stone Edge
===
Snorlax @ Leftovers
Ability: Thick Fat
Level: 50
- Body Slam
- Earthquake
- Crunch
- Curse
===
Heatran @ Choice Scarf
Ability: Flash Fire
Level: 50
- Fire Blast
- Earth Power
- Dragon Pulse
- Flamethrower
===
Lucario @ Life Orb
Ability: Inner Focus
Level: 50
- Close Combat
- Crunch
- ExtremeSpeed
- Bullet Punch
===
""".strip()


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
        state["id"] += 1; send({"jsonrpc":"2.0","id":state["id"],"method":method,"params":params or {}}); return recv(state["id"])
    def call(name, args):
        r = req("tools/call", {"name": name, "arguments": args})
        return json.loads(r["result"]["content"][0]["text"])
    req("initialize", {"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"st","version":"1"}})
    send({"jsonrpc":"2.0","method":"notifications/initialized"})
    return p, call


def main():
    data = base64.b64encode(SAVE.read_bytes()).decode()
    sets = [s.strip() for s in SETS.split("===") if s.strip()]
    p, call = mcp()
    try:
        call("load_save_file", {"base64Data": data})
        boxes = call("get_all_boxes", {})
        used = {(x["box"], x["slot"]) for x in boxes.get("pokemon", [])}

        # Pick empty slots
        empties = [(b, s) for b in range(18) for s in range(30) if (b, s) not in used]
        if len(empties) < len(sets):
            print(f"not enough empty slots: {len(empties)}", file=sys.stderr); return 1

        t0 = time.time()
        results = []
        for sd, (b, s) in zip(sets, empties):
            r = call("import_showdown", {"box": b, "slot": s, "showdownSet": sd})
            ok = r.get("success") and r.get("legal") is True
            results.append((sd.splitlines()[0], b, s, r.get("method"), r.get("legal"), ok))
            err = r.get("error", "")
            print(f"  {'OK' if ok else 'FAIL'} {sd.splitlines()[0]:35s} -> method={r.get('method')} legal={r.get('legal')} err={err[:80]}")
        dt = (time.time() - t0)
        legal_count = sum(1 for *_, ok in results if ok)
        print(f"\nImported {legal_count}/{len(sets)} legal in {dt:.1f}s ({dt*1000/len(sets):.0f}ms each)")

        # Save round-trip
        t0 = time.time()
        exp = call("get_save_base64", {})
        print(f"Export: {len(exp.get('base64', ''))} chars in {(time.time()-t0)*1000:.0f}ms")

        # Mass legality report
        t0 = time.time()
        rep = call("legality_report_all", {})
        print(f"legality_report_all: total={rep.get('total')} valid={rep.get('valid')} invalid={rep.get('invalid_count')} in {(time.time()-t0)*1000:.0f}ms")

        return 0 if legal_count == len(sets) else 1
    finally:
        p.stdin.close()
        try: p.wait(timeout=5)
        except Exception: p.kill()


if __name__ == "__main__":
    sys.exit(main())
