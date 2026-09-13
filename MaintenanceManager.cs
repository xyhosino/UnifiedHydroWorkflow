using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnifiedHydroLauncher
{
    internal static class MaintenanceManager
    {
        internal static string RecoverInterruptedRollback(string modelRoot)
        {
            if (string.IsNullOrWhiteSpace(modelRoot) || !Directory.Exists(modelRoot))
                return "";
            string backupDir = Path.Combine(modelRoot, "unified_workflow_backups");
            string rollback = Path.Combine(backupDir, "project_rollback_current.db");
            string database = Path.Combine(modelRoot, "project.db");
            if (!File.Exists(rollback)) return "";

            try
            {
                File.Copy(rollback, database, true);
                string lastGood = Path.Combine(backupDir, "project_last_good.db");
                MoveOrReplace(rollback, lastGood);
                return "DATABASE_INTERRUPTED_ROLLBACK_RECOVERED " + database +
                    " backup=" + lastGood;
            }
            catch (Exception exception)
            {
                return "DATABASE_INTERRUPTED_ROLLBACK_FAILED backup=" + rollback +
                    " error=" + exception.Message;
            }
        }

        internal static string NormalizeDatabaseBackups(string modelRoot)
        {
            if (string.IsNullOrWhiteSpace(modelRoot) || !Directory.Exists(modelRoot))
                return "";

            string backupDir = Path.Combine(modelRoot, "unified_workflow_backups");
            if (!Directory.Exists(backupDir))
                return "";

            try
            {
                string lastGood = Path.Combine(backupDir, "project_last_good.db");
                List<string> legacy = Directory.GetFiles(
                    backupDir,
                    "project_before_unified_*.db",
                    SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                    .ToList();

                int removed = 0;
                if (!File.Exists(lastGood) && legacy.Count > 0)
                {
                    MoveOrReplace(legacy[0], lastGood);
                    legacy.RemoveAt(0);
                }

                foreach (string path in legacy)
                {
                    TryDelete(path);
                    if (!File.Exists(path)) removed++;
                }

                // A stale rollback file can only be left by an interrupted older run.
                // Do not delete it automatically: it may be the only recovery copy.
                string rollback = Path.Combine(backupDir, "project_rollback_current.db");
                string rollbackNote = File.Exists(rollback)
                    ? " rollback_pending=1"
                    : " rollback_pending=0";

                return "BACKUP_MAINTENANCE kept_last_good=" +
                    (File.Exists(lastGood) ? "1" : "0") +
                    " removed_legacy=" + removed + rollbackNote;
            }
            catch (Exception exception)
            {
                return "BACKUP_MAINTENANCE_WARNING " + exception.Message;
            }
        }

        internal static string FinalizeSuccessfulRun(
            string modelRoot,
            string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                return NormalizeDatabaseBackups(modelRoot);

            try
            {
                string backupDir = Path.Combine(modelRoot, "unified_workflow_backups");
                Directory.CreateDirectory(backupDir);
                string lastGood = Path.Combine(backupDir, "project_last_good.db");

                if (!PathsEqual(backupPath, lastGood))
                    MoveOrReplace(backupPath, lastGood);

                string cleanup = NormalizeDatabaseBackups(modelRoot);
                return "DATABASE_BACKUP_COMMITTED " + lastGood +
                    (cleanup.Length == 0 ? "" : "\r\n" + cleanup);
            }
            catch (Exception exception)
            {
                return "DATABASE_BACKUP_COMMIT_WARNING " + exception.Message;
            }
        }

        internal static string RestoreFailedRun(
            string modelRoot,
            string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                return "DATABASE_ROLLBACK_SKIPPED no_backup_created";

            string database = Path.Combine(modelRoot, "project.db");
            try
            {
                File.Copy(backupPath, database, true);
                string backupDir = Path.Combine(modelRoot, "unified_workflow_backups");
                Directory.CreateDirectory(backupDir);
                string lastGood = Path.Combine(backupDir, "project_last_good.db");
                if (!PathsEqual(backupPath, lastGood))
                    MoveOrReplace(backupPath, lastGood);
                string cleanup = NormalizeDatabaseBackups(modelRoot);
                return "DATABASE_ROLLBACK_OK restored=" + database +
                    " backup=" + lastGood +
                    (cleanup.Length == 0 ? "" : "\r\n" + cleanup);
            }
            catch (Exception exception)
            {
                return "DATABASE_ROLLBACK_FAILED backup=" + backupPath +
                    " error=" + exception.Message;
            }
        }

        internal static string CleanupKnownTemporaryFiles(string unitRoot)
        {
            if (string.IsNullOrWhiteSpace(unitRoot) || !Directory.Exists(unitRoot))
                return "";

            string[] rootBases =
            {
                "temp_rivl_assigned",
                "temp_node_assigned",
                "temp_sj_wata",
                "temp_sj_rivl"
            };
            string[] unitBases =
            {
                "__uh_soil_clip",
                "__uh_land_clip"
            };
            string[] extensions =
            {
                ".shp", ".shx", ".dbf", ".prj", ".cpg",
                ".sbn", ".sbx", ".qix", ".shp.xml"
            };

            int removed = 0;
            try
            {
                foreach (string name in rootBases)
                    removed += DeleteBundle(unitRoot, name, extensions);

                foreach (string folder in Directory.GetDirectories(unitRoot))
                {
                    foreach (string name in unitBases)
                        removed += DeleteBundle(folder, name, extensions);
                }

                return removed == 0
                    ? "CACHE_CLEANUP clean"
                    : "CACHE_CLEANUP removed_files=" + removed;
            }
            catch (Exception exception)
            {
                return "CACHE_CLEANUP_WARNING " + exception.Message;
            }
        }

        private static int DeleteBundle(
            string folder,
            string baseName,
            IEnumerable<string> extensions)
        {
            int count = 0;
            foreach (string extension in extensions)
            {
                string path = Path.Combine(folder, baseName + extension);
                if (!File.Exists(path)) continue;
                TryDelete(path);
                if (!File.Exists(path)) count++;
            }
            return count;
        }

        private static void MoveOrReplace(string source, string destination)
        {
            if (PathsEqual(source, destination)) return;
            if (File.Exists(destination)) File.Delete(destination);
            try
            {
                File.Move(source, destination);
            }
            catch (IOException)
            {
                File.Copy(source, destination, true);
                File.Delete(source);
            }
        }

        private static bool PathsEqual(string first, string second)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(first),
                    Path.GetFullPath(second),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Caller reports remaining files through its summary; cleanup is best effort.
            }
        }
    }
}
