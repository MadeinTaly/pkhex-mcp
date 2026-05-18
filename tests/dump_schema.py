"""Dump tool input schemas for inspection."""
from __future__ import annotations
import json, os, subprocess, sys
from pathlib import Path
SERVER = Path(os.environ["USERPROFILE"]) / ".dotnet" / "tools" / "pkhex-mcp.exe"
env = os.environ.copy(); env["DOTNET_ROOT"] = str(Path(os.environ["USERPROFILE"]) / ".dotnet")
p = subprocess.Popen([str(SERVER)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, env=env, bufsize=0)
def send(o): p.stdin.write((json.dumps(o)+"\n").encode()); p.stdin.flush()
def recv(want):
    while True:
        l = p.stdout.readline()
        if not l: return None
        try: m = json.loads(l)
        except Exception: continue
        if m.get("id") == want: return m
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"d","version":"1"}}})
recv(1)
send({"jsonrpc":"2.0","method":"notifications/initialized"})
send({"jsonrpc":"2.0","id":2,"method":"tools/list"})
r = recv(2)
want = set(sys.argv[1:]) if len(sys.argv) > 1 else None
for t in r["result"]["tools"]:
    if want and t["name"] not in want: continue
    props = t.get("inputSchema",{}).get("properties",{})
    print(f"{t['name']}: {list(props.keys())}")
p.stdin.close(); p.wait(timeout=5)
