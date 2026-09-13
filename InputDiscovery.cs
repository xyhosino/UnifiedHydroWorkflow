using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnifiedHydroLauncher
{
    internal static class InputDiscovery
    {
        internal const string AutomaticMode = "自动识别";
        internal const string UnifiedNameMode = "统一文件名";

        internal static LauncherUnitInput Discover(
            string unitFolder,
            string stationMode,
            string stationName,
            string rainfallMode,
            string rainfallName)
        {
            return Discover(
                unitFolder,
                stationMode,
                stationName,
                rainfallMode,
                rainfallName,
                "",
                "");
        }

        internal static LauncherUnitInput Discover(
            string unitFolder,
            string stationMode,
            string stationName,
            string rainfallMode,
            string rainfallName,
            string stationExternalRoot,
            string rainfallExternalRoot)
        {
            string unitName = Path.GetFileName(unitFolder);
            var result = new LauncherUnitInput(unitName, unitFolder);
            var notes = new List<string>();
            var shapes = ReadShapeCandidates(unitFolder, notes);

            SelectShape(result, InputRoles.Watershed, shapes, notes, unitName);
            SelectShape(result, InputRoles.River, shapes, notes, unitName);
            SelectShape(result, InputRoles.Node, shapes, notes, unitName);
            SelectShape(result, InputRoles.Land, shapes, notes, unitName);
            SelectShape(result, InputRoles.Soil, shapes, notes, unitName);

            List<string> localExcel = ReadExcelCandidates(unitFolder, false);
            List<string> stationExternal = ReadExcelCandidates(stationExternalRoot, true);
            List<string> rainfallExternal = ReadExcelCandidates(rainfallExternalRoot, true);

            SelectExcelWithFallback(
                result,
                InputRoles.Station,
                localExcel,
                stationExternal,
                stationMode,
                stationName,
                unitName,
                null,
                notes);

            string station = result.GetPath(InputRoles.Station);

            SelectExcelWithFallback(
                result,
                InputRoles.Rainfall,
                localExcel,
                rainfallExternal,
                rainfallMode,
                rainfallName,
                unitName,
                station,
                notes);

            string rainfall = result.GetPath(InputRoles.Rainfall);
            if (station.Length > 0 && rainfall.Length > 0 &&
                string.Equals(station, rainfall, StringComparison.OrdinalIgnoreCase))
            {
                result.SetPath(InputRoles.Station, "");
                result.SetPath(InputRoles.Rainfall, "");
                notes.Add("站点和降雨不能使用同一文件");
            }

            result.RecognitionNote = string.Join("；", notes.Distinct().ToArray());
            return result;
        }

        private static List<string> ReadExcelCandidates(string folder, bool recursive)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return result;

            try
            {
                SearchOption option = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;
                result = Directory.GetFiles(folder, "*.*", option)
                    .Where(path =>
                    {
                        string extension = Path.GetExtension(path);
                        if (!string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
                            return false;
                        return !Path.GetFileName(path).EndsWith(
                            "上游水源数据表.xls",
                            StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                // 外部目录只是补充来源；读取失败时由主界面状态提示缺失。
            }
            return result;
        }

        private static List<ShapeCandidate> ReadShapeCandidates(
            string folder, List<string> notes)
        {
            var result = new List<ShapeCandidate>();
            foreach (string shp in Directory.GetFiles(folder, "*.shp"))
            {
                string dbf = Path.ChangeExtension(shp, ".dbf");
                if (!File.Exists(dbf))
                {
                    notes.Add(Path.GetFileName(shp) + " 缺少 DBF");
                    continue;
                }
                try
                {
                    result.Add(new ShapeCandidate(
                        shp,
                        ReadDbfFieldNames(dbf),
                        ReadShapeType(shp)));
                }
                catch (Exception exception)
                {
                    notes.Add(Path.GetFileName(shp) + " 无法识别：" + exception.Message);
                }
            }
            return result;
        }

        private static void SelectShape(
            LauncherUnitInput result,
            string role,
            List<ShapeCandidate> all,
            List<string> notes,
            string unitName)
        {
            List<ShapeCandidate> eligible = all
                .Where(candidate => MatchesRole(candidate, role))
                .ToList();
            result.SetCandidates(role, eligible.Select(candidate => candidate.Path));
            ShapeCandidate selected = SelectUniqueBest(eligible, role, unitName);
            if (selected != null)
                result.SetPath(role, selected.Path);
            else if (eligible.Count > 1)
                notes.Add(LauncherUnitInput.RoleCaption(role) + "存在多个候选");
            else if (eligible.Count == 0)
                notes.Add(LauncherUnitInput.RoleCaption(role) + "未识别");
        }

        // V5.0.6: geometry is the first discriminator.  This prevents a polygon
        // thematic layer that also carries WSCD from competing with river/node
        // inputs, and supports unit-named watershed files such as WHA39_5_2_1.shp.
        private static bool MatchesRole(ShapeCandidate candidate, string role)
        {
            if (role == InputRoles.Watershed)
                return IsPolygon(candidate.ShapeType) &&
                       candidate.Fields.Contains("WSCD") &&
                       !candidate.Fields.Contains("XDMDM") &&
                       !candidate.Fields.Contains("TRZDBM");
            if (role == InputRoles.River)
                return IsPolyline(candidate.ShapeType) &&
                       candidate.Fields.Contains("RVCD") &&
                       candidate.Fields.Contains("FRVCD");
            if (role == InputRoles.Node)
                return IsPoint(candidate.ShapeType) &&
                       candidate.Fields.Contains("NDCD") &&
                       (candidate.Fields.Contains("FNDCD") ||
                        candidate.Fields.Contains("TNDCD"));
            if (role == InputRoles.Land)
                return IsPolygon(candidate.ShapeType) &&
                       candidate.Fields.Contains("XDMDM");
            if (role == InputRoles.Soil)
                return IsPolygon(candidate.ShapeType) &&
                       candidate.Fields.Contains("TRZDBM");
            return false;
        }

        private static bool IsPoint(int shapeType)
        {
            return shapeType == 1 || shapeType == 8 ||
                   shapeType == 11 || shapeType == 18 ||
                   shapeType == 21 || shapeType == 28;
        }

        private static bool IsPolyline(int shapeType)
        {
            return shapeType == 3 || shapeType == 13 || shapeType == 23;
        }

        private static bool IsPolygon(int shapeType)
        {
            return shapeType == 5 || shapeType == 15 || shapeType == 25;
        }

        private static ShapeCandidate SelectUniqueBest(
            List<ShapeCandidate> values, string role, string unitName)
        {
            if (values.Count == 0) return null;
            if (values.Count == 1) return values[0];
            List<ShapeCandidate> ordered = values
                .OrderByDescending(value => ShapeScore(value, role, unitName))
                .ThenBy(value => Path.GetFileName(value.Path),
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            return ShapeScore(ordered[0], role, unitName) >
                   ShapeScore(ordered[1], role, unitName)
                ? ordered[0]
                : null;
        }

        private static int ShapeScore(
            ShapeCandidate candidate, string role, string unitName)
        {
            string name = Path.GetFileNameWithoutExtension(candidate.Path).ToLowerInvariant();
            string unit = (unitName ?? "").Trim().ToLowerInvariant();
            int score = 0;
            string standard = role == InputRoles.Watershed ? "wata" :
                role == InputRoles.River ? "rivl" :
                role == InputRoles.Node ? "node" :
                role == InputRoles.Land ? "土地利用" : "土壤类型";

            if (name == standard) score += 100;

            // A polygon whose basename equals the calculation-unit folder is the
            // strongest watershed convention used by the Guangxi data.
            if (role == InputRoles.Watershed && unit.Length > 0 &&
                string.Equals(name, unit, StringComparison.OrdinalIgnoreCase))
                score += 160;

            if (role == InputRoles.Watershed &&
                (name.Contains("wata") || name.Contains("basin") || name.Contains("catchment") ||
                 name.Contains("流域")))
                score += 30;
            if (role == InputRoles.River &&
                (name.Contains("rivl") || name.Contains("river") || name.Contains("河道")))
                score += 30;
            if (role == InputRoles.Node &&
                (name.Contains("node") || name.Contains("junction") || name.Contains("节点")))
                score += 30;
            if (role == InputRoles.Land &&
                (name.Contains("land") || name.Contains("lucc") || name.Contains("土地")))
                score += 30;
            if (role == InputRoles.Soil &&
                (name.Contains("soil") || name.Contains("土壤")))
                score += 30;

            if (role == InputRoles.Watershed && candidate.Fields.Contains("WSCU_Name"))
                score += 20;
            if (role == InputRoles.Node &&
                candidate.Fields.Contains("NDX") && candidate.Fields.Contains("NDY"))
                score += 5;

            return score;
        }

        private static void SelectExcelWithFallback(
            LauncherUnitInput result,
            string role,
            List<string> localExcel,
            List<string> externalExcel,
            string mode,
            string unifiedName,
            string unitName,
            string excludedPath,
            List<string> notes)
        {
            List<string> local = Excluding(localExcel, excludedPath);
            List<string> external = Excluding(externalExcel, excludedPath);
            result.SetCandidates(role, local.Concat(external));

            string selected = "";
            if (mode == UnifiedNameMode)
            {
                selected = FindExactFileName(local, unifiedName);
                if (selected.Length == 0)
                    selected = FindUniqueExactFileName(external, unifiedName);
            }
            else
            {
                selected = SelectUniqueExcel(local, role, unifiedName, unitName);
                if (selected.Length == 0)
                    selected = SelectUniqueExcel(external, role, unifiedName, unitName);
            }

            if (selected.Length > 0)
            {
                result.SetPath(role, selected);
                if (!IsInsideFolder(selected, result.UnitFolder))
                    notes.Add(LauncherUnitInput.RoleCaption(role) + "使用外部目录文件");
                return;
            }

            if (local.Count + external.Count > 1)
                notes.Add(LauncherUnitInput.RoleCaption(role) + "存在多个候选");
            else
                notes.Add(LauncherUnitInput.RoleCaption(role) + "未识别");
        }

        private static List<string> Excluding(List<string> values, string excludedPath)
        {
            if (string.IsNullOrWhiteSpace(excludedPath))
                return new List<string>(values);
            return values
                .Where(value => !string.Equals(value, excludedPath,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static string FindExactFileName(List<string> values, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return "";
            return values.FirstOrDefault(path => string.Equals(
                Path.GetFileName(path), fileName.Trim(),
                StringComparison.OrdinalIgnoreCase)) ?? "";
        }

        private static string FindUniqueExactFileName(List<string> values, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return "";
            List<string> matches = values.Where(path => string.Equals(
                Path.GetFileName(path), fileName.Trim(),
                StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? matches[0] : "";
        }

        private static string SelectUniqueExcel(
            List<string> values, string role, string preferredName, string unitName)
        {
            if (values.Count == 0) return "";

            var scored = values
                .Select(path => new KeyValuePair<string, int>(
                    path,
                    ExcelScore(path, role, preferredName, unitName)))
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => Path.GetFileName(pair.Key),
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (role == InputRoles.Rainfall && values.Count == 1)
                return values[0];

            if (scored[0].Value <= 0) return "";
            if (scored.Count > 1 && scored[0].Value == scored[1].Value) return "";
            return scored[0].Key;
        }

        private static int ExcelScore(
            string path, string role, string preferredName, string unitName)
        {
            string file = Path.GetFileName(path);
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            string unit = (unitName ?? "").Trim().ToLowerInvariant();
            string shortUnit = GetShortUnitName(unitName).ToLowerInvariant();
            int score = string.Equals(file, preferredName,
                StringComparison.OrdinalIgnoreCase) ? 100 : 0;

            if (role == InputRoles.Station)
            {
                if (unit.Length > 0 && string.Equals(name, unit,
                    StringComparison.OrdinalIgnoreCase)) score += 100;
                if (shortUnit.Length > 0 && string.Equals(name, shortUnit,
                    StringComparison.OrdinalIgnoreCase)) score += 90;
                if (shortUnit.Length > 0 && name.Contains(shortUnit)) score += 35;
                if (name.Contains("站点") || name.Contains("station") || name.Contains("site"))
                    score += 20;
                if (name.Contains("降雨") || name.Contains("rainfall") || name.Contains("rain"))
                    score -= 30;
            }
            else if (role == InputRoles.Rainfall)
            {
                if (name.Contains("降雨") || name.Contains("rainfall") || name.Contains("rain"))
                    score += 30;
                if (shortUnit.Length > 0 && string.Equals(name, shortUnit,
                    StringComparison.OrdinalIgnoreCase)) score -= 40;
                if (name.Contains("站点") || name.Contains("station") || name.Contains("site"))
                    score -= 30;
            }
            return score;
        }

        internal static string GetShortUnitName(string unitName)
        {
            string value = (unitName ?? "").Trim();
            Match match = Regex.Match(value, @"^[A-Za-z]+(.+)$");
            if (match.Success && match.Groups.Count > 1)
                return match.Groups[1].Value;
            return value;
        }

        private static bool IsInsideFolder(string path, string folder)
        {
            try
            {
                string fullFolder = Path.GetFullPath(folder).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullPath = Path.GetFullPath(path);
                return fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static HashSet<string> ReadDbfFieldNames(string dbfFile)
        {
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var stream = new FileStream(dbfFile, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 33)
                    throw new InvalidDataException("DBF 文件过小");
                stream.Seek(8, SeekOrigin.Begin);
                ushort headerLength = reader.ReadUInt16();
                if (headerLength < 33 || headerLength > stream.Length)
                    throw new InvalidDataException("DBF 头长度无效");
                stream.Seek(32, SeekOrigin.Begin);
                while (stream.Position < headerLength)
                {
                    byte first = reader.ReadByte();
                    if (first == 0x0D) break;
                    byte[] rest = reader.ReadBytes(31);
                    if (rest.Length != 31)
                        throw new EndOfStreamException("DBF 字段描述不完整");
                    byte[] descriptor = new byte[32];
                    descriptor[0] = first;
                    Buffer.BlockCopy(rest, 0, descriptor, 1, 31);
                    int length = 0;
                    while (length < 11 && descriptor[length] != 0) length++;
                    fields.Add(System.Text.Encoding.ASCII
                        .GetString(descriptor, 0, length).Trim());
                }
            }
            return fields;
        }

        private static int ReadShapeType(string shpFile)
        {
            using (var stream = new FileStream(shpFile, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 36) return 0;
                stream.Seek(32, SeekOrigin.Begin);
                return reader.ReadInt32();
            }
        }

        private sealed class ShapeCandidate
        {
            internal readonly string Path;
            internal readonly HashSet<string> Fields;
            internal readonly int ShapeType;

            internal ShapeCandidate(
                string path, HashSet<string> fields, int shapeType)
            {
                Path = path;
                Fields = fields;
                ShapeType = shapeType;
            }
        }
    }
}
