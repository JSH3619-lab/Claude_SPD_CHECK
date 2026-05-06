using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using SPD_Checker.Models;

namespace SPD_Checker.Forms
{
    // Sci-fi HUD callout tooltip — shows over Key Byte cells in SpdEditorForm
    internal class HudTooltipForm : Form
    {
        // ── 투명 처리용 키 컬러 (이 색상 픽셀은 click-through + 투명 처리)
        private static readonly Color KEY_COLOR = Color.FromArgb(1, 2, 3);

        // ── 팔레트
        private static readonly Color BG_COLOR      = Color.FromArgb(8, 16, 32);
        private static readonly Color BORDER_HI     = Color.FromArgb(66, 135, 245);
        private static readonly Color BORDER_MID    = Color.FromArgb(40, 90, 180);
        private static readonly Color BORDER_LO     = Color.FromArgb(20, 50, 110);
        private static readonly Color CORNER_COLOR  = Color.FromArgb(130, 180, 255);
        private static readonly Color HEADER_COLOR  = Color.FromArgb(100, 195, 255);
        private static readonly Color ADDR_COLOR    = Color.FromArgb(120, 155, 205);
        private static readonly Color VALUE_COLOR   = Color.FromArgb(220, 235, 255);
        private static readonly Color MEANING_COLOR = Color.FromArgb(155, 195, 240);
        private static readonly Color PASS_COLOR    = Color.FromArgb(0, 220, 110);
        private static readonly Color FAIL_COLOR    = Color.FromArgb(255, 75, 75);
        private static readonly Color SEP_COLOR     = Color.FromArgb(35, 75, 145);
        private static readonly Color ARROW_COLOR   = Color.FromArgb(66, 135, 245);

        // ── 박스 크기
        private const int BOX_W    = 256;
        private const int BOX_H    = 126;
        private const int PAD      = 13;
        private const int CORNER_R = 5;
        private const int BRACKET  = 10;    // 코너 브라켓 길이

        // ── 레이아웃 (form 내 상대 좌표)
        private Point _boxPt;       // 박스 top-left
        private Point _arrowTip;    // 화살표 끝 (셀 방향)

        // ── 콘텐츠
        private string _addrStr;
        private string _fieldName;
        private string _rawVal;
        private string _meaning;
        private bool?  _pass;

        public HudTooltipForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.Manual;
            BackColor       = KEY_COLOR;
            TransparencyKey = KEY_COLOR;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        // 포커스 탈취 방지
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }

        // ── 외부에서 호출: 셀 우상단 스크린 좌표 (anchor) + 표시 내용 전달 ──
        // anchor = 클릭된 셀의 우상단 모서리 (스크린 좌표)
        public void ShowAt(Point anchor,
                           string addrStr, string fieldName,
                           string rawVal, string meaning, bool? pass)
        {
            _addrStr   = addrStr;
            _fieldName = fieldName;
            _rawVal    = rawVal;
            _meaning   = meaning;
            _pass      = pass;

            const int GAP    = 8;    // 셀과 박스 사이 여백
            const int MARGIN = 6;   // form 여백

            var screen = Screen.FromPoint(anchor).WorkingArea;

            // 기본: 박스는 anchor 우측 + 살짝 위 (셀 우상단 기준)
            int boxScreenX = anchor.X + GAP;
            int boxScreenY = anchor.Y - BOX_H / 2;

            // 오른쪽 공간 부족 → 왼쪽으로
            if (boxScreenX + BOX_W > screen.Right)
                boxScreenX = anchor.X - GAP - BOX_W;

            // 위쪽 초과 → 내려서 맞춤
            if (boxScreenY < screen.Top)
                boxScreenY = screen.Top + MARGIN;

            // 아래쪽 초과 → 올려서 맞춤
            if (boxScreenY + BOX_H > screen.Bottom)
                boxScreenY = screen.Bottom - BOX_H - MARGIN;

            // 화살표 끝: anchor (셀 경계)
            int formLeft   = Math.Min(anchor.X, boxScreenX) - MARGIN;
            int formTop    = Math.Min(anchor.Y, boxScreenY) - MARGIN;
            int formRight  = Math.Max(anchor.X, boxScreenX + BOX_W) + MARGIN;
            int formBottom = Math.Max(anchor.Y, boxScreenY + BOX_H) + MARGIN;

            _boxPt    = new Point(boxScreenX - formLeft, boxScreenY - formTop);
            _arrowTip = new Point(anchor.X - formLeft, anchor.Y - formTop);

            SetBounds(formLeft, formTop, formRight - formLeft, formBottom - formTop);
            if (!Visible) Show();
            Invalidate();
            Update();
        }

        public new void Hide()
        {
            if (Visible) base.Hide();
        }

        // ── 페인팅 ──────────────────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode      = SmoothingMode.AntiAlias;
            g.TextRenderingHint  = TextRenderingHint.ClearTypeGridFit;

            var boxRect = new Rectangle(_boxPt.X, _boxPt.Y, BOX_W, BOX_H);

            // 화살표 기준점: 박스 테두리에서 arrowTip 방향으로 가장 가까운 점
            Point arrowBase = BorderIntersect(boxRect, _arrowTip);

