using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Xml;
using Aspose.Cells;
using DataImport;
using D8WataExtract;
using Hydro.Watershed;
using PublicControl;
using SKBYModel;
using SysDAL;
using SysModel;
using System.Text;

internal static class UnifiedHydroWorkflow
{
    private const string DefaultStartTime = "2006/8/1 1:00:00";
    private const string DefaultEndTime = "2006/8/6 0:00:00";
    private const int DefaultIntervalMinutes = 60;
    private const string DefaultSourceStartTime = "2006/8/3 1:00:00";
    private const double ZeroCoverageEpsilon = 1e-12;
    private const string DefaultStationFileName = "站点信息.xlsx";
    private const string DefaultRainfallFileName = "20060803111降雨.xlsx";

    private static DateTime _startTime = DateTime.Parse(DefaultStartTime, CultureInfo.InvariantCulture);
    private static DateTime _endTime = DateTime.Parse(DefaultEndTime, CultureInfo.InvariantCulture);
    private static int _intervalMinutes = DefaultIntervalMinutes;
    private static DateTime _sourceStartTime = DateTime.Parse(DefaultSourceStartTime, CultureInfo.InvariantCulture);
    private static string _stationFileName = DefaultStationFileName;
    private static string _rainfallFileName = DefaultRainfallFileName;
    private static string _inputMapPath = "";
    private static bool _skipPreflight;
    private static readonly Dictionary<string, MappedUnitInput> _mappedUnits =
        new Dictionary<string, MappedUnitInput>(StringComparer.OrdinalIgnoreCase);

