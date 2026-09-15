namespace UnifiedHydroLauncher
{
    /// <summary>
    /// User-visible application identity. Keep UI version/repository metadata here
    /// so the main window, preprocessing window and About dialog stay consistent.
    /// </summary>
    internal static class AppInfo
    {
        internal const string ProductName = "Unified Hydro Workflow";
        internal const string Version = "5.1.0";
        internal const string DisplayVersion = "V" + Version;
        internal const string WindowTitle = ProductName + " " + DisplayVersion;
        internal const string LauncherProduct = "UnifiedHydroLauncher " + DisplayVersion;
        internal const string AssemblyVersion = "5.1.0.0";
        internal const string WorkflowCoreVersion = "3.5";
        internal const string GitHubUrl = "https://github.com/xyhosino/UnifiedHydroWorkflow";
        internal const string GitHubReleasesUrl = GitHubUrl + "/releases";
        internal const string GitHubLatestReleaseApiUrl =
            "https://api.github.com/repos/xyhosino/UnifiedHydroWorkflow/releases/latest";
        internal const string HelpFileName = "Unified_Hydro_Workflow_V5.1.0_简明使用说明.txt";
    }
}
