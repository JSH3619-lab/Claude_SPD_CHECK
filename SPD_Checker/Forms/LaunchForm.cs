using System;
using System.Drawing;
using System.Windows.Forms;

namespace SPD_Checker.Forms
{
    public enum AppMode
    {
        Check,
        Editor,
        AutoGen
    }

    public class LaunchForm : Form
    {
        public AppMode SelectedMode { get; private set; }

        public LaunchForm()
        {
            BuildUI();
        }

        // ── UI Construction ──────────────────────────────────────────────────
        private void BuildUI()
        {
            Text            = "DDR5 SPD Tool — 모드 선택";
            ClientSize      = new Size(620, 320);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            Font            = new Font("Segoe UI", 9F);
            BackColor       = Color.FromArgb(245, 246, 248);

            // Header
            var header = new Panel
            {
                Location  = new Point(0, 0),
                Size      = new Size(620, 60),
                BackColor = Color.FromArgb(28, 57, 95)
            };
            header.Controls.Add(new Label
            {
                Text      = "DDR5 SPD Tool  v2.0",
                Font      = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            });
            Controls.Add(header);

            // Subtitle
            Controls.Add(new Label
            {
                Text      = "사용할 모드를 선택하세요",
                Font      = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location  = new Point(0, 60),
                Size      = new Size(620, 36),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // Three mode buttons
            var btnCheck = MakeModeButton(
                "✅\n\nCheck Mode\n\n다중 파일 일괄 검증",
                Color.FromArgb(34, 153, 60),
                new Point(20, 110));
            btnCheck.Click += (s, e) => SelectMode(AppMode.Check);

            var btnEditor = MakeModeButton(
                "✏\n\nEditor Mode\n\nSPD 편집 · CRC · Auto-Fix",
                Color.FromArgb(65, 125, 190),
                new Point(220, 110));
            btnEditor.Click += (s, e) => SelectMode(AppMode.Editor);

            var btnAutoGen = MakeModeButton(
                "🤖\n\nAuto-Gen\n\nQR 스캔 자동 생성 (작업자)",
                Color.FromArgb(170, 70, 20),
                new Point(420, 110));
            btnAutoGen.Click += (s, e) => ShowComingSoon(
                "Auto-Gen Mode은 추후 단계(E-6)에서 구현됩니다.");

            Controls.Add(btnCheck);
            Controls.Add(btnEditor);
            Controls.Add(btnAutoGen);
        }

        private Button MakeModeButton(string text, Color color, Point location)
        {
            var btn = new Button
            {
                Text      = text,
                Location  = location,
                Size      = new Size(180, 190),
                Font      = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = color,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor    = Cursors.Hand,
                UseCompatibleTextRendering = false
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void SelectMode(AppMode mode)
        {
            SelectedMode = mode;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static void ShowComingSoon(string message)
        {
            MessageBox.Show(message, "준비 중",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
