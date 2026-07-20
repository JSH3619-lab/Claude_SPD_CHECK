using System;
using System.Drawing;
using System.Windows.Forms;

namespace SPD_Checker.Forms
{
    // 헤더 우상단 [Mode ▾] 드롭다운 — 3개 폼 공유
    // currentMode 항목은 비활성화·체크 표시.
    // beforeSwitch: 전환 전 검증(예: dirty 확인). false 반환 시 전환 취소.
    public static class ModeDropdown
    {
        public static Button Create(
            AppMode currentMode,
            Action<AppMode> onSelect,
            Func<bool> beforeSwitch = null)
        {
            var btn = new Button
            {
                Text      = "Mode ▾",
                Dock      = DockStyle.Right,
                Width     = 110,
                Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(50, 80, 120),
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand,
                TabStop   = false
            };
            btn.FlatAppearance.BorderSize = 0;

            var menu = new ContextMenuStrip();
            menu.Items.Add(BuildItem("✅  Check",     AppMode.Check,   currentMode, onSelect, beforeSwitch));
            menu.Items.Add(BuildItem("✏  Editor",    AppMode.Editor,  currentMode, onSelect, beforeSwitch));
            menu.Items.Add(BuildItem("🤖  Auto-Gen", AppMode.AutoGen, currentMode, onSelect, beforeSwitch));
            menu.Items.Add(new ToolStripSeparator());
            var rulesItem = new ToolStripMenuItem("⚙  규칙 설정") { Font = new Font("Segoe UI", 9.5F) };
            rulesItem.Click += (s, e) => { using (var f = new RulesForm()) f.ShowDialog(btn.FindForm()); };
            menu.Items.Add(rulesItem);

            btn.Click += (s, e) => menu.Show(btn, new Point(0, btn.Height));
            return btn;
        }

        private static ToolStripMenuItem BuildItem(
            string label, AppMode target, AppMode current,
            Action<AppMode> onSelect, Func<bool> beforeSwitch)
        {
            string text = target == current ? label + "   (현재)" : label;
            var item = new ToolStripMenuItem(text)
            {
                Enabled = target != current,
                Checked = target == current,
                Font    = new Font("Segoe UI", 9.5F)
            };
            if (target != current)
                item.Click += (s, e) =>
                {
                    if (beforeSwitch != null && !beforeSwitch()) return;
                    onSelect(target);
                };
            return item;
        }
    }
}
