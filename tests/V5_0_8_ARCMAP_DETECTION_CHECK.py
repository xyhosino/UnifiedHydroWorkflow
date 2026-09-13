from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
src = (ROOT / "PreprocessingForm.cs").read_text(encoding="utf-8")

def need(cond, msg):
    if not cond:
        raise SystemExit("[FAIL] " + msg)

need("sys.version_info[0]" in src, "ArcMap detection must verify Python major version")
need("ARCPY_OK" in src, "ArcMap detection must verify import arcpy")
need("45000" in src, "ArcPy import probe needs a generous first-load timeout")
need("lower.Contains(\"arcgispro\")" in src, "ArcGIS Pro paths must be excluded from ArcMap")
need("!string.IsNullOrWhiteSpace(arcGisDesktopPythonCache)" in src,
     "negative ArcMap detection must not be permanently cached")
need("product.Contains(\"pro\")" in src, "explicit Pro product reports must be rejected")

print("V5.0.8 ArcMap detection regression check: PASS")
