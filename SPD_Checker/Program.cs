using System;
using System.Windows.Forms;
using SPD_Checker.Forms;
using SPD_Checker.Logic;

namespace SPD_Checker
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppLogger.Init();
            RulesConfig.Init();   // 식별 규칙 로드 (rules.json, 없으면 기본값 생성)

            Application.ThreadException += (s, e) =>
            {
                AppLogger.Fatal("ThreadException", "UI 스레드 예외", e.Exception);
                ShowFatalDialog(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                AppLogger.Fatal("UnhandledException", $"비UI 예외 (IsTerminating={e.IsTerminating})", ex);
                ShowFatalDialog(ex);
            };

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
                        if (launch.ShowDialog() != DialogResult.OK)
                        {
                            AppLogger.Info("Program", "==== SPD Studio 종료 (LaunchForm 취소) ====");
                            return;
                        }
                        mode = launch.SelectedMode;
                        AppLogger.Info("Program", $"모드 시작: {mode}");
                    }
                }

                AppMode? nextMode = null;
                string  nextPath   = null;
                int?    nextOffset = null;

                try
                {
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
                }
                catch (Exception ex)
                {
                    AppLogger.Fatal("Program", $"{mode} 모드 실행 중 예외", ex);
                    ShowFatalDialog(ex);
                    return;
                }

                if (!nextMode.HasValue)
                {
                    AppLogger.Info("Program", "==== SPD Studio 종료 ====");
                    return;
                }
                AppLogger.Info("Program", $"모드 전환: {mode} → {nextMode}");
                mode       = nextMode;
                jumpPath   = nextPath;
                jumpOffset = nextOffset;
            }
        }

        private static void ShowFatalDialog(Exception ex)
        {
            try
            {
                string logDir = AppLogger.LogDirectory ?? "(로그 미초기화)";
                MessageBox.Show(
                    $"예기치 못한 오류가 발생했습니다.\n\n{ex?.GetType().Name}: {ex?.Message}\n\n로그 위치:\n{logDir}",
                    "SPD Studio — 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
