using System;
using System.Windows.Forms;
using SPD_Checker.Forms;

namespace SPD_Checker
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppMode? mode = null;
            string  jumpPath   = null;
            int?    jumpOffset = null;

            while (true)
            {
                if (mode == null)
                {
                    using (var launch = new LaunchForm())
                    {
                        if (launch.ShowDialog() != DialogResult.OK) return;
                        mode = launch.SelectedMode;
                    }
                }

                AppMode? nextMode = null;
                string  nextPath   = null;
                int?    nextOffset = null;

                switch (mode.Value)
                {
                    case AppMode.Check:
                        var f = new MainForm();
                        Application.Run(f);
                        nextMode   = f.NextMode;
                        nextPath   = f.JumpFilePath;
                        nextOffset = f.JumpOffset;
                        break;

                    case AppMode.Editor:
                        var ed = new SpdEditorForm(jumpPath, jumpOffset);
                        Application.Run(ed);
                        nextMode = ed.NextMode;
                        break;

                    case AppMode.AutoGen:
                        var ag = new AutoGenForm();
                        Application.Run(ag);
                        nextMode = ag.NextMode;
                        break;
                }

                if (!nextMode.HasValue) return;
                mode       = nextMode;
                jumpPath   = nextPath;
                jumpOffset = nextOffset;
            }
        }
    }
}