            DrawArrow(g, arrowBase, _arrowTip);
            DrawBox(g, boxRect);
        }

        // ── 박스 그리기 ──────────────────────────────────────────────────────
        private void DrawBox(Graphics g, Rectangle r)
        {
            // 글로우 (3겹 테두리, 바깥→안)
            for (int i = 3; i >= 1; i--)
            {
                var glow = i == 3 ? BORDER_LO : i == 2 ? BORDER_MID : BORDER_HI;
                using (var pen = new Pen(glow, i == 3 ? 5f : i == 2 ? 3f : 1.8f))
                using (var path = RoundedRect(r.X - i, r.Y - i, r.Width + i * 2, r.Height + i * 2, CORNER_R + i))
                    g.DrawPath(pen, path);
            }

            // 배경
            using (var brush = new SolidBrush(BG_COLOR))
            using (var path  = RoundedRect(r.X, r.Y, r.Width, r.Height, CORNER_R))
                g.FillPath(brush, path);

            // 메인 테두리
            using (var pen  = new Pen(BORDER_HI, 1.5f))
            using (var path = RoundedRect(r.X, r.Y, r.Width, r.Height, CORNER_R))
                g.DrawPath(pen, path);

            // 코너 브라켓
            DrawCornerBrackets(g, r);

            // 텍스트
            DrawContent(g, r);
        }

        private void DrawContent(Graphics g, Rectangle r)
        {
            int tx = r.X + PAD;
            int ty = r.Y + PAD;
            int tw = r.Width - PAD * 2;

            // 주소 라벨
            using (var f = new Font("Consolas", 7.5f, FontStyle.Bold))
            using (var b = new SolidBrush(ADDR_COLOR))
                g.DrawString($"[ {_addrStr} ]", f, b, tx, ty);
            ty += 15;

            // 구분선
            using (var pen = new Pen(SEP_COLOR, 1f))
                g.DrawLine(pen, tx, ty, tx + tw, ty);
            ty += 6;

            // 필드명
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var b = new SolidBrush(HEADER_COLOR))
                g.DrawString(_fieldName, f, b, tx, ty);
            ty += 19;

            // 원시값
            if (!string.IsNullOrEmpty(_rawVal))
            {
                using (var f = new Font("Consolas", 8.5f))
                using (var b = new SolidBrush(VALUE_COLOR))
                    g.DrawString(_rawVal, f, b, tx, ty);
                ty += 17;
            }

            // 의미 (디코딩)
            if (!string.IsNullOrEmpty(_meaning))
            {
                using (var f = new Font("Segoe UI", 8.5f))
                using (var b = new SolidBrush(MEANING_COLOR))
                    g.DrawString(_meaning, f, b, tx, ty);
                ty += 17;
            }

            // 상태
            if (_pass.HasValue)
            {
                string label = _pass.Value ? "✓  PASS" : "✗  FAIL";
                Color  col   = _pass.Value ? PASS_COLOR : FAIL_COLOR;
                using (var f = new Font("Segoe UI", 8f, FontStyle.Bold))
                using (var b = new SolidBrush(col))
                    g.DrawString(label, f, b, tx, ty);
            }
        }

        // ── 화살표 그리기 ────────────────────────────────────────────────────
        private static void DrawArrow(Graphics g, Point from, Point to)
        {
            // 점선 몸통
            using (var pen = new Pen(ARROW_COLOR, 1.5f))
            {
                pen.DashStyle   = DashStyle.Custom;
                pen.DashPattern = new float[] { 5f, 3f };
                g.DrawLine(pen, from, to);
            }

            // 화살촉 (채운 삼각형)
            double angle  = Math.Atan2(to.Y - from.Y, to.X - from.X);
            double spread = Math.PI / 6.0;
            int    size   = 7;
            var p1 = new PointF(to.X - size * (float)Math.Cos(angle - spread),
                                to.Y - size * (float)Math.Sin(angle - spread));
            var p2 = new PointF(to.X - size * (float)Math.Cos(angle + spread),
                                to.Y - size * (float)Math.Sin(angle + spread));
            using (var brush = new SolidBrush(ARROW_COLOR))
                g.FillPolygon(brush, new PointF[] { new PointF(to.X, to.Y), p1, p2 });
        }

        // ── 코너 브라켓 ──────────────────────────────────────────────────────
        private static void DrawCornerBrackets(Graphics g, Rectangle r)
        {
            using (var pen = new Pen(CORNER_COLOR, 1.8f))
            {
                int x0 = r.X + 2, x1 = r.Right - 2;
                int y0 = r.Y + 2, y1 = r.Bottom - 2;
                // 좌상
                g.DrawLines(pen, new[] { new Point(x0, y0 + BRACKET), new Point(x0, y0), new Point(x0 + BRACKET, y0) });
                // 우상
                g.DrawLines(pen, new[] { new Point(x1 - BRACKET, y0), new Point(x1, y0), new Point(x1, y0 + BRACKET) });
                // 좌하
                g.DrawLines(pen, new[] { new Point(x0, y1 - BRACKET), new Point(x0, y1), new Point(x0 + BRACKET, y1) });
                // 우하
                g.DrawLines(pen, new[] { new Point(x1 - BRACKET, y1), new Point(x1, y1), new Point(x1, y1 - BRACKET) });
            }
        }

        // ── 헬퍼 ─────────────────────────────────────────────────────────────
        // 박스 테두리와 직선 교점 (화살표 출발점)
        private static Point BorderIntersect(Rectangle r, Point tip)
        {
            var  cx = r.X + r.Width  / 2f;
            var  cy = r.Y + r.Height / 2f;
            float dx = tip.X - cx, dy = tip.Y - cy;
            if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1) return new Point(r.X, r.Bottom / 2);

            float scaleX = dx != 0 ? (dx > 0 ? (r.Right  - cx) / dx : (r.Left - cx) / dx) : float.MaxValue;
            float scaleY = dy != 0 ? (dy > 0 ? (r.Bottom - cy) / dy : (r.Top  - cy) / dy) : float.MaxValue;
            float s      = Math.Min(scaleX, scaleY);
            return new Point((int)(cx + dx * s), (int)(cy + dy * s));
        }

        private static GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
