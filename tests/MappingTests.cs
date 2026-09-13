using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnifiedHydroLauncher
{
    internal static class MappingTests
    {
        private static int count;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine("PASS " + name);
            count++;
        }

        private static void WriteDbf(string path, params string[] fields)
        {
            int headerLength = 32 + fields.Length * 32 + 1;
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)3);
                writer.Write(new byte[3]);
                writer.Write(0);
                writer.Write((ushort)headerLength);
                writer.Write((ushort)1);
                writer.Write(new byte[20]);
                foreach (string field in fields)
                {
                    byte[] descriptor = new byte[32];
                    byte[] name = Encoding.ASCII.GetBytes(field);
                    Buffer.BlockCopy(name, 0, descriptor, 0, Math.Min(11, name.Length));
                    descriptor[11] = (byte)'C';
                    descriptor[16] = 1;
                    writer.Write(descriptor);
                }
                writer.Write((byte)0x0D);
                writer.Write((byte)0x1A);
            }
        }

        private static void WriteShape(string folder, string name, int shapeType,
            params string[] fields)
        {
            string shp = Path.Combine(folder, name + ".shp");
            byte[] bytes = new byte[100];
            byte[] type = BitConverter.GetBytes(shapeType);
            Buffer.BlockCopy(type, 0, bytes, 32, 4);
            File.WriteAllBytes(shp, bytes);
            File.WriteAllBytes(Path.ChangeExtension(shp, ".shx"), new byte[100]);
            WriteDbf(Path.ChangeExtension(shp, ".dbf"), fields);
        }

        private static LauncherUnitInput CreateSyntheticUnit(string root)
        {
            string folder = Path.Combine(root, "UNIT_A");
            Directory.CreateDirectory(folder);
            WriteShape(folder, "basin_alpha", 5, "WSCD");
            WriteShape(folder, "river_alpha", 3, "RVCD", "FRVCD");
            WriteShape(folder, "junction_alpha", 1, "NDCD", "FNDCD", "NDX", "NDY");
            WriteShape(folder, "lucc_alpha", 5, "XDMDM");
            WriteShape(folder, "soil_alpha", 5, "TRZDBM");
            WriteShape(folder, "wata_named_but_wrong", 5, "WRONG");
            File.WriteAllBytes(Path.Combine(folder, "station_alpha.xls"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder, "rain_alpha.xlsx"), new byte[] { 1 });
            return InputDiscovery.Discover(folder,
                InputDiscovery.AutomaticMode, "",
                InputDiscovery.AutomaticMode, "");
        }

        private static void TestSynthetic(string root)
        {
            LauncherUnitInput unit = CreateSyntheticUnit(root);
            string note;
            Assert(unit.Validate(out note), "nonstandard seven-file discovery ready");
            Assert(Path.GetFileName(unit.GetPath(InputRoles.Watershed)) == "basin_alpha.shp",
                "DBF schema wins over misleading file name");
            Assert(Path.GetExtension(unit.GetPath(InputRoles.Station)) == ".xls" &&
                Path.GetExtension(unit.GetPath(InputRoles.Rainfall)) == ".xlsx",
                "station xls and rainfall xlsx supported");

            var document = new LauncherInputMappingDocument();
            document.Add(unit.Clone());
            string xml = Path.Combine(root, "UnifiedHydroInputMapping.xml");
            document.Save(xml);
            string text = File.ReadAllText(xml);
            Assert(text.Contains("File=\"basin_alpha.shp\"") &&
                !text.Contains(unit.UnitFolder), "mapping saves unit-relative paths");
            LauncherInputMappingDocument loaded =
                LauncherInputMappingDocument.Load(xml, root);
            Assert(loaded.GetRequired("UNIT_A").Validate(out note),
                "saved mapping reloads and validates");

            var invalidDocument = new LauncherInputMappingDocument();
            invalidDocument.Add(new LauncherUnitInput(
                "INVALID", Path.Combine(root, "INVALID")));
            bool invalidSaveRejected = false;
            try
            {
                invalidDocument.Save(Path.Combine(root, "invalid.xml"));
            }
            catch (InvalidDataException)
            {
                invalidSaveRejected = true;
            }
            Assert(invalidSaveRejected,
                "mapping save rejects incomplete seven-role unit");

            string duplicate = text.Replace(
                "<Watershed File=\"basin_alpha.shp\" />",
                "<Watershed File=\"basin_alpha.shp\" /><Watershed File=\"basin_alpha.shp\" />");
            File.WriteAllText(Path.Combine(root, "duplicate.xml"), duplicate);
            bool rejected = false;
            try
            {
                LauncherInputMappingDocument.Load(Path.Combine(root, "duplicate.xml"), root);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Assert(rejected, "duplicate mapping field rejected");

            string ambiguousFolder = Path.Combine(root, "UNIT_B");
            Directory.CreateDirectory(ambiguousFolder);
            WriteShape(ambiguousFolder, "alpha", 5, "WSCD");
            WriteShape(ambiguousFolder, "beta", 5, "WSCD");
            LauncherUnitInput ambiguous = InputDiscovery.Discover(ambiguousFolder,
                InputDiscovery.AutomaticMode, "",
                InputDiscovery.AutomaticMode, "");
            Assert(ambiguous.GetPath(InputRoles.Watershed).Length == 0 &&
                ambiguous.GetCandidates(InputRoles.Watershed).Count == 2,
                "equal schema candidates remain ambiguous");

            string gxFolder = Path.Combine(root, "WHA39_5_2_1");
            Directory.CreateDirectory(gxFolder);
            WriteShape(gxFolder, "WHA39_5_2_1", 5, "WSCD", "WSCU_Name");
            WriteShape(gxFolder, "other_polygon", 5, "WSCD");
            WriteShape(gxFolder, "rivl_WHA39_5_2_1", 3, "RVCD", "FRVCD", "WSCD");
            WriteShape(gxFolder, "node_WHA39_5_2_1", 1, "NDCD", "FNDCD");
            WriteShape(gxFolder, "土地利用_WHA39_5_2_1", 5, "XDMDM", "WSCD");
            WriteShape(gxFolder, "土壤类型_WHA39_5_2_1", 5, "TRZDBM", "WSCD");
            File.WriteAllBytes(Path.Combine(gxFolder, "站点信息.xlsx"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(gxFolder, "20060803111降雨.xlsx"), new byte[] { 1 });
            LauncherUnitInput gx = InputDiscovery.Discover(gxFolder,
                InputDiscovery.AutomaticMode, "站点信息.xlsx",
                InputDiscovery.AutomaticMode, "20060803111降雨.xlsx");
            Assert(Path.GetFileName(gx.GetPath(InputRoles.Watershed)) ==
                "WHA39_5_2_1.shp",
                "unit-named polygon wins watershed mapping");
            Assert(Path.GetFileName(gx.GetPath(InputRoles.River)).StartsWith("rivl_"),
                "polyline maps to river");
            Assert(Path.GetFileName(gx.GetPath(InputRoles.Node)).StartsWith("node_"),
                "point maps to node");
        }

        private static void TestRealScan(string root, string mappingOutput)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            int ready = 0;
            int ambiguous = 0;
            int missing = 0;
            var document = new LauncherInputMappingDocument();
            string[] folders = Directory.GetDirectories(root);
            foreach (string folder in folders)
            {
                LauncherUnitInput unit = InputDiscovery.Discover(folder,
                    InputDiscovery.AutomaticMode, "站点信息.xlsx",
                    InputDiscovery.AutomaticMode, "20060803111降雨.xlsx");
                document.Add(unit.Clone());
                string note;
                if (unit.Validate(out note)) ready++;
                else if (InputRoles.All.Any(role =>
                    unit.GetPath(role).Length == 0 &&
                    unit.GetCandidates(role).Count > 1)) ambiguous++;
                else missing++;
            }
            Console.WriteLine("REAL_SCAN total={0} ready={1} ambiguous={2} missing={3}",
                folders.Length, ready, ambiguous, missing);
            Assert(folders.Length == 34, "real scan unit count 34");
            Assert(ready + ambiguous + missing == folders.Length,
                "real scan classification complete");
            if (!string.IsNullOrWhiteSpace(mappingOutput))
            {
                document.Save(mappingOutput);
                Assert(File.Exists(mappingOutput),
                    "real 34-unit mapping saved");
            }
        }

        private static int Main(string[] args)
        {
            string root = Path.Combine(Path.GetTempPath(),
                "UnifiedHydroMappingTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                TestSynthetic(root);
                TestRealScan(args.Length > 0 ? args[0] : "",
                    args.Length > 1 ? args[1] : "");
                Console.WriteLine("TOTAL " + count + " PASSED");
                return 0;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
