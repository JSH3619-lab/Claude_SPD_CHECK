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
            Text            = "DDR5 SPD Studio — 모드 선택";
            ClientSize      = new Size(720, 320);
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
                Size      = new Size(720, 60),
                BackColor = Color.FromArgb(28, 57, 95)
            };
            header.Controls.Add(new Label
            {
                Text      = "DDR5 SPD Studio  v2.1",
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
                Size      = new Size(720, 36),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // Three mode panels (x: 25 / 255 / 485 — gap 20px, margin 25px)
            Controls.Add(MakeModePanel("✅", "Check Mode",  "다중 파일 일괄 검증",
                Color.FromArgb(34, 153, 60),  new Point(25,  110), () => SelectMode(AppMode.Check)));
            Controls.Add(MakeModePanel("✏",  "Editor Mode", "SPD 편집 · CRC · Auto-Fix",
                Color.FromArgb(65, 125, 190), new Point(255, 110), () => SelectMode(AppMode.Editor)));
            Controls.Add(MakeModePanel("🤖", "Auto-Gen",   "QR 스캔 자동 생성 (작업자)",
                Color.FromArgb(170, 70, 20),  new Point(485, 110), () => SelectMode(AppMode.AutoGen)));
        }

        private Panel MakeModePanel(string icon, string title, string subtitle, Color bg, Point loc, Action onClick)
        {
            Color bgHover = Color.FromArgb(
                Math.Max(0, bg.R - 22),
                Math.Max(0, bg.G - 22),
                Math.Max(0, bg.B - 22));

            var pnl = new Panel { Location = loc, Size = new Size(210, 190), BackColor = bg, Cursor = Cursors.Hand };

            var lblIcon  = MakePanelLabel(icon,     new Font("Segoe UI", 26F),                    Color.White,                   0,   12, 210, 62);
            var lblTitle = MakePanelLabel(title,    new Font("Segoe UI", 12F, FontStyle.Bold),    Color.White,                   0,   78, 210, 30);
            var lblSub   = MakePanelLabel(subtitle, new Font("Segoe UI", 9F),                     Color.FromArgb(215, 215, 215), 5,  114, 200, 62);
            lblSub.TextAlign = ContentAlignment.TopCenter;

            pnl.Controls.AddRange(new Control[] { lblIcon, lblTitle, lblSub });

            // Click 이벤트 — 자식 Label 클릭도 패널까지 전달
            pnl.Click += (s, e) => onClick();
            foreach (Control c in pnl.Controls)
                c.Click += (s, e) => onClick();

            // Hover — 자식에서 패널 내부로 이동 시 색상 유지
            EventHandler enterH = (s, e) => pnl.BackColor = bgHover;
            EventHandler leaveH = (s, e) =>
            {
                if (pnl.Parent == null) return;
                Point cur = pnl.Parent.PointToClient(Cursor.Position);
                if (!new Rectangle(pnl.Location, pnl.Size).Contains(cur))
                    pnl.BackColor = bg;
            };
            pnl.MouseEnter += enterH;
            pnl.MouseLeave += leaveH;
            foreach (Control c in pnl.Controls)
            {
                c.MouseEnter += enterH;
                c.MouseLeave += leaveH;
            }

            return pnl;
        }

        private static Label MakePanelLabel(string text, Font font, Color fore, int x, int y, int w, int h)
        {
            return new Label
            {
                Text      = text,
                Font      = font,
                ForeColor = fore,
                Location  = new Point(x, y),
                Size      = new Size(w, h),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize  = false
            };
        }

        private void SelectMode(AppMode mode)
        {
            SelectedMode = mode;
            DialogResult = DialogResult.OK;
            Close();
        }

    }
}
