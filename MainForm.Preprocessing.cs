using System;
using System.IO;
using System.Windows.Forms;

namespace UnifiedHydroLauncher
{
    public sealed partial class MainForm
    {
        private Button btnPreprocess;

        private void BtnPreprocess_Click(object sender, EventArgs e)
        {
            if (_isRunning) return;
            using (var dialog = new PreprocessingForm())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || !dialog.Succeeded)
                    return;

                string outputRoot = dialog.OutputRoot;
                if (Directory.Exists(outputRoot))
                {
                    txtUnitRoot.Text = Path.GetFullPath(outputRoot);
                    AppendLog("PREPROCESS_LOADED_ROOT " + Path.GetFullPath(outputRoot));
                    AppendLog("");
                    ScanUnits();
                }
            }
        }
    }
}
