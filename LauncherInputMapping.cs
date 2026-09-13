using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace UnifiedHydroLauncher
{
    internal static class InputRoles
    {
        internal const string Watershed = "Watershed";
        internal const string River = "River";
        internal const string Node = "Node";
        internal const string Land = "Land";
        internal const string Soil = "Soil";
        internal const string Station = "Station";
        internal const string Rainfall = "Rainfall";

        internal static readonly string[] All =
        {
            Watershed, River, Node, Land, Soil, Station, Rainfall
        };

        internal static bool IsShape(string role)
        {
            return role == Watershed || role == River || role == Node ||
                   role == Land || role == Soil;
        }
    }

    internal sealed class LauncherUnitInput
    {
        private readonly Dictionary<string, string> paths =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> candidates =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        internal readonly string UnitName;
        internal readonly string UnitFolder;
        internal string RecognitionNote = "";

        internal LauncherUnitInput(string unitName, string unitFolder)
        {
            UnitName = unitName;
            UnitFolder = Path.GetFullPath(unitFolder);
            foreach (string role in InputRoles.All)
                candidates[role] = new List<string>();
        }

        internal string GetPath(string role)
        {
            string value;
            return paths.TryGetValue(role, out value) ? value : "";
        }

        internal void SetPath(string role, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                paths.Remove(role);
            else
                paths[role] = Path.GetFullPath(value.Trim());
        }

        internal List<string> GetCandidates(string role)
        {
            return candidates[role];
        }

        internal void SetCandidates(string role, IEnumerable<string> values)
        {
            candidates[role] = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => Path.GetFileName(value), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal bool Validate(out string note)
        {
            var problems = new List<string>();
            foreach (string role in InputRoles.All)
            {
                string path = GetPath(role);
                if (path.Length == 0)
                {
                    int count = GetCandidates(role).Count;
                    problems.Add(RoleCaption(role) +
                        (count > 1 ? "有多个候选" : "未识别"));
                    continue;
                }

                if (InputRoles.IsShape(role))
                {
                    if (!string.Equals(Path.GetExtension(path), ".shp",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        problems.Add(RoleCaption(role) + "扩展名不是 .shp");
                        continue;
                    }
                    CheckFile(path, RoleCaption(role), problems);
                    CheckFile(Path.ChangeExtension(path, ".shx"),
                        RoleCaption(role) + " .shx", problems);
                    CheckFile(Path.ChangeExtension(path, ".dbf"),
                        RoleCaption(role) + " .dbf", problems);
                }
                else
                {
                    string extension = Path.GetExtension(path);
                    if (!string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        problems.Add(RoleCaption(role) + "必须是 .xls 或 .xlsx");
                        continue;
                    }
                    CheckFile(path, RoleCaption(role), problems);
                }
            }

            note = string.Join("；", problems.ToArray());
            return problems.Count == 0;
        }

        internal LauncherUnitInput Clone()
        {
            var clone = new LauncherUnitInput(UnitName, UnitFolder);
            foreach (string role in InputRoles.All)
            {
                clone.SetCandidates(role, GetCandidates(role));
                clone.SetPath(role, GetPath(role));
            }
            clone.RecognitionNote = RecognitionNote;
            return clone;
        }

        private static void CheckFile(
            string path, string caption, List<string> problems)
        {
            if (!File.Exists(path))
                problems.Add(caption + "缺失");
        }

        internal static string RoleCaption(string role)
        {
            if (role == InputRoles.Watershed) return "流域";
            if (role == InputRoles.River) return "河道";
            if (role == InputRoles.Node) return "节点";
            if (role == InputRoles.Land) return "土地利用";
            if (role == InputRoles.Soil) return "土壤";
            if (role == InputRoles.Station) return "站点";
            if (role == InputRoles.Rainfall) return "降雨";
            return role;
        }
    }

    internal sealed class LauncherInputMappingDocument
    {
        private readonly Dictionary<string, LauncherUnitInput> units =
            new Dictionary<string, LauncherUnitInput>(StringComparer.Ordinal);

        internal IEnumerable<LauncherUnitInput> Units
        {
            get { return units.Values; }
        }

        internal int UnitCount { get { return units.Count; } }

        internal void Add(LauncherUnitInput unit)
        {
            if (units.ContainsKey(unit.UnitName))
                throw new InvalidDataException(
                    "输入映射包含重复单元：" + unit.UnitName);
            units.Add(unit.UnitName, unit);
        }

        internal bool TryGet(string unitName, out LauncherUnitInput unit)
        {
            return units.TryGetValue(unitName, out unit);
        }

        internal LauncherUnitInput GetRequired(string unitName)
        {
            LauncherUnitInput unit;
            if (!units.TryGetValue(unitName, out unit))
                throw new InvalidDataException(
                    "INPUT_MAP_UNIT_MISSING unit=" + unitName);
            return unit;
        }

        internal static LauncherInputMappingDocument Load(
            string mappingPath, string unitRoot)
        {
            if (!File.Exists(mappingPath))
                throw new FileNotFoundException("输入映射文件不存在。", mappingPath);
            var xml = new XmlDocument();
            xml.Load(mappingPath);
            XmlElement root = xml.DocumentElement;
            if (root == null || root.Name != "InputMapping" ||
                root.GetAttribute("Version") != "1")
                throw new InvalidDataException(
                    "输入映射 XML 必须使用 <InputMapping Version=\"1\">。 ");

            var result = new LauncherInputMappingDocument();
            foreach (XmlNode node in root.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null) continue;
                if (element.Name != "Unit")
                    throw new InvalidDataException("未知映射元素：" + element.Name);
                string name = element.GetAttribute("Name").Trim();
                if (name.Length == 0)
                    throw new InvalidDataException("映射单元名称不能为空。");
                string folder = Path.Combine(unitRoot, name);
                var unit = new LauncherUnitInput(name, folder);
                foreach (string role in InputRoles.All)
                {
                    XmlNodeList matches = element.SelectNodes(role);
                    if (matches == null || matches.Count != 1)
                        throw new InvalidDataException(
                            "映射字段必须且只能出现一次：unit=" + name +
                            " field=" + role);
                    XmlElement fileElement = matches[0] as XmlElement;
                    string value = fileElement == null
                        ? ""
                        : fileElement.GetAttribute("File").Trim();
                    if (value.Length == 0)
                        throw new InvalidDataException(
                            "映射字段缺失：unit=" + name + " field=" + role);
                    unit.SetPath(role, ResolvePath(folder, value));
                }
                result.Add(unit);
            }
            if (result.UnitCount == 0)
                throw new InvalidDataException("输入映射没有计算单元。");
            return result;
        }

        internal void Save(string mappingPath)
        {
            foreach (LauncherUnitInput unit in units.Values)
            {
                string validationNote;
                if (!unit.Validate(out validationNote))
                    throw new InvalidDataException(
                        "无法保存无效映射：unit=" + unit.UnitName + " " + validationNote);
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(mappingPath));
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var settings = new XmlWriterSettings();
            settings.Encoding = new System.Text.UTF8Encoding(false);
            settings.Indent = true;
            settings.NewLineChars = Environment.NewLine;
            using (XmlWriter writer = XmlWriter.Create(mappingPath, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("InputMapping");
                writer.WriteAttributeString("Version", "1");
                foreach (LauncherUnitInput unit in units.Values
                    .OrderBy(value => value.UnitName, StringComparer.Ordinal))
                {
                    writer.WriteStartElement("Unit");
                    writer.WriteAttributeString("Name", unit.UnitName);
                    foreach (string role in InputRoles.All)
                    {
                        writer.WriteStartElement(role);
                        writer.WriteAttributeString(
                            "File",
                            ToPortablePath(unit.UnitFolder, unit.GetPath(role)));
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        internal string BuildFingerprint()
        {
            var values = new List<string>();
            foreach (LauncherUnitInput unit in units.Values
                .OrderBy(value => value.UnitName, StringComparer.Ordinal))
            {
                values.Add(unit.UnitName);
                foreach (string role in InputRoles.All)
                    values.Add(role + "=" + unit.GetPath(role));
            }
            return string.Join("\n", values.ToArray());
        }

        private static string ResolvePath(string folder, string value)
        {
            return Path.GetFullPath(
                Path.IsPathRooted(value) ? value : Path.Combine(folder, value));
        }

        internal static string ToPortablePath(string folder, string path)
        {
            string fullFolder = Path.GetFullPath(folder).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(fullFolder.Length);
            return fullPath;
        }
    }
}