    private static string _basePath;
    private static string _projectId;
    private static string _projectName;
    private static string _projectFolder;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 &&
                string.Equals(args[0], "--capabilities", StringComparison.OrdinalIgnoreCase))
            {
                PrintCapabilities();
                return 0;
            }

            if (args.Length < 2)
            {
                PrintUsage();
                return 2;
            }

            string unitsRoot = Path.GetFullPath(args[0]);
            _projectName = args[1].Trim();

            string selector;
            bool prepareOnly;
            bool preflightOnly;
            ParseOptionalArguments(args, out selector, out prepareOnly, out preflightOnly);
            ValidateRuntimeParameters();

            RequireDirectory(unitsRoot);
            if (string.IsNullOrWhiteSpace(_projectName))
                throw new ArgumentException("Project name cannot be empty.");

            if (_inputMapPath.Length > 0)
                LoadInputMapping(unitsRoot, _inputMapPath);

            Console.WriteLine(
                "CONFIG start={0} end={1} interval_min={2} station={3} rainfall={4} source_start={5} source_rows={6}",
                FormatModelTime(_startTime),
                FormatModelTime(_endTime),
                _intervalMinutes,
                _stationFileName,
                _rainfallFileName,
                FormatModelTime(_sourceStartTime),
                GetSourceRowCount());

            List<UnitInput> selected = Directory.GetDirectories(unitsRoot)
                .Select(folder => new UnitInput(folder))
                .Where(unit => selector == "--all" || unit.Name == selector)
                .OrderBy(unit => NaturalUnitNumber(unit.Name))
                .ThenBy(unit => unit.Name, StringComparer.Ordinal)
                .ToList();
            if (selected.Count == 0)
                throw new InvalidOperationException("No unit folder matches the selector.");

            if (_inputMapPath.Length > 0)
            {
                foreach (UnitInput unit in selected)
                {
                    if (!_mappedUnits.ContainsKey(unit.Name))
                        throw new InvalidDataException(
                            "INPUT_MAP_UNIT_MISSING unit=" + unit.Name);
                }

                foreach (UnitInput unit in selected)
                {
                    Console.WriteLine("INPUT_RESOLVED");
                    Console.WriteLine("unit=" + unit.Name);
                    Console.WriteLine("wata=" + unit.Wata);
                    Console.WriteLine("river=" + unit.River);
                    Console.WriteLine("node=" + unit.Node);
                    Console.WriteLine("land=" + unit.LandUse);
                    Console.WriteLine("soil=" + unit.Soil);
                    Console.WriteLine("station=" + unit.StationFile);
                    Console.WriteLine("rainfall=" + unit.RainfallFile);
                }
            }

            var validUnits = new List<UnitInput>();
            var skippedCoverage = new List<string>();

            if (_skipPreflight)
            {
                foreach (UnitInput unit in selected)
                {
                    unit.RequireInputs();
                    validUnits.Add(unit);
                }
                Console.WriteLine("PREFLIGHT_REUSED total=" + validUnits.Count);
            }
            else
            {
                Console.WriteLine("PREFLIGHT_BEGIN total=" + selected.Count);
                foreach (UnitInput unit in selected)
                {
                    try
                    {
                        unit.RequireInputs();
                        Console.WriteLine(
                            "INPUT_FILES unit={0} station={1} rainfall={2}",
                            unit.Name, Path.GetFileName(unit.StationFile),
                            Path.GetFileName(unit.RainfallFile));
                        unit.LandCoverage = CheckCoverage(unit.Wata, unit.LandUse, "XDMDM");
                        unit.SoilCoverage = CheckCoverage(unit.Wata, unit.Soil, "TRZDBM");
                        Console.WriteLine(
                            "COVERAGE unit={0} land_min={1:F6} soil_min={2:F6} " +
                            "land_overall={3:F6} soil_overall={4:F6} " +
                            "land_zero={5} soil_zero={6} wata={7}",
                            unit.Name,
                            unit.LandCoverage.Minimum,
                            unit.SoilCoverage.Minimum,
                            unit.LandCoverage.Overall,
                            unit.SoilCoverage.Overall,
                            unit.LandCoverage.ZeroCoverageCount,
                            unit.SoilCoverage.ZeroCoverageCount,
                            unit.SoilCoverage.WatershedCount);
                        if (unit.LandCoverage.ZeroCoverageCount > 0 ||
                            unit.SoilCoverage.ZeroCoverageCount > 0)
                        {
                            skippedCoverage.Add(
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "{0}: land_zero={1}, soil_zero={2}, " +
                                    "land_overall={3:F6}, soil_overall={4:F6}",
                                    unit.Name,
                                    unit.LandCoverage.ZeroCoverageCount,
                                    unit.SoilCoverage.ZeroCoverageCount,
                                    unit.LandCoverage.Overall,
                                    unit.SoilCoverage.Overall));
                            Console.WriteLine(
                                "SKIPPED_COVERAGE unit={0} land_zero={1} soil_zero={2} " +
                                "land_overall={3:F6} soil_overall={4:F6}",
                                unit.Name,
                                unit.LandCoverage.ZeroCoverageCount,
                                unit.SoilCoverage.ZeroCoverageCount,
                                unit.LandCoverage.Overall,
                                unit.SoilCoverage.Overall);
                            continue;
                        }
                        validUnits.Add(unit);
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine("PREFLIGHT_FAILED unit={0}: {1}", unit.Name, exception.Message);
                    }
                }
                Console.WriteLine("PREFLIGHT_SUMMARY selected={0} valid={1} skipped_coverage={2}",
                    selected.Count, validUnits.Count, skippedCoverage.Count);
                foreach (string item in skippedCoverage)
                    Console.WriteLine("  " + item);
                if (preflightOnly)
                    return validUnits.Count + skippedCoverage.Count == selected.Count ? 0 : 1;
                if (validUnits.Count == 0)
                    throw new InvalidOperationException("No unit passed the coverage preflight.");
            }

            _basePath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            Directory.SetCurrentDirectory(_basePath);
            BackupDatabase();
            ClientConn.Client();
            EnsureProject();

            int success = 0;
            var failures = new List<string>();
            foreach (UnitInput unit in validUnits)
            {
                try
                {
                    ProcessUnit(unit, prepareOnly);
                    success++;
                }
                catch (Exception exception)
                {
                    failures.Add(unit.Name + ": " + exception.Message);
                    Console.Error.WriteLine("FAILED unit={0}\n{1}", unit.Name, exception);
                }
            }

            Console.WriteLine(
                "FINAL_SUMMARY selected={0} coverage_skipped={1} attempted={2} success={3} failed={4} mode={5}",
                selected.Count, skippedCoverage.Count, validUnits.Count, success, failures.Count,
                prepareOnly ? "prepare" : "calculate");
            foreach (string failure in failures)
                Console.Error.WriteLine("  " + failure);
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ProcessUnit(UnitInput unit, bool prepareOnly)
    {
        Console.WriteLine("BEGIN unit=" + unit.Name);

        // 1. Spatial data: create once, otherwise safely reuse.
        ZoneInfo zone = EnsureCalculatingZone(unit);
        BindZone(zone);

        // 2. Flood event can be created/reused independently of station/rainfall import.
        HSCC scenario = GetOrCreateScenario(zone);
        BindScenario(scenario);

        // 3. Analysis data: station and rainfall are checked/imported separately.
        EnsureAnalysisData(unit, zone);

        // 4. Cross-unit upstream boundary sources.
        EnsureAndImportSources(unit, scenario);

        int createResult = SKBYModelClass.CreateInputCSV();
        if (createResult <= 0)
            throw new InvalidOperationException("Model input generation returned " + createResult + ".");
        if (!UpdateGroovyParams.UpdateTime())
            throw new InvalidOperationException("Cannot update model time parameters.");
        Console.WriteLine("PREPARED unit={0} zone={1} hscc={2} create={3}",
            unit.Name, zone.Id, scenario.hsccid, createResult);
        if (prepareOnly)
            return;

        RunModelEXE.RunModelOnlyFile(scenario.name);
        RunModelEXE.RunModel();
        string outlet = Path.Combine(
            _basePath, "SKBYEXE", "output", scenario.name, "out", "outlet.csv");
        if (!File.Exists(outlet) || new FileInfo(outlet).Length == 0)
            throw new InvalidOperationException("Model ended without a valid outlet.csv: " + outlet);
        Console.WriteLine("CALCULATED unit={0} outlet={1}", unit.Name, outlet);
    }

    private static ZoneInfo EnsureCalculatingZone(UnitInput unit)
    {
        List<ZoneInfo> matches = GetProjectZones()
            .Where(candidate => candidate.Name == unit.Name)
            .ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException("Duplicate calculating zones exist: " + unit.Name);
        if (matches.Count == 1)
        {
            BindZone(matches[0]);
            if (HookHelper.WataList.Count == 0 || HookHelper.RivlList.Count == 0 ||
                HookHelper.NodeList.Count == 0)
                throw new InvalidOperationException(
                    "An existing calculating zone is incomplete. Restore the automatic database backup before retrying.");
            Console.WriteLine("UNIT_REUSED unit={0} zone={1}", unit.Name, matches[0].Id);
            return matches[0];
        }

        string unitFolderName = SafeFolderName(unit.Name.ToLowerInvariant());
        string targetFolder = Path.Combine(
            _basePath, "Project", _projectFolder, unitFolderName);
        Directory.CreateDirectory(targetFolder);
        CopyShape(unit.Wata, targetFolder, "Wata");
        CopyShape(unit.River, targetFolder, "Rivl");
        CopyShape(unit.Node, targetFolder, "Node");
        CopyShape(unit.LandUse, targetFolder, "USLU");
        CopyShape(unit.Soil, targetFolder, "SLTA");

        int zoneId = IngenieurinformatikDAL.InsertCalculatingZone(
            _projectId, unit.Name, unitFolderName);
        if (zoneId <= 0)
            throw new InvalidOperationException("Cannot create calculating zone: " + unit.Name);
        var zone = new ZoneInfo(zoneId.ToString(), unit.Name, unitFolderName, true);
        SetZoneIdentity(zone);

        HookHelper.WataList.Clear();
        HookHelper.RivlList.Clear();
        HookHelper.NodeList.Clear();
        HookHelper.USLUList.Clear();
        HookHelper.SLTAList.Clear();
        HookHelper.WataParamDefaultList.Clear();

        string wataLayerName = Path.GetFileNameWithoutExtension(unit.Wata);
        string riverLayerName = Path.GetFileNameWithoutExtension(unit.River);
        string nodeLayerName = Path.GetFileNameWithoutExtension(unit.Node);
        Console.WriteLine(
            "SHP_IMPORT_LAYER unit={0} wata={1} river={2} node={3}",
            unit.Name, wataLayerName, riverLayerName, nodeLayerName);

        SHPImport.ReadWata(unit.Wata, wataLayerName, 1);
        SHPImport.ReadRivl(unit.River, riverLayerName, 1);
        SHPImport.ReadNode(unit.Node, nodeLayerName, 1);
        SHPImport.CalculateLagTime();

        var extract = new WataExtract();
        extract.GeoStatistic(unit.Wata, unit.LandUse, "WSCD", "XDMDM", "uslu");
        extract.GeoStatistic(unit.Wata, unit.Soil, "WSCD", "TRZDBM", "slta");

        SHPImport.InsertData(zone.Id);
        USLUManagerDAL.InsertUSLUData(HookHelper.USLUList.ToList(), zone.Id);
        SLTAManagerDAL.InsertSLTAData(HookHelper.SLTAList.ToList(), zone.Id);
        USLUManagerDAL.SetWataUSLUSLTA(zone.Id);
        USLUManagerDAL.SetWataResponseUnits(zone.Id);
        extract.CalculateUSLUArea();
        extract.CalculateSLTAArea();
        WataManagerDAL.InsertWataParamDefault(
            HookHelper.WataParamDefaultList.ToList(), zone.Id);

        Console.WriteLine(
            "UNIT_IMPORTED unit={0} zone={1} wata={2} river={3} node={4} land={5} soil={6}",
            unit.Name, zone.Id, HookHelper.WataList.Count, HookHelper.RivlList.Count,
            HookHelper.NodeList.Count, HookHelper.USLUList.Count, HookHelper.SLTAList.Count);
        return zone;
    }

    private static void EnsureAnalysisData(UnitInput unit, ZoneInfo zone)
    {
        EnsureStationData(unit, zone);
        EnsureRainfallData(unit, zone);

        Console.WriteLine(
            "ANALYSIS_DATA_READY unit={0} station={1} rainfall={2}",
            unit.Name,
            Path.GetFileName(unit.StationFile),
            Path.GetFileName(unit.RainfallFile));
    }

    private static void EnsureStationData(UnitInput unit, ZoneInfo zone)
    {
        HookHelper.SiteList.Clear();
        HookHelper.SiteList = SiteManagerDAL.SearchSite(zone.Id, "");

        if (HookHelper.SiteList.Count > 0)
        {
            Console.WriteLine(
                "STATION_REUSED unit={0} count={1}",
                unit.Name, HookHelper.SiteList.Count);
            return;
        }

        ExcelImport.ReadYLZExcel(unit.StationFile);
        SiteManagerDAL.InsertSite(ExcelImport.SiteValueList, zone.Id);

        HookHelper.SiteList.Clear();
        HookHelper.SiteList = SiteManagerDAL.SearchSite(zone.Id, "");
        if (HookHelper.SiteList.Count == 0)
            throw new InvalidOperationException(
                "Station import completed but no station exists in the database: " + unit.Name);

        Console.WriteLine(
            "STATION_IMPORTED unit={0} file={1} count={2}",
            unit.Name,
            Path.GetFileName(unit.StationFile),
            HookHelper.SiteList.Count);
    }

    private static void EnsureRainfallData(UnitInput unit, ZoneInfo zone)
    {
        var rainfall = new List<YlZRainfall>();

        // A newly-created unit cannot already contain rainfall, so skip the
        // original model's empty-rainfall query to avoid its confirmation dialog.
        if (!zone.IsNew)
        {
            if (SKBYModelClass.QueryYLZData(rainfall))
            {
                Console.WriteLine(
                    "RAINFALL_REUSED unit={0} rows={1}",
                    unit.Name, rainfall.Count);
                return;
            }
        }

        ExcelImport.ReadYLZRainfallExcel(unit.RainfallFile);
        SiteManagerDAL.InsertYLZRainfall(
            ExcelImport.YLZRainfallList, "1", zone.Id);

        rainfall.Clear();
        if (!SKBYModelClass.QueryYLZData(rainfall))
            throw new InvalidOperationException(
                "Rainfall import completed but no rainfall exists in the flood-event time range: " +
                unit.Name);

        Console.WriteLine(
            "RAINFALL_IMPORTED unit={0} file={1} rows={2}",
            unit.Name,
            Path.GetFileName(unit.RainfallFile),
            rainfall.Count);
    }

    private static HSCC GetOrCreateScenario(ZoneInfo zone)
    {
        List<HSCC> matches = FloodCalculateDAL.GetHSCCAll()
            .Where(value => value.czone == zone.Id && value.name == zone.Name)
            .ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException("Duplicate flood events exist for this unit.");
        if (matches.Count == 1)
        {
            ValidateScenario(matches[0]);
            return matches[0];
        }
        if (HookHelper.NodeList.Count == 0)
            throw new InvalidOperationException("The unit has no node for selecting the outlet.");

        var scenario = new HSCC
        {
            name = zone.Name,
            starttime = FormatModelTime(_startTime),
            endtime = FormatModelTime(_endTime),
            meteo_starttime = "",
            meteo_endtime = "",
            timeinterval = _intervalMinutes.ToString(CultureInfo.InvariantCulture),
            ylinterval = _intervalMinutes.ToString(CultureInfo.InvariantCulture),
            llinterval = _intervalMinutes.ToString(CultureInfo.InvariantCulture),
            Localtion = HookHelper.NodeList.Last().ndcd,
            jylType = 0
        };
        int hsccId = FloodCalculateDAL.InsertHSCC(scenario, zone.Id);
        if (hsccId <= 0)
            throw new InvalidOperationException("Cannot insert flood event.");
        RequireModelInsert(FloodCalculateDAL.InsertHSCCWata(hsccId, zone.Id), "HSCC wata");
        RequireModelInsert(FloodCalculateDAL.InsertHSCCRivl(hsccId, zone.Id), "HSCC river");
        RequireModelInsert(FloodCalculateDAL.InsertHSCCWataParam(hsccId), "HSCC wata parameters");
        RequireModelInsert(FloodCalculateDAL.InsertHSCCRivlParam(hsccId), "HSCC river parameters");
        RequireModelInsert(SKBYFloodCalculateDAL.InsertSKBYHSCCWata(hsccId), "SKBY wata");
        RequireModelInsert(SKBYFloodCalculateDAL.InsertSKBYHSCCRivl(hsccId), "SKBY river");
        RequireModelInsert(
            SKBYFloodCalculateDAL.InsertSKBYHSCCWataParam(hsccId, zone.Id),
            "SKBY wata parameters");
        RequireModelInsert(
            SKBYFloodCalculateDAL.InsertSKBYHSCCRivlParam(hsccId, zone.Id),
            "SKBY river parameters");
        scenario.hsccid = hsccId.ToString();
        scenario.czone = zone.Id;
        Console.WriteLine("EVENT_CREATED unit={0} hscc={1} outlet={2}",
            zone.Name, scenario.hsccid, scenario.Localtion);
        return scenario;
    }

    private static void EnsureAndImportSources(UnitInput unit, HSCC scenario)
    {
        var riverCodes = new HashSet<string>(
            HookHelper.RivlList.Select(river => river.rvcd), StringComparer.Ordinal);
        var inputs = HookHelper.RivlList
            .Select(river => new InputSegment(
                river.rvcd,
                SplitCodes(river.frvcd)
                    .Where(code => code != "-1" && !riverCodes.Contains(code))
                    .Distinct(StringComparer.Ordinal)
                    .ToList()))
            .Where(value => value.UpstreamCodes.Count != 0)
            .ToList();

        foreach (InputSegment input in inputs)
        {
            int expectedRows = input.UpstreamCodes.Count * GetSourceRowCount();

            // 1. The workbook is the file-side source of truth. Always make sure
            //    it exists and matches the current source-start / interval / codes.
            string sourceFile = Path.Combine(
                unit.Folder, input.RiverCode + "上游水源数据表.xls");

            EnsureSourceWorkbook(sourceFile, input.UpstreamCodes);

            // 2. Compare the current database source against the validated workbook.
            //    Reuse only when both the source definition and every flow row match.
            int databaseRows;
            string databaseReason;
            if (DatabaseSourceMatchesWorkbook(
                scenario.hsccid,
                input.RiverCode,
                input.UpstreamCodes,
                sourceFile,
                out databaseRows,
                out databaseReason))
            {
                Console.WriteLine(
                    "SOURCE_REUSED river={0} rows={1}",
                    input.RiverCode,
                    databaseRows);
                continue;
            }

            // 3. If the database contains an old/incomplete/different source for
            //    this HSCC + RVCD, remove only that source and its flow rows.
            int removedSources = DeleteExistingDatabaseSource(
                scenario.hsccid,
                input.RiverCode);

            if (removedSources > 0)
            {
                Console.WriteLine(
                    "SOURCE_DB_REPLACED river={0} old_sources={1} reason={2}",
                    input.RiverCode,
                    removedSources,
                    databaseReason);
            }
            else
            {
                Console.WriteLine(
                    "SOURCE_DB_MISSING river={0} reason={1}",
                    input.RiverCode,
                    databaseReason);
            }

            // 4. Import the validated workbook into a fresh database source.
            var source = new WaterSource
            {
                rvcd = input.RiverCode,
                frvcd = string.Join(",", input.UpstreamCodes),
                starttime = "",
                endtime = "",
                name = input.RiverCode + "Source",
                hsccid = scenario.hsccid
            };

            int sourceId = WaterSourceManagerDAL.InsertSKBYWaterSource(source);
            if (sourceId <= 0)
                throw new InvalidOperationException(
                    "Cannot insert upstream source for " + input.RiverCode + ".");

            ExcelImport.ReadSKBYWaterSourceExcel(sourceFile);
            int inserted = WaterSourceManagerDAL.InsertSKBYSourceFlow(
                ExcelImport.SourceFlowList, sourceId);

            if (inserted != expectedRows)
                throw new InvalidOperationException(string.Format(
                    "Unexpected source row count for {0}: inserted={1}, expected={2}",
                    input.RiverCode, inserted, expectedRows));

            Console.WriteLine(
                "SOURCE_IMPORTED river={0} upstream={1} rows={2}",
                input.RiverCode,
                string.Join(",", input.UpstreamCodes),
                inserted);
        }
    }

    private static void EnsureSourceWorkbook(
        string sourceFile,
        List<string> upstreamCodes)
    {
        if (!File.Exists(sourceFile))
        {
            CreateSourceWorkbook(sourceFile, upstreamCodes);
            Console.WriteLine("SOURCE_FILE_CREATED " + sourceFile);
            return;
        }

        string validationError;
        if (ValidateSourceWorkbook(
            sourceFile,
            upstreamCodes,
            out validationError))
        {
            Console.WriteLine("SOURCE_FILE_VALID " + sourceFile);
            return;
        }

        Console.WriteLine(
            "SOURCE_FILE_INVALID file={0} reason={1}",
            sourceFile,
            validationError);

        File.Delete(sourceFile);
        Console.WriteLine("SOURCE_FILE_DELETED " + sourceFile);

        CreateSourceWorkbook(sourceFile, upstreamCodes);
        Console.WriteLine("SOURCE_FILE_RECREATED " + sourceFile);

        // Defensive verification: a workbook generated by this program must
        // itself pass the same validator before it is imported.
        if (!ValidateSourceWorkbook(
            sourceFile,
            upstreamCodes,
            out validationError))
            throw new InvalidOperationException(
                "The recreated upstream source workbook is still invalid: " +
                validationError);
    }

    private static bool DatabaseSourceMatchesWorkbook(
        string hsccId,
        string riverCode,
        List<string> upstreamCodes,
        string sourceFile,
        out int databaseRows,
        out string reason)
    {
        databaseRows = 0;
        reason = "";

        string safeHscc = SqlQuote(hsccId);
        string safeRiver = SqlQuote(riverCode);

        DataTable sourceTable = PublicDAL.GetData(
            "select ID, FRVCD from SKBY_nsource " +
            "where HSCCID='" + safeHscc + "' " +
            "and RVCD='" + safeRiver + "' order by ID");

        if (sourceTable.Rows.Count == 0)
        {
            reason = "No database source exists for the current HSCC and river.";
            return false;
        }

        if (sourceTable.Rows.Count != 1)
        {
            reason = "Duplicate database source definitions exist: " +
                     sourceTable.Rows.Count;
            return false;
        }

        int sourceId = Convert.ToInt32(sourceTable.Rows[0]["ID"]);
        string databaseFrvcd = Convert.ToString(
            sourceTable.Rows[0]["FRVCD"]);

        var databaseCodes = new HashSet<string>(
            SplitCodes(databaseFrvcd),
            StringComparer.Ordinal);

        var expectedCodes = new HashSet<string>(
            upstreamCodes,
            StringComparer.Ordinal);

        if (!databaseCodes.SetEquals(expectedCodes))
        {
            reason = "Database upstream river codes do not match the workbook.";
            return false;
        }

        List<SourceRecord> workbookRecords =
            ReadSourceWorkbookRecords(sourceFile);

        DataTable flowTable = PublicDAL.GetData(
            "select Code, dtime, Val from SKBY_nsource_LL " +
            "where SourceID=" +
            sourceId.ToString(CultureInfo.InvariantCulture) +
            " order by ID");

        databaseRows = flowTable.Rows.Count;

        if (databaseRows != workbookRecords.Count)
        {
            reason = string.Format(
                "Database/workbook row count mismatch: database={0}, workbook={1}.",
                databaseRows,
                workbookRecords.Count);
            return false;
        }

        var workbookMap = new Dictionary<string, double>(
            StringComparer.Ordinal);

        foreach (SourceRecord record in workbookRecords)
        {
            string key = SourceRecordKey(record.Code, record.Time);

            if (workbookMap.ContainsKey(key))
            {
                reason = "Duplicate code/time exists in the workbook.";
                return false;
            }

            workbookMap.Add(key, record.Flow);
        }

        var databaseKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (DataRow row in flowTable.Rows)
        {
            string code = Convert.ToString(row["Code"]).Trim();

            DateTime time;
            if (!TryGetCellDateTime(row["dtime"], out time))
            {
                reason = "Database contains an invalid source time.";
                return false;
            }

            double flow;
            if (!TryGetCellDouble(row["Val"], out flow))
            {
                reason = "Database contains an invalid source flow value.";
                return false;
            }

            string key = SourceRecordKey(code, NormalizeSourceTime(time));

            if (!databaseKeys.Add(key))
            {
                reason = "Database contains a duplicate code/time source row.";
                return false;
            }

            double expectedFlow;
            if (!workbookMap.TryGetValue(key, out expectedFlow))
            {
                reason = "Database contains a source code/time that is not in the workbook: " +
                         key;
                return false;
            }

            if (Math.Abs(flow - expectedFlow) > 0.0000001)
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "Database/workbook flow mismatch at {0}: database={1}, workbook={2}.",
                    key,
                    flow,
                    expectedFlow);
                return false;
            }
        }

        if (databaseKeys.Count != workbookMap.Count)
        {
            reason = "Database is missing one or more workbook source rows.";
            return false;
        }

        reason = "Database source matches the validated workbook.";
        return true;
    }

    private static List<SourceRecord> ReadSourceWorkbookRecords(
        string sourceFile)
    {
        var records = new List<SourceRecord>();
        var workbook = new Workbook(sourceFile);

        if (workbook.Worksheets.Count == 0)
            throw new InvalidOperationException(
                "Upstream source workbook has no worksheet: " + sourceFile);

        Cells cells = workbook.Worksheets[0].Cells;
        int maxRow = cells.MaxDataRow;

        for (int row = 2; row <= maxRow; row++)
        {
            string code = Convert.ToString(cells[row, 0].Value).Trim();
            object timeValue = cells[row, 1].Value;
            object flowValue = cells[row, 2].Value;

            bool emptyRow =
                code.Length == 0 &&
                (timeValue == null ||
                 Convert.ToString(timeValue).Trim().Length == 0) &&
                (flowValue == null ||
                 Convert.ToString(flowValue).Trim().Length == 0);

            if (emptyRow)
                continue;

            DateTime time;
            if (!TryGetCellDateTime(timeValue, out time))
                throw new InvalidOperationException(
                    "Invalid source time at Excel row " + (row + 1) + ".");

            double flow;
            if (!TryGetCellDouble(flowValue, out flow))
                throw new InvalidOperationException(
                    "Invalid source flow at Excel row " + (row + 1) + ".");

            records.Add(new SourceRecord(
                code,
                NormalizeSourceTime(time),
                flow));
        }

        return records;
    }

    private static int DeleteExistingDatabaseSource(
        string hsccId,
        string riverCode)
    {
        string safeHscc = SqlQuote(hsccId);
        string safeRiver = SqlQuote(riverCode);

        DataTable sources = PublicDAL.GetData(
            "select ID from SKBY_nsource " +
            "where HSCCID='" + safeHscc + "' " +
            "and RVCD='" + safeRiver + "'");

        if (sources.Rows.Count == 0)
            return 0;

        var sourceIds = sources.Rows
            .Cast<DataRow>()
            .Select(row => Convert.ToInt32(row["ID"]))
            .Distinct()
            .ToList();

        PublicDAL.BeginTransaction();
        try
        {
            foreach (int sourceId in sourceIds)
            {
                PublicDAL.ExecuteSql(
                    "delete from SKBY_nsource_LL where SourceID=" +
                    sourceId.ToString(CultureInfo.InvariantCulture));
            }

            PublicDAL.ExecuteSql(
                "delete from SKBY_nsource " +
                "where HSCCID='" + safeHscc + "' " +
                "and RVCD='" + safeRiver + "'");

            PublicDAL.CommitTransacton();
            return sourceIds.Count;
        }
        catch
        {
            PublicDAL.RollbackTransaction();
            throw;
        }
    }

    private static DateTime NormalizeSourceTime(DateTime value)
    {
        return new DateTime(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            value.Minute,
            value.Second);
    }

    private static string SourceRecordKey(
        string code,
        DateTime time)
    {
        return (code ?? "").Trim() + "|" +
               NormalizeSourceTime(time).ToString(
                   "yyyyMMddHHmmss",
                   CultureInfo.InvariantCulture);
    }

    private static string SqlQuote(string value)
    {
        return (value ?? "").Replace("'", "''");
    }

    private static bool ValidateSourceWorkbook(
        string sourceFile,
        List<string> upstreamCodes,
        out string error)
    {
        error = "";

        try
        {
            var workbook = new Workbook(sourceFile);
            if (workbook.Worksheets.Count == 0)
            {
                error = "Workbook has no worksheet.";
                return false;
            }

            Worksheet sheet = workbook.Worksheets[0];
            Cells cells = sheet.Cells;

            string headerCode = Convert.ToString(cells[1, 0].Value).Trim();
            string headerTime = Convert.ToString(cells[1, 1].Value).Trim();
            string headerFlow = Convert.ToString(cells[1, 2].Value).Trim();

            if (headerCode != "上游河道编码" ||
                headerTime != "时间" ||
                headerFlow != "流量值")
            {
                error = "Header does not match the expected source-workbook format.";
                return false;
            }

            int expectedPerCode = GetSourceRowCount();
            int expectedTotal = upstreamCodes.Count * expectedPerCode;

            var expectedCodes = new HashSet<string>(
                upstreamCodes,
                StringComparer.Ordinal);

            var timesByCode = upstreamCodes.ToDictionary(
                code => code,
                code => new HashSet<long>(),
                StringComparer.Ordinal);

            int actualRows = 0;
            int maxRow = cells.MaxDataRow;

            for (int row = 2; row <= maxRow; row++)
            {
                string code = Convert.ToString(cells[row, 0].Value).Trim();
                object timeValue = cells[row, 1].Value;
                object flowValue = cells[row, 2].Value;

                bool emptyRow =
                    code.Length == 0 &&
                    (timeValue == null || Convert.ToString(timeValue).Trim().Length == 0) &&
                    (flowValue == null || Convert.ToString(flowValue).Trim().Length == 0);

                if (emptyRow)
                    continue;

                actualRows++;

                if (!expectedCodes.Contains(code))
                {
                    error = "Unexpected upstream river code at row " +
                            (row + 1) + ": " + code;
                    return false;
                }

                DateTime time;
                if (!TryGetCellDateTime(timeValue, out time))
                {
                    error = "Invalid time at row " + (row + 1) + ".";
                    return false;
                }

                double flow;
                if (!TryGetCellDouble(flowValue, out flow))
                {
                    error = "Invalid flow value at row " + (row + 1) + ".";
                    return false;
                }

                DateTime normalized = new DateTime(
                    time.Year,
                    time.Month,
                    time.Day,
                    time.Hour,
                    time.Minute,
                    0);

                double minutesFromStart =
                    (normalized - _sourceStartTime).TotalMinutes;

                if (minutesFromStart < 0 ||
                    minutesFromStart % _intervalMinutes != 0)
                {
                    error = "Time is outside the expected grid at row " +
                            (row + 1) + ".";
                    return false;
                }

                int timeIndex =
                    (int)(minutesFromStart / _intervalMinutes);

                if (timeIndex < 0 || timeIndex >= expectedPerCode)
                {
                    error = "Time is outside the requested source period at row " +
                            (row + 1) + ".";
                    return false;
                }

                if (!timesByCode[code].Add(normalized.Ticks))
                {
                    error = "Duplicate time for upstream code " + code +
                            " at row " + (row + 1) + ".";
                    return false;
                }
            }

            if (actualRows != expectedTotal)
            {
                error = string.Format(
                    "Row count mismatch: actual={0}, expected={1}.",
                    actualRows,
                    expectedTotal);
                return false;
            }

            foreach (string code in upstreamCodes)
            {
                if (timesByCode[code].Count != expectedPerCode)
                {
                    error = string.Format(
                        "Time-step count mismatch for {0}: actual={1}, expected={2}.",
                        code,
                        timesByCode[code].Count,
                        expectedPerCode);
                    return false;
                }

                for (int index = 0; index < expectedPerCode; index++)
                {
                    DateTime expectedTime =
                        _sourceStartTime.AddMinutes(
                            (double)index * _intervalMinutes);

                    if (!timesByCode[code].Contains(expectedTime.Ticks))
                    {
                        error = "Missing time for " + code + ": " +
                                FormatModelTime(expectedTime);
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryGetCellDateTime(
        object value,
        out DateTime result)
    {
        result = DateTime.MinValue;

        if (value == null)
            return false;

        if (value is DateTime)
        {
            result = (DateTime)value;
            return true;
        }

        if (value is double)
        {
            try
            {
                result = DateTime.FromOADate((double)value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        string text = Convert.ToString(value).Trim();

        return DateTime.TryParse(
                   text,
                   CultureInfo.CurrentCulture,
                   DateTimeStyles.None,
                   out result) ||
               DateTime.TryParse(
                   text,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out result);
    }

    private static bool TryGetCellDouble(
        object value,
        out double result)
    {
        result = 0;

        if (value == null)
            return false;

        try
        {
            result = Convert.ToDouble(
                value,
                CultureInfo.InvariantCulture);

            return !double.IsNaN(result) &&
                   !double.IsInfinity(result);
        }
        catch
        {
            string text = Convert.ToString(value).Trim();

            return double.TryParse(
                       text,
                       NumberStyles.Float,
                       CultureInfo.CurrentCulture,
                       out result) ||
                   double.TryParse(
                       text,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out result);
        }
    }

    private static void CreateSourceWorkbook(string output, List<string> upstreamCodes)
    {
        var workbook = new Workbook();
        Worksheet sheet = workbook.Worksheets[0];
        sheet.Name = "SKBY上游水源数据表";
        Cells cells = sheet.Cells;
        cells.Merge(0, 0, 1, 3);
        cells[0, 0].PutValue("SKBY上游水源数据表");
        cells[1, 0].PutValue("上游河道编码");
        cells[1, 1].PutValue("时间");
        cells[1, 2].PutValue("流量值");
        int row = 2;
        int sourceRows = GetSourceRowCount();
        foreach (string upstreamCode in upstreamCodes)
        {
            for (int index = 0; index < sourceRows; index++, row++)
            {
                cells[row, 0].PutValue(upstreamCode);
                cells[row, 1].PutValue(
                    _sourceStartTime.AddMinutes((double)index * _intervalMinutes));
                cells[row, 2].PutValue(2);
                Style dateStyle = cells[row, 1].GetStyle();
                dateStyle.Custom = "yyyy-mm-dd hh:mm:ss";
                cells[row, 1].SetStyle(dateStyle);
            }
        }
        workbook.Save(output, SaveFormat.Excel97To2003);
    }

    private sealed class DbfFieldDefinition
    {
        public string Name;
        public int Offset;
        public int Length;
    }


    private static int ReadBigEndianInt32(
        BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);

        if (bytes.Length != 4)
            throw new EndOfStreamException();

        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return BitConverter.ToInt32(bytes, 0);
    }


    private static List<double> ReadPolygonAreas(
        string shpFile)
    {
        var result = new List<double>();

        using (var stream = new FileStream(
            shpFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite))
        using (var reader = new BinaryReader(stream))
        {
            if (stream.Length < 100)
                throw new InvalidDataException(
                    "Invalid SHP file: " + shpFile);

            // SHP file header = 100 bytes.
            stream.Position = 100;

            while (stream.Position + 8 <= stream.Length)
            {
                // Record number.
                ReadBigEndianInt32(reader);

                // Record content length, unit = 16-bit words.
                int contentLengthWords =
                    ReadBigEndianInt32(reader);

                if (contentLengthWords <= 0)
                    throw new InvalidDataException(
                        "Invalid SHP record length.");

                long recordEnd =
                    stream.Position +
                    contentLengthWords * 2L;

                if (recordEnd > stream.Length)
                    throw new InvalidDataException(
                        "SHP record exceeds file length.");

                int shapeType =
                    reader.ReadInt32();

                // Null Shape.
                if (shapeType == 0)
                {
                    result.Add(0.0);
                    stream.Position = recordEnd;
                    continue;
                }

                // Polygon / PolygonZ / PolygonM.
                if (shapeType != 5 &&
                    shapeType != 15 &&
                    shapeType != 25)
                {
                    throw new InvalidDataException(
                        "Expected polygon SHP but found shape type " +
                        shapeType.ToString(
                            CultureInfo.InvariantCulture) +
                        ".");
                }

                // Bounding box.
                reader.ReadDouble();
                reader.ReadDouble();
                reader.ReadDouble();
                reader.ReadDouble();

                int partCount =
                    reader.ReadInt32();

                int pointCount =
                    reader.ReadInt32();

                if (partCount <= 0 ||
                    pointCount <= 0)
                {
                    result.Add(0.0);
                    stream.Position = recordEnd;
                    continue;
                }

                var parts =
                    new int[partCount];

                for (int index = 0;
                    index < partCount;
                    index++)
                {
                    parts[index] =
                        reader.ReadInt32();
                }

                var x =
                    new double[pointCount];

                var y =
                    new double[pointCount];

                for (int index = 0;
                    index < pointCount;
                    index++)
                {
                    x[index] =
                        reader.ReadDouble();

                    y[index] =
                        reader.ReadDouble();
                }

                double signedArea = 0.0;

                for (int partIndex = 0;
                    partIndex < partCount;
                    partIndex++)
                {
                    int start =
                        parts[partIndex];

                    int end =
                        partIndex + 1 < partCount
                            ? parts[partIndex + 1]
                            : pointCount;

                    if (end - start < 3)
                        continue;

                    double ringArea = 0.0;

                    for (int index = start;
                        index < end;
                        index++)
                    {
                        int next =
                            index + 1 < end
                                ? index + 1
                                : start;

                        ringArea +=
                            x[index] * y[next] -
                            x[next] * y[index];
                    }

                    signedArea +=
                        ringArea * 0.5;
                }

                result.Add(
                    Math.Abs(signedArea));

                // PolygonZ / PolygonM still contains
                // Z/M arrays after XY; skip them.
                stream.Position = recordEnd;
            }
        }

        return result;
    }


    private static List<string> ReadDbfFieldValues(
        string dbfFile,
        string requestedField)
    {
        var values =
            new List<string>();

        using (var stream = new FileStream(
            dbfFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite))
        using (var reader = new BinaryReader(stream))
        {
            byte[] header =
                reader.ReadBytes(32);

            if (header.Length != 32)
                throw new InvalidDataException(
                    "Invalid DBF header: " + dbfFile);

            int recordCount =
                header[4] |
                (header[5] << 8) |
                (header[6] << 16) |
                (header[7] << 24);

            int headerLength =
                header[8] |
                (header[9] << 8);

            int recordLength =
                header[10] |
                (header[11] << 8);

            var fields =
                new List<DbfFieldDefinition>();

            int fieldOffset = 1;

            while (stream.Position < headerLength)
            {
                int marker =
                    stream.ReadByte();

                if (marker < 0 ||
                    marker == 0x0D)
                {
                    break;
                }

                stream.Position--;

                byte[] descriptor =
                    reader.ReadBytes(32);

                if (descriptor.Length != 32)
                    throw new InvalidDataException(
                        "Invalid DBF field descriptor.");

                int nameLength = 0;

                while (nameLength < 11 &&
                    descriptor[nameLength] != 0)
                {
                    nameLength++;
                }

                string name =
                    Encoding.ASCII.GetString(
                        descriptor,
                        0,
                        nameLength).Trim();

                int length =
                    descriptor[16];

                fields.Add(
                    new DbfFieldDefinition
                    {
                        Name = name,
                        Offset = fieldOffset,
                        Length = length
                    });

                fieldOffset += length;
            }

            DbfFieldDefinition target =
                fields.FirstOrDefault(
                    field =>
                        string.Equals(
                            field.Name,
                            requestedField,
                            StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                throw new InvalidDataException(
                    "DBF field not found: " +
                    requestedField);
            }

            stream.Position =
                headerLength;

            for (int recordIndex = 0;
                recordIndex < recordCount;
                recordIndex++)
            {
                byte[] record =
                    reader.ReadBytes(
                        recordLength);

                if (record.Length != recordLength)
                    throw new InvalidDataException(
                        "Unexpected end of DBF file.");

                // Deleted DBF record.
                if (record[0] == 0x2A)
                {
                    values.Add("");
                    continue;
                }

                string value =
                    Encoding.ASCII.GetString(
                        record,
                        target.Offset,
                        target.Length).Trim();

                values.Add(value);
            }
        }

        return values;
    }


    private static Dictionary<string, double>
        ReadWatershedAreasByCode(
            string watershedFile)
    {
        string dbfFile =
            Path.ChangeExtension(
                watershedFile,
                ".dbf");

        RequireFile(watershedFile);
        RequireFile(dbfFile);

        List<double> areas =
            ReadPolygonAreas(
                watershedFile);

        List<string> codes =
            ReadDbfFieldValues(
                dbfFile,
                "WSCD");

        if (areas.Count != codes.Count)
        {
            throw new InvalidDataException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "SHP/DBF feature count mismatch: " +
                    "shp={0}, dbf={1}.",
                    areas.Count,
                    codes.Count));
        }

        var result =
            new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);

        for (int index = 0;
            index < areas.Count;
            index++)
        {
            string code =
                (codes[index] ?? "").Trim();

            if (code.Length == 0)
                continue;

            double area =
                areas[index];

            if (area <= 0.0 ||
                double.IsNaN(area) ||
                double.IsInfinity(area))
            {
                continue;
            }

            double existing;

            if (result.TryGetValue(
                    code,
                    out existing))
            {
                result[code] =
                    existing + area;
            }
            else
            {
                result.Add(
                    code,
                    area);
            }
        }

        return result;
    }

    private static CoverageResult CheckCoverage(
        string watershedFile,
        string thematicFile,
        string thematicField)
    {
        var split =
            new WatershedSplit();

        try
        {
            if (!split.DoGeoStatistic(
                    watershedFile,
                    thematicFile,
                    "WSCD",
                    thematicField))
            {
                throw new InvalidOperationException(
                    "Coverage calculation failed: " +
                    split.LastError);
            }

            // 读取 Wata.shp 中全部子流域及其面积。
            // 整个计算单元的全部面积都必须进入总体覆盖率的分母。
            Dictionary<string, double> areaByCode =
                ReadWatershedAreasByCode(
                    watershedFile);

            if (areaByCode.Count == 0)
            {
                throw new InvalidOperationException(
                    "No valid watershed polygons were found.");
            }

            // DoGeoStatistic 返回的是实际产生专题统计结果的子流域。
            int statCount =
                split.GetStatWatershedNum();

            var coverageByCode =
                new Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase);

            for (int index = 0;
                index < statCount;
                index++)
            {
                SubWatershedLandAttr item =
                    split.GetSubWatershedLandAttr(
                        index);

                string shapeCode =
                    Convert.ToString(
                        item.ShapeCode).Trim();

                if (shapeCode.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Coverage statistic contains an empty WSCD.");
                }

                if (!areaByCode.ContainsKey(shapeCode))
                {
                    throw new InvalidOperationException(
                        "Coverage statistic cannot match Wata WSCD=" +
                        shapeCode);
                }

                double coverage =
                    item.LandAttrAndRatio
                        .Values
                        .Sum();

                // 防止浮点误差造成略小于0或略大于1。
                if (coverage < 0.0)
                    coverage = 0.0;

                if (coverage > 1.0)
                    coverage = 1.0;

                if (coverageByCode.ContainsKey(shapeCode))
                {
                    throw new InvalidOperationException(
                        "Duplicate coverage statistic WSCD=" +
                        shapeCode);
                }

                coverageByCode.Add(
                    shapeCode,
                    coverage);
            }

            double totalArea = 0.0;
            double coveredArea = 0.0;
            double minimum = double.MaxValue;

            int matchedCount = 0;
            int zeroCoverageCount = 0;

            // 关键修正：
            // 遍历全部 Wata 子流域。
            foreach (KeyValuePair<string, double> pair
                    in areaByCode)
            {
                string watershedCode =
                    pair.Key;

                double area =
                    pair.Value;

                double coverage;

                if (coverageByCode.TryGetValue(
                        watershedCode,
                        out coverage))
                {
                    matchedCount++;
                }
                else
                {
                    // Wata 中存在，但专题图层没有任何统计结果：
                    // 该子流域覆盖率视为 0%。
                    coverage = 0.0;
                }

                if (coverage <= ZeroCoverageEpsilon)
                    zeroCoverageCount++;

                totalArea += area;

                coveredArea +=
                    area * coverage;

                minimum =
                    Math.Min(
                        minimum,
                        coverage);
            }

            if (totalArea <= 0.0)
            {
                throw new InvalidOperationException(
                    "Total watershed area is zero.");
            }

            if (minimum == double.MaxValue)
                minimum = 0.0;

            double overall =
                coveredArea /
                totalArea;

            Console.WriteLine(
                "COVERAGE_DETAIL field={0} total_wata={1} stat_wata={2} matched={3} zero={4}",
                thematicField,
                areaByCode.Count,
                statCount,
                matchedCount,
                zeroCoverageCount);

            return new CoverageResult(
                minimum,
                overall,
                areaByCode.Count,
                zeroCoverageCount);
        }
        finally
        {
            split.Release();
        }
    }

    private static void EnsureProject()
    {
        DataTable projects = IngenieurinformatikDAL.GetGCXX();
        List<DataRow> matches = projects.AsEnumerable()
            .Where(row => Convert.ToString(row["Name"]) == _projectName)
            .ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException("Duplicate projects exist: " + _projectName);
        if (matches.Count == 0)
        {
            _projectFolder = SafeFolderName(_projectName.ToLowerInvariant());
            int projectId = IngenieurinformatikDAL.InsertProjectData(
                _projectName, _projectFolder, "");
            if (projectId <= 0)
                throw new InvalidOperationException("Cannot create project: " + _projectName);
            _projectId = projectId.ToString();
            Console.WriteLine("PROJECT_CREATED name={0} id={1} folder={2}",
                _projectName, _projectId, _projectFolder);
        }
        else
        {
            _projectId = Convert.ToString(matches[0]["ID"]);
            _projectFolder = Convert.ToString(matches[0]["PinYin"]);
            Console.WriteLine("PROJECT_REUSED name={0} id={1} folder={2}",
                _projectName, _projectId, _projectFolder);
        }
        Directory.CreateDirectory(Path.Combine(_basePath, "Project", _projectFolder));
        HookHelper.BasePath = _basePath;
        HookHelper.ProjectID = _projectId;
        HookHelper.ProjectNames = _projectName;
        HookHelper.ProjectFolder = _projectFolder;
        HookHelper.ProjectType = "";
    }

    private static List<ZoneInfo> GetProjectZones()
    {
        DataTable table = IngenieurinformatikDAL.GetCalcZoneByName(_projectName, "");
        return table.AsEnumerable()
            .Select(row => new ZoneInfo(
                Convert.ToString(row["ID"]),
                Convert.ToString(row["Name"]),
                Convert.ToString(row["PinYin"]),
                false))
            .ToList();
    }

    private static void BindZone(ZoneInfo zone)
    {
        SetZoneIdentity(zone);
        HookHelper.WataList.Clear();
        HookHelper.WataList = WataManagerDAL.SearchWata(zone.Id, "0", "");
        HookHelper.RivlList.Clear();
        HookHelper.RivlList = RivlManagerDAL.SearchRivl(zone.Id, "0", "");
        HookHelper.NodeList.Clear();
        HookHelper.NodeList = NodeManagerDAL.SearchNode(zone.Id, "0", "");
        HookHelper.SiteList.Clear();
        HookHelper.SiteList = SiteManagerDAL.SearchSite(zone.Id, "");
    }

    private static void SetZoneIdentity(ZoneInfo zone)
    {
        HookHelper.CZoneID = zone.Id;
        HookHelper.CZoneName = zone.Name;
        HookHelper.CZoneFolder = zone.Folder;
    }

    private static void BindScenario(HSCC scenario)
    {
        HookHelper.HSCCID = scenario.hsccid;
        HookHelper.HSCCName = scenario.name;
        HookHelper.JS_StartTime = scenario.starttime;
        HookHelper.JS_EndTime = scenario.endtime;
        HookHelper.JS_TimeInterval = scenario.timeinterval;
        HookHelper.JS_YLInterval = scenario.ylinterval;
        HookHelper.JS_LLInterval = scenario.llinterval;
        HookHelper.JS_JYLType = scenario.jylType;
        HookHelper.Node_Localtion = scenario.Localtion;
    }

    private static void ValidateScenario(HSCC scenario)
    {
        string interval = _intervalMinutes.ToString(CultureInfo.InvariantCulture);
        if (DateTime.Parse(scenario.starttime) != _startTime ||
            DateTime.Parse(scenario.endtime) != _endTime ||
            scenario.timeinterval != interval ||
            scenario.ylinterval != interval ||
            scenario.llinterval != interval ||
            scenario.jylType != 0)
            throw new InvalidOperationException(
                "Existing flood event does not match the requested start/end time or interval. " +
                "Use a new project name for a different flood event.");
    }

    private static IEnumerable<string> SplitCodes(string value)
    {
        return (value ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(code => code.Trim())
            .Where(code => code.Length != 0);
    }

    private static void BackupDatabase()
    {
        string database = Path.Combine(_basePath, "project.db");
        RequireFile(database);
        string backupFolder = Path.Combine(_basePath, "unified_workflow_backups");
        Directory.CreateDirectory(backupFolder);
        string backup = Path.Combine(backupFolder, "project_rollback_current.db");
        File.Copy(database, backup, true);
        Console.WriteLine("DATABASE_ROLLBACK_READY " + backup);
    }

    private static void CopyShape(
        string sourceShp, string targetFolder, string targetBase)
    {
        RequireFile(sourceShp);
        string sourceFolder = Path.GetDirectoryName(sourceShp);
        string sourceBase = Path.GetFileNameWithoutExtension(sourceShp);
        string[] extensions =
        {
            ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx",
            ".qix", ".shp.xml"
        };
        foreach (string extension in extensions)
        {
            string source = Path.Combine(sourceFolder, sourceBase + extension);
            if (File.Exists(source))
                File.Copy(source, Path.Combine(targetFolder, targetBase + extension), true);
        }
    }

    private static string SafeFolderName(string value)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        string safe = new string(value.Select(character =>
            invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character).ToArray());
        return safe.Trim('_');
    }

    private static void RequireModelInsert(int result, string stage)
    {
        if (result < 0)
            throw new InvalidOperationException("Flood-event initialization failed at " + stage + ".");
    }

    private static void RequireDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "Required input is missing:" + path,
                path);
    }

    private static int NaturalUnitNumber(string name)
    {
        int index = name.LastIndexOf('_');
        int number;
        return index >= 0 && int.TryParse(name.Substring(index + 1), out number)
            ? number
            : int.MaxValue;
    }

    private static void ParseOptionalArguments(
        string[] args,
        out string selector,
        out bool prepareOnly,
        out bool preflightOnly)
    {
        selector = "--all";
        prepareOnly = false;
        preflightOnly = false;
        bool selectorSpecified = false;

        for (int index = 2; index < args.Length; index++)
        {
            string argument = args[index];

            if (argument == "--all")
            {
                if (selectorSpecified)
                    throw new ArgumentException("Only one unit selector can be specified.");
                selector = "--all";
                selectorSpecified = true;
            }
            else if (argument == "--prepare-only")
            {
                prepareOnly = true;
            }
            else if (argument == "--preflight-only")
            {
                preflightOnly = true;
            }
            else if (argument == "--start")
            {
                _startTime = ParseDateTimeOption(
                    RequireOptionValue(args, ref index, "--start"),
                    "--start");
            }
            else if (argument == "--end")
            {
                _endTime = ParseDateTimeOption(
                    RequireOptionValue(args, ref index, "--end"),
                    "--end");
            }
            else if (argument == "--interval")
            {
                string value = RequireOptionValue(args, ref index, "--interval");
                int minutes;
                if (!int.TryParse(value, out minutes) || minutes <= 0)
                    throw new ArgumentException("--interval must be a positive integer number of minutes.");
                _intervalMinutes = minutes;
            }
            else if (argument == "--station-file")
            {
                _stationFileName = RequireOptionValue(
                    args, ref index, "--station-file").Trim();
                ValidateInputFileName(_stationFileName, "--station-file");
            }
            else if (argument == "--rainfall-file")
            {
                _rainfallFileName = RequireOptionValue(
                    args, ref index, "--rainfall-file").Trim();
                ValidateInputFileName(_rainfallFileName, "--rainfall-file");
            }
            else if (argument == "--source-start")
            {
                _sourceStartTime = ParseDateTimeOption(
                    RequireOptionValue(args, ref index, "--source-start"),
                    "--source-start");
            }
            else if (argument == "--input-map")
            {
                _inputMapPath = Path.GetFullPath(
                    RequireOptionValue(args, ref index, "--input-map"));
            }
            else if (argument == "--skip-preflight")
            {
                _skipPreflight = true;
            }
            else if (argument.StartsWith("--"))
            {
                throw new ArgumentException("Unknown option: " + argument);
            }
            else
            {
                if (selectorSpecified)
                    throw new ArgumentException("Only one unit selector can be specified.");
                selector = argument;
                selectorSpecified = true;
            }
        }

        if (prepareOnly && preflightOnly)
            throw new ArgumentException(
                "--prepare-only and --preflight-only cannot be used together.");
        if (preflightOnly && _skipPreflight)
            throw new ArgumentException(
                "--skip-preflight cannot be used with --preflight-only.");
    }

    private static string RequireOptionValue(
        string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException("Missing value for " + option + ".");

        index++;
        return args[index];
    }

    private static DateTime ParseDateTimeOption(string value, string option)
    {
        DateTime parsed;
        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return parsed;

        throw new ArgumentException(
            "Invalid date/time for " + option +
            ". Example: 2006/8/1 1:00:00 or 2006-08-01 01:00:00.");
    }

    private static void ValidateInputFileName(string value, string option)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(option + " cannot be empty.");

        if (Path.IsPathRooted(value) ||
            value.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
            value.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            throw new ArgumentException(
                option + " must be a file name only, not a path.");

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            if (value.IndexOf(invalid) >= 0)
                throw new ArgumentException(
                    option + " contains an invalid file-name character: " + invalid);
        }
    }

    private static void ValidateRuntimeParameters()
    {
        if (_endTime <= _startTime)
            throw new ArgumentException("--end must be later than --start.");

        if (_sourceStartTime < _startTime || _sourceStartTime > _endTime)
            throw new ArgumentException(
                "--source-start must fall between --start and --end.");

        double eventMinutes = (_endTime - _startTime).TotalMinutes;
        if (eventMinutes % _intervalMinutes != 0)
            throw new ArgumentException(
                "The event duration must be exactly divisible by --interval.");

        double sourceMinutes = (_endTime - _sourceStartTime).TotalMinutes;
        if (sourceMinutes % _intervalMinutes != 0)
            throw new ArgumentException(
                "The period from --source-start to --end must be exactly divisible by --interval.");
    }

    private static int GetSourceRowCount()
    {
        return checked(
            (int)((_endTime - _sourceStartTime).TotalMinutes / _intervalMinutes) + 1);
    }


    private static string FormatModelTime(DateTime value)
    {
        return value.ToString("yyyy/M/d H:mm:ss", CultureInfo.InvariantCulture);
    }

    private static void PrintCapabilities()
    {
        Console.WriteLine("WORKFLOW_CORE_VERSION 3.5");
        Console.WriteLine("CAPABILITY capability-query");
        Console.WriteLine("CAPABILITY skip-preflight");
        Console.WriteLine("CAPABILITY database-rollback");
        Console.WriteLine("CAPABILITY input-mapping");
        Console.WriteLine("CAPABILITY auto-source");
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: UnifiedHydroWorkflow.exe <unit-root> <project-name> " +
            "[--all|unit-name] [--prepare-only|--preflight-only] " +
            "[--start \"2006/8/1 1:00:00\"] [--end \"2006/8/6 0:00:00\"] " +
            "[--interval 60] [--station-file \"站点信息.xlsx\"] " +
            "[--rainfall-file \"20060803111降雨.xlsx\"] " +
            "[--source-start \"2006/8/3 1:00:00\"] " +
            "[--input-map \"UnifiedHydroInputMapping.xml\"] [--skip-preflight]");
    }

    private static void LoadInputMapping(string unitsRoot, string mappingPath)
    {
        RequireFile(mappingPath);
        var xml = new XmlDocument();
        xml.Load(mappingPath);
        XmlElement root = xml.DocumentElement;
        if (root == null || root.Name != "InputMapping" ||
            root.GetAttribute("Version") != "1")
            throw new InvalidDataException(
                "Input mapping must use <InputMapping Version=\"1\">.");

        _mappedUnits.Clear();
        foreach (XmlNode node in root.ChildNodes)
        {
            XmlElement element = node as XmlElement;
            if (element == null) continue;
            if (element.Name != "Unit")
                throw new InvalidDataException("Unknown input-map element: " + element.Name);

            string name = element.GetAttribute("Name").Trim();
            if (name.Length == 0)
                throw new InvalidDataException("Input-map unit name cannot be empty.");
            if (_mappedUnits.ContainsKey(name))
                throw new InvalidDataException("Duplicate input-map unit: " + name);

            string folder = Path.Combine(unitsRoot, name);
            var mapped = new MappedUnitInput(
                ResolveMappedFile(element, "Watershed", folder),
                ResolveMappedFile(element, "River", folder),
                ResolveMappedFile(element, "Node", folder),
                ResolveMappedFile(element, "Land", folder),
                ResolveMappedFile(element, "Soil", folder),
                ResolveMappedFile(element, "Station", folder),
                ResolveMappedFile(element, "Rainfall", folder));
            _mappedUnits.Add(name, mapped);
        }

        if (_mappedUnits.Count == 0)
            throw new InvalidDataException("Input mapping contains no units.");
        Console.WriteLine("INPUT_MAP_LOADED path={0} units={1}",
            mappingPath, _mappedUnits.Count);
    }

    private static string ResolveMappedFile(
        XmlElement unit, string role, string unitFolder)
    {
        XmlNodeList nodes = unit.SelectNodes(role);
        if (nodes == null || nodes.Count != 1)
            throw new InvalidDataException(
                "Input-map field must appear exactly once: unit=" +
                unit.GetAttribute("Name") + " field=" + role);
        XmlElement item = nodes[0] as XmlElement;
        string value = item == null ? "" : item.GetAttribute("File").Trim();
        if (value.Length == 0)
            throw new InvalidDataException(
                "Input-map field is empty: unit=" +
                unit.GetAttribute("Name") + " field=" + role);
        return Path.GetFullPath(
            Path.IsPathRooted(value) ? value : Path.Combine(unitFolder, value));
    }

    private sealed class MappedUnitInput
    {
        public readonly string Wata;
        public readonly string River;
        public readonly string Node;
        public readonly string Land;
        public readonly string Soil;
        public readonly string Station;
        public readonly string Rainfall;

        public MappedUnitInput(
            string wata, string river, string node, string land,
            string soil, string station, string rainfall)
        {
            Wata = wata;
            River = river;
            Node = node;
            Land = land;
            Soil = soil;
            Station = station;
            Rainfall = rainfall;
        }
    }

    private sealed class UnitInput
    {
        public readonly string Folder;
        public readonly string Name;
        public readonly string Wata;
        public readonly string River;
        public readonly string Node;
        public readonly string LandUse;
        public readonly string Soil;
        public readonly string StationFile;
        public readonly string RainfallFile;
        public CoverageResult LandCoverage;
        public CoverageResult SoilCoverage;

        public UnitInput(string folder)
        {
            Folder = folder;
            Name = Path.GetFileName(folder);

            MappedUnitInput mapped;
            if (_mappedUnits.TryGetValue(Name, out mapped))
            {
                Wata = mapped.Wata;
                River = mapped.River;
                Node = mapped.Node;
                LandUse = mapped.Land;
                Soil = mapped.Soil;
                StationFile = mapped.Station;
                RainfallFile = mapped.Rainfall;
            }
            else
            {
                Wata = Path.Combine(folder, "wata.shp");
                River = Path.Combine(folder, "rivl.shp");
                Node = Path.Combine(folder, "node.shp");
                LandUse = Path.Combine(folder, "土地利用.shp");
                Soil = Path.Combine(folder, "土壤类型.shp");
                StationFile = Path.Combine(folder, _stationFileName);
                RainfallFile = Path.Combine(folder, _rainfallFileName);
            }
        }

        public void RequireInputs()
        {
            RequireFile(Wata);
            RequireFile(River);
            RequireFile(Node);
            RequireFile(LandUse);
            RequireFile(Soil);
            RequireFile(StationFile);
            RequireFile(RainfallFile);
        }
    }

    private sealed class CoverageResult
    {
        public readonly double Minimum;
        public readonly double Overall;
        public readonly int WatershedCount;
        public readonly int ZeroCoverageCount;

        public CoverageResult(
            double minimum,
            double overall,
            int count,
            int zeroCoverageCount)
        {
            Minimum = minimum;
            Overall = overall;
            WatershedCount = count;
            ZeroCoverageCount = zeroCoverageCount;
        }
    }

    private sealed class ZoneInfo
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Folder;
        public readonly bool IsNew;

        public ZoneInfo(string id, string name, string folder, bool isNew)
        {
            Id = id;
            Name = name;
            Folder = folder;
            IsNew = isNew;
        }
    }

    private sealed class SourceRecord
    {
        public readonly string Code;
        public readonly DateTime Time;
        public readonly double Flow;

        public SourceRecord(
            string code,
            DateTime time,
            double flow)
        {
            Code = code;
            Time = time;
            Flow = flow;
        }
    }

    private sealed class InputSegment
    {
        public readonly string RiverCode;
        public readonly List<string> UpstreamCodes;
        public InputSegment(string riverCode, List<string> upstreamCodes)
        {
            RiverCode = riverCode;
            UpstreamCodes = upstreamCodes;
        }
    }
}
