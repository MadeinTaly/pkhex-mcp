"""User-requested workflow: load -> trainer -> audit -> mass_fix(nuclear) -> audit -> export."""
import base64, json, os, subprocess, time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SAVE = REPO / "pokemon heartgold.sav"
OUT = REPO / "pokemon heartgold.fixed.sav"
SERVER = Path(os.environ["USERPROFILE"]) / ".dotnet" / "tools" / "pkhex-mcp.exe"
DOTNET = Path(os.environ["USERPROFILE"]) / ".dotnet"
env = os.environ.copy(); env["DOTNET_ROOT"] = str(DOTNET)


def mcp():
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
        sid[0]+=1; send({"jsonrpc":"2.0","id":sid[0],"method":method,"params":params or {}}); return recv(sid[0])
    def call(name, args):
        return json.loads(req("tools/call", {"name": name, "arguments": args})["result"]["content"][0]["text"])
    req("initialize",{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"uf","version":"1"}})
    send({"jsonrpc":"2.0","method":"notifications/initialized"})
    return p, call


p, call = mcp()
try:
    data = base64.b64encode(SAVE.read_bytes()).decode()
    print("== load_save_file ==")
    r = call("load_save_file", {"base64Data": data})
    print(json.dumps(r, indent=2)[:500])

    print("\n== get_trainer_info ==")
    ti = call("get_trainer_info", {})
    print(json.dumps(ti, indent=2)[:800])

    print("\n== legality_audit (before) ==")
    pre = call("legality_audit", {})
    print(json.dumps(pre, indent=2)[:1500])

    print("\n== mass_auto_fix(nuclear=true) ==")
    t0 = time.time()
    fix = call("mass_auto_fix", {"allowNuclearFix": True})
    print(f"elapsed={time.time()-t0:.1f}s")
    print(json.dumps(fix.get("summary", fix), indent=2)[:800])

    print("\n== legality_audit (after) ==")
    post = call("legality_audit", {})
    print(json.dumps(post, indent=2)[:1500])

    print("\n== get_save_base64 -> write fixed.sav ==")
    exp = call("get_save_base64", {})
    b64 = exp.get("base64") or exp.get("data") or exp.get("base64Data")
    if not b64:
        print("KEYS:", list(exp.keys())); raise SystemExit(1)
    OUT.write_bytes(base64.b64decode(b64))
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes)")
finally:
    p.stdin.close()
    try: p.wait(timeout=10)
    except Exception: p.kill()
