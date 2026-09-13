from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
src = (ROOT / "PreprocessingForm.cs").read_text(encoding="utf-8")

def need(cond, msg):
    if not cond:
        raise SystemExit("[FAIL] " + msg)

# Registry support.
need("using Microsoft.Win32;" in src, "registry namespace missing")
need("RegistryView.Registry64" in src and "RegistryView.Registry32" in src,
     "32/64-bit registry views are not both checked")

# ArcGIS Pro.
need("GetArcGisProInstallDirectoriesFromRegistry" in src,
     "ArcGIS Pro registry detection missing")
need(r'SOFTWARE\ESRI\ArcGISPro' in src,
     "ArcGIS Pro ESRI registry key missing")
need(r'App Paths\ArcGISPro.exe' in src,
     "ArcGIS Pro App Paths fallback missing")
need(r'"ArcGIS", "Pro", "bin", "Python", "envs"' in src,
     "ArcGIS Pro official env layout missing")

# ArcMap.
need("AddArcMapRegistryCandidates" in src,
     "ArcMap registry detection missing")
need('StartsWith("Desktop10"' in src,
     "ArcGIS Desktop 10.x registry enumeration missing")
need('"Python27", "ArcGIS" + version, "python.exe"' in src,
     "ArcMap Python27/ArcGIS10.x candidate missing")
need("IsVerifiedArcMapPython" in src and "ARCPY_OK" in src,
     "ArcMap Python2/arcpy verification missing")
need("AddArcMapCommonBaseCandidates" in src,
     "ArcMap custom-layout scan missing")

# QGIS.
need("GetQgisInstallDirectoriesFromRegistry" in src,
     "QGIS installed-program registry detection missing")
need(r'CurrentVersion\Uninstall' in src,
     "QGIS uninstall registry scan missing")
need(r'App Paths\" + appName' in src,
     "QGIS App Paths fallback missing")
need('"OSGeo4W"' in src and '"OSGeo4W64"' in src,
     "QGIS OSGeo4W fallback missing")
need("AddQgisCandidatesFromDirectory" in src,
     "QGIS Program Files/custom-directory scan missing")

# Manual fallback stays in UI.
need('NewButton("浏览...", 85)' in src,
     "manual browse fallback missing")

print("V5.0.8 portable GIS detection check: PASS")
