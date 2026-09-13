using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using UnifiedHydroLauncher;

class EnvironmentTests
{
    static int count;
    static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name); count++;
    }
    static object Call(object form, string method, params object[] args)
    { return form.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args); }
    static T Field<T>(object form, string name)
    { return (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
    [STAThread]
    static int Main(string[] args)
    {
        string testRoot = Path.Combine(Path.GetTempPath(),
            "UnifiedHydroLauncherV44Tests_" + Guid.NewGuid().ToString("N"));
        string testUnit = Path.Combine(testRoot, "unit_1");

        // V5.0.4 deployment detection uses real signature files before the injected
        // file-system checker takes over.  The test executable runs from tests\,
        // so create minimal disposable signatures beside it to emulate legacy flat deployment.
        string deploymentBase = AppDomain.CurrentDomain.BaseDirectory;
        string[] deploymentSignatureFiles = { "FloodAnalysisSYS.exe", "project.db", "SysDAL.dll" };
        var createdDeploymentFiles = new System.Collections.Generic.List<string>();
        string deploymentSkby = Path.Combine(deploymentBase, "SKBYEXE");
        bool createdSkby = !Directory.Exists(deploymentSkby);
        if (createdSkby) Directory.CreateDirectory(deploymentSkby);
        foreach (string signature in deploymentSignatureFiles)
        {
            string path = Path.Combine(deploymentBase, signature);
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, new byte[] { 1 });
                createdDeploymentFiles.Add(path);
            }
        }
        Directory.CreateDirectory(testUnit);
        foreach (string name in new[] { "wata", "rivl", "node", "土地利用", "土壤类型" })
            foreach (string extension in new[] { ".shp", ".shx", ".dbf" })
                File.WriteAllBytes(Path.Combine(testUnit, name + extension), new byte[40]);
        File.WriteAllBytes(Path.Combine(testUnit, "站点信息.xlsx"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(testUnit, "20060803111降雨.xlsx"), new byte[] { 1 });

        try
        {
        string missing = "";
        bool win64 = true;
        Func<string,bool> files = p => !p.EndsWith(missing, StringComparison.OrdinalIgnoreCase) || missing.Length == 0;
        var checker = new EnvironmentChecker(files, p => true, @"C:\Windows", win64);
        Assert(checker.Check(@"E:\模型", @"E:\输入").Passed, "A Chinese paths accepted");
        var pendingInput = checker.Check(@"E:\模型", "");
        Assert(pendingInput.Pending && pendingInput.FatalCount == 0 && pendingInput.PendingCount == 1,
            "empty input alone is pending without fatal");
        Assert(pendingInput.Log.Contains("ENV_PATH_INPUT PENDING not_configured") &&
            pendingInput.Log.Contains("ENV_CHECK_SUMMARY fatal=0 pending=1 warning=0"),
            "pending input has explicit log");
        var unavailableModel = checker.Check("", @"E:\输入");
        Assert(!unavailableModel.Passed && unavailableModel.FatalCount > 0 &&
            unavailableModel.PendingCount == 0,
            "unavailable automatic model directory is fatal");
        var missingDirectoryChecker = new EnvironmentChecker(p => true,
            p => !p.EndsWith("不存在", StringComparison.Ordinal), @"C:\Windows", true);
        var missingInput = missingDirectoryChecker.Check(
            @"E:\模型", @"E:\不存在");
        Assert(!missingInput.Passed && !missingInput.Pending && missingInput.FatalCount > 0 &&
            missingInput.PendingCount == 0 && missingInput.Log.Contains("ENV_PATH_INPUT FAIL missing_or_invalid"),
            "configured missing input remains fatal");
        var missingModel = missingDirectoryChecker.Check(
            @"E:\不存在", @"E:\输入");
        Assert(!missingModel.Passed && !missingModel.Pending && missingModel.FatalCount > 0 &&
            missingModel.PendingCount == 0 && missingModel.Log.Contains("ENV_PATH_MODEL FAIL missing_or_invalid"),
            "configured missing model remains fatal");
        foreach (string dll in new[] { "msvcr100.dll", "msvcp100.dll", "UnifiedHydroWorkflow.exe",
            "UnifiedHydroWorkflow.exe.config", "project.db", "SysDAL.dll", "DBSupport.dll",
            "System.Data.SQLite.dll", "ogr_wrap.dll", "gdal_wrap.dll", "gdal110.dll" })
        {
            missing = dll;
            var r = checker.Check(@"E:\模型", @"E:\输入");
            Assert(!r.Passed && string.Join("\n", r.Details.ToArray()).Contains(dll), "B/C missing " + dll);
            if (dll == "UnifiedHydroWorkflow.exe")
                Assert(string.Join("\n", r.Details.ToArray()).Contains(
                    "请将 Launcher 与 UnifiedHydroWorkflow.exe 放在水文模型根目录"),
                    "missing auto core has deployment guidance");
        }
        missing = "";
        Assert(!checker.Check(@"E:\model dir", @"E:\输入").Passed, "D model space");
        Assert(!checker.Check(@"E:\模型", @"E:\input dir").Passed, "D input space");
        foreach (bool is64 in new[] { true, false })
        {
            string expected = is64 ? "SysWOW64" : "System32";
            var vc = new EnvironmentChecker(p => p.Contains(expected), p => true, @"C:\Windows", is64);
            string detail, log;
            Assert(vc.HasVc2010X86(out detail, out log), "x86 system directory " + expected);
        }
        var absent = new EnvironmentChecker(p => false, p => false, @"C:\Windows", true);
        Assert(absent.Check(@"E:\模型", "").RuntimeDirectory == "", "missing Runtime is safe");
        Assert(!absent.Check("\"invalid", "").Passed, "invalid and absent paths blocked");
        missing = "msvcr100.dll";
        Assert(checker.Check(@"E:\模型", @"E:\输入").RuntimeDirectory == @"E:\模型\Runtime", "Runtime discovery");

        using (var form = new MainForm())
        {
            Field<ComboBox>(form, "cmbInputMode").SelectedIndex = 1;
            Field<TextBox>(form, "txtUnitRoot").Text = testRoot;
            Call(form, "ScanUnits");
            missing = "";
            typeof(MainForm).GetField("_environmentChecker", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(form, checker);
            Call(form, "CheckEnvironment");
            string automaticCore = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "UnifiedHydroWorkflow.exe");
            Assert(Field<TextBox>(form, "txtExe").ReadOnly &&
                string.Equals(Field<TextBox>(form, "txtExe").Text, automaticCore,
                    StringComparison.OrdinalIgnoreCase) &&
                typeof(MainForm).GetField("btnBrowseExe", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                "core path is automatic and browse control is removed");
            var grid = Field<DataGridView>(form, "gridUnits");
            grid.Rows[0].Cells["Source"].Value = "已生成";
            grid.Rows[0].Cells["Coverage"].Value = "100%";
            grid.Rows[0].Cells["Status"].Value = "预检通过";
            grid.Rows[0].Cells["Note"].Value = "retained";
            var combo = Field<ComboBox>(form, "cmbUnit");
            combo.SelectedIndex = 1;
            Call(form, "CheckEnvironment");
            Assert(Convert.ToString(grid.Rows[0].Cells["Status"].Value) == "预检通过" &&
                Convert.ToString(grid.Rows[0].Cells["Source"].Value) == "已生成" && combo.SelectedIndex == 1,
                "environment check preserves status and selection");
            Field<TextBox>(form, "txtUnitRoot").Text = @"E:\missing input";
            Assert(!Field<Button>(form, "btnRun").Enabled && Field<Button>(form, "btnScan").Enabled &&
                Field<Button>(form, "btnBrowseRoot").Enabled, "fatal blocks execution but permits repair");
            Call(form, "SetRunning", false);
            Assert(!Field<Button>(form, "btnRun").Enabled, "process exit cannot bypass fatal gate");
            Field<TextBox>(form, "txtExe").Text = @"E:\其他位置\UnifiedHydroWorkflow.exe";
            Call(form, "CheckEnvironment");
            Assert(string.Equals(Field<TextBox>(form, "txtExe").Text, automaticCore,
                StringComparison.OrdinalIgnoreCase), "environment check restores automatic core path");
            Field<TextBox>(form, "txtUnitRoot").Text = testRoot;
            Assert(Field<Button>(form, "btnRun").Enabled, "valid path change automatically restores execution");
            Call(form, "SetRunning", true);
            Call(form, "CheckEnvironment");
            Assert(!Field<Button>(form, "btnRun").Enabled, "recheck during running cannot start second process");
            missing = "gdal110.dll";
            Call(form, "SetRunning", false);
            Assert(!Field<Button>(form, "btnRun").Enabled, "exit rechecks changed dependency");
            missing = "";
            Call(form, "CheckEnvironment");
            Assert(Field<Button>(form, "btnRun").Enabled, "manual recheck restores repaired dependency");
            Field<TextBox>(form, "txtUnitRoot").Text = "";
            Assert(!Field<Button>(form, "btnRun").Enabled &&
                Field<Label>(form, "lblEnvironment").Text == "运行环境：○ 待配置" &&
                Field<TextBox>(form, "txtEnvironmentDetails").Text.Contains("参数未完整配置"),
                "pending GUI disables execution with incomplete-parameters reason");
            Field<TextBox>(form, "txtUnitRoot").Text = testRoot;
            Assert(Field<Button>(form, "btnRun").Enabled &&
                Field<Label>(form, "lblEnvironment").Text == "运行环境：✓ 正常",
                "selecting pending directory immediately restores execution");

            var baselineAssembly = Assembly.LoadFrom(Path.GetFullPath(@"tests\baseline\Baseline.dll"));
            using (var baseline = (Form)Activator.CreateInstance(baselineAssembly.GetType("UnifiedHydroLauncher.MainForm")))
            {
                foreach (object f in new object[] { form, baseline })
                {
                    Field<TextBox>(f, "txtUnitRoot").Text = testRoot;
                    Field<TextBox>(f, "txtProject").Text = "测试工程";
                    var units = Field<ComboBox>(f, "cmbUnit");
                    units.Items.Clear(); units.Items.Add("all"); units.Items.Add("unit_1"); units.SelectedIndex = 1;
                }
                for (int mode = 0; mode < 3; mode++)
                {
                    Field<ComboBox>(form, "cmbMode").SelectedIndex = mode;
                    Field<ComboBox>(baseline, "cmbMode").SelectedIndex = mode;
                    string commandLine = (string)Call(form, "BuildArguments");
                    Assert(commandLine == (string)Call(baseline, "BuildArguments") && !commandLine.Contains("--all"),
                        "E baseline single-unit arguments mode=" + mode);
                }

                Field<ComboBox>(form, "cmbMode").SelectedIndex = 0;
                Field<ComboBox>(baseline, "cmbMode").SelectedIndex = 0;
                Field<ComboBox>(form, "cmbUnit").SelectedIndex = 0;
                Field<ComboBox>(baseline, "cmbUnit").SelectedIndex = 0;
                string allLegacy = (string)Call(form, "BuildArguments");
                Assert(allLegacy == (string)Call(baseline, "BuildArguments") &&
                    allLegacy.Contains(" --all "),
                    "legacy --all arguments are byte-for-byte V4.3.2 compatible");

                Field<ComboBox>(form, "cmbInputMode").SelectedIndex = 0;
                Field<ComboBox>(form, "cmbUnit").SelectedIndex = 1;
                Field<ComboBox>(baseline, "cmbUnit").SelectedIndex = 1;
                string mapped = (string)Call(form, "BuildArguments");
                Assert(mapped.StartsWith((string)Call(baseline, "BuildArguments"),
                    StringComparison.Ordinal) && mapped.Contains(" --input-map \""),
                    "smart mode appends only input-map after legacy command");
            }

            if (args.Length > 0)
            {
                string realRoot = args[0];
                string realUnit = Path.Combine(realRoot, "WFC17_2_3_1");
                string mappedRiver = Path.Combine(testUnit, "mapped_river.shp");
                foreach (string extension in new[] { ".shp", ".shx", ".dbf" })
                    File.Copy(Path.Combine(realUnit, "rivl" + extension),
                        Path.ChangeExtension(mappedRiver, extension), true);
                object topology = typeof(MainForm).GetMethod(
                    "DetectExternalSourceTopology",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(
                        null, new object[] { mappedRiver });
                Assert((bool)topology.GetType().GetField("Determined").GetValue(topology),
                    "topology scan reads explicitly mapped River DBF");

                LauncherUnitInput discovered = InputDiscovery.Discover(realUnit,
                    InputDiscovery.AutomaticMode, "站点信息.xlsx",
                    InputDiscovery.AutomaticMode, "20060803111降雨.xlsx");
                using (var mappingForm = new InputMappingForm(realRoot,
                    new[] { discovered }, InputDiscovery.AutomaticMode,
                    "站点信息.xlsx", InputDiscovery.AutomaticMode,
                    "20060803111降雨.xlsx"))
                {
                    var mappingGrid = (DataGridView)mappingForm.GetType().GetField(
                        "grid", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(mappingForm);
                    Assert(mappingGrid.Rows.Count == 1 && mappingGrid.Columns.Count == 10,
                        "mapping GUI builds ten required columns");
                }

                Field<TextBox>(form, "txtUnitRoot").Text = realRoot;
                Field<ComboBox>(form, "cmbInputMode").SelectedIndex = 0;
                Call(form, "ScanUnits");
                Assert(Field<DataGridView>(form, "gridUnits").Rows.Count == 34 &&
                    Field<Label>(form, "lblInputData").Text == "34/34 已识别",
                    "GUI smart scan recognizes all 34 real units");
                Field<DataGridView>(form, "gridUnits").Rows[0]
                    .Cells["Status"].Value = "完成";
                Call(form, "ScanUnits");
                Assert(Convert.ToString(Field<DataGridView>(form, "gridUnits").Rows[0]
                    .Cells["Status"].Value) == "完成",
                    "mapping context preserves V4.2.1 unit state cache");
            }
        }
        Console.WriteLine("TOTAL " + count + " PASSED");
        return 0;
        }
        catch (Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                Console.Error.WriteLine("FAIL_TYPE " + current.GetType().FullName);
                Console.Error.WriteLine("FAIL_MESSAGE " + current.Message);
                Console.Error.WriteLine("FAIL_STACK " + current.StackTrace);
                current = current.InnerException;
            }
            return 1;
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
            foreach (string path in createdDeploymentFiles)
                if (File.Exists(path)) File.Delete(path);
            if (createdSkby && Directory.Exists(deploymentSkby))
                Directory.Delete(deploymentSkby, true);
        }
    }
}
