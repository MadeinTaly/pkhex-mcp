"""Run every test in tests/ that starts with mcp_ and exits 0/1.

Returns non-zero if any test fails. Lighter alternative to pytest for this repo —
all tests are subprocess-based and self-contained."""
import subprocess, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PY = sys.executable
TESTS = [
    "mcp_smoke.py",
    "mcp_verify.py",
    "mcp_scenarios.py",
    "mcp_nuclear.py",
    "mcp_stress.py",
    "mcp_massfix.py",
    "mcp_deep.py",
]

failed = []
for name in TESTS:
    print(f"\n=== {name} ===")
    rc = subprocess.call([PY, str(HERE / name)])
    if rc != 0:
        failed.append(name)
        print(f"!! {name} exit {rc}")

print()
if failed:
    print(f"FAIL: {failed}")
    sys.exit(1)
print(f"OK: all {len(TESTS)} test files passed")
