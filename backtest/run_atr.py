"""Runner que resuelve paths con caracteres especiales (u202f en nombres NT8)."""
import sys, io, os, importlib, importlib.util

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

# Lee paths desde _csv_paths.bin (uno por línea, UTF-8)
bin_file = os.path.join(os.path.dirname(__file__), "_csv_paths.bin")
if not os.path.exists(bin_file):
    print("ERROR: crea backtest/_csv_paths.bin con los paths (uno por línea, UTF-8)")
    sys.exit(1)

paths = open(bin_file, "rb").read().decode("utf-8").strip().splitlines()
paths = [p.strip() for p in paths if p.strip()]
print(f"Paths a procesar: {len(paths)}")
for p in paths:
    print(f"  {p[-60:]}")

sys.argv = [sys.argv[0]] + paths

spec = importlib.util.spec_from_file_location(
    "atr", os.path.join(os.path.dirname(__file__), "analyze_regime_atr.py")
)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
mod.main()
