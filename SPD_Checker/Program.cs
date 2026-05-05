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

            while (true)
            {
                AppMode mode;
                using (var launch = new LaunchForm())
                {
                    if (launch.ShowDialog() != DialogResult.OK)
                        return;
                    mode = launch.SelectedMode;
                }

                bool switchRequested = false;
                switch (mode)
                {
                    case AppMode.Check:
                        var f = new MainForm();
                        Application.Run(f);
                        switchRequested = f.SwitchRequested;
                        break;

                    case AppMode.Editor:
                        var ed = new SpdEditorForm();
                        Application.Run(ed);
                        switchRequested = ed.SwitchRequested;
                        break;

                    case AppMode.AutoGen:
                        var ag = new AutoGenForm();
                        Application.Run(ag);
                        switchRequested = ag.SwitchRequested;
                        break;
                }

                if (!switchRequested) return;
            }
        }
    }
}
