"""Test mass_auto_fix with nuclear on the entire save."""
import base64, json, os, subprocess, sys, time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SAVE = REPO / "pokemon heartgold.sav"
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
    req("initialize",{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"mf","version":"1"}})
    send({"jsonrpc":"2.0","method":"notifications/initialized"})
    return p, call


p, call = mcp()
try:
    data = base64.b64encode(SAVE.read_bytes()).decode()
    call("load_save_file", {"base64Data": data})
    pre = call("legality_report_all", {})
    print(f"Before: total={pre['total']} valid={pre['valid']} invalid={pre['invalid_count']}")

    t0 = time.time()
    fix = call("mass_auto_fix", {"allowNuclearFix": True})
    dt = time.time() - t0
    s = fix["summary"]
    print(f"\nMass auto-fix in {dt:.1f}s")
    print(f"  scanned       = {s['scanned']}")
    print(f"  already_legal = {s['already_legal']}")
    print(f"  fixed_soft    = {s['fixed_soft']}")
    print(f"  fixed_nuclear = {s['fixed_nuclear']}")
    print(f"  still_illegal = {s['still_illegal']}")
    print(f"  total_fixed   = {s['total_fixed']}")

    post = call("legality_report_all", {})
    print(f"\nAfter: total={post['total']} valid={post['valid']} invalid={post['invalid_count']}")
    print(f"Improvement: {post['valid'] - pre['valid']} more legal Pokemon")
finally:
    p.stdin.close()
    try: p.wait(timeout=10)
    except Exception: p.kill()
