"""Send one command file to ui_driver.py and print its output: python uicmd.py <queue_dir> <file.py> [timeout_s]"""
import sys
import time
from pathlib import Path

Q = Path(sys.argv[1])
src = Path(sys.argv[2]).read_text()
timeout = float(sys.argv[3]) if len(sys.argv) > 3 else 120
nums = [int(p.stem.split("_")[1]) for p in Q.glob("cmd_*.py")]
n = max(nums) + 1 if nums else 1
(Q / f"cmd_{n}.py").write_text(src)
t0 = time.time()
while time.time() - t0 < timeout:
    o = Q / f"out_{n}.txt"
    if o.exists():
        time.sleep(0.2)
        sys.stdout.buffer.write(o.read_bytes().decode("utf-8", "replace").encode("ascii", "replace"))
        print()
        sys.exit(0)
    time.sleep(0.3)
print("TIMEOUT waiting for", n)
