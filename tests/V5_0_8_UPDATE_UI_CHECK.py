from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
help_cs = (ROOT / "MainForm.Help.cs").read_text(encoding="utf-8")
prep_cs = (ROOT / "PreprocessingForm.cs").read_text(encoding="utf-8")

def need(cond, msg):
    if not cond:
        raise AssertionError(msg)

need("UpdateCheckCacheMinutes = 30" in help_cs, "30-minute update cache missing")
need("update_check_cache.txt" in help_cs, "local update cache missing")
need("QueryLatestReleaseFallback" in help_cs, "GitHub fallback query missing")
need('GitHubReleasesUrl + ".atom"' in help_cs, "Atom fallback missing")
need("FormatRateLimitResetLocal" in help_cs, "friendly reset-time conversion missing")
need("预计可在 " in help_cs, "friendly rate-limit message missing")
need("RateLimit-Reset=" not in help_cs, "raw reset timestamp still user-facing")
need("BeginArcMapBackgroundDetection" in prep_cs, "ArcMap background detection missing")
need("ThreadPool.QueueUserWorkItem" in prep_cs, "ArcMap probe is still synchronous")
need("GetCachedArcGisDesktopPython" in prep_cs, "ArcMap cache lookup missing")
need('txtPython.Text = cachedArcMap ?? "";' in prep_cs,
     "ArcMap selection does not clear stale engine path")
print("V5.0.8 update/UI regression check: PASS")
