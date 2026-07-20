using System;
using System.Drawing;
using System.Windows.Forms;
using SPD_Checker.Logic;

namespace SPD_Checker.Forms
{
    // Phase 1: 규칙 뷰어 (읽기전용). 현재 하드코딩된 규칙을 표시만 함.
    // 편집·저장·일괄 적용은 Phase 2~4에서 추가.
    public class RulesForm : Form
    {
        public RulesForm()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            Text            = "DDR5 SPD Studio — 규칙 설정 (읽기전용)";
            ClientSize      = new Size(680, 620);
            StartPosition   = FormStartPosition.CenterParent;
            Font            = new Font("Segoe UI", 9F);
            BackColor       = Color.FromArgb(245, 246, 248);
            MinimumSize     = new Size(560, 480);

            // Header
            var header = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 57, 95) };
            header.Controls.Add(new Label
            {
                Text      = "⚙  규칙 설정 (Rules)  —  읽기전용",
                Font      = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.White,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(16, 0, 0, 0)
            });

            // Scope bar
            var scope = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 32,
                Text      = "  스코프: UDIMM / SODIMM (Client, XMP 포함)     ·     RDIMM · CUDIMM/CSODIMM = 향후 확장",
                ForeColor = Color.FromArgb(70, 70, 70),
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 9F)
            };

            // Bottom note
            var note = new Label
            {
                Dock      = DockStyle.Bottom,
                Height    = 38,
                Text      = "  읽기전용 뷰어입니다. 편집 · 저장 · 일괄 적용은 다음 단계(Phase 2~4)에서 추가됩니다.",
                ForeColor = Color.FromArgb(110, 110, 110),
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 8.5F),
                BackColor = Color.FromArgb(238, 240, 243)
            };

            // Content (2 sections)
            var root = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 1,
                RowCount    = 4,
                Padding     = new Padding(12, 8, 12, 8),
                BackColor   = Color.FromArgb(245, 246, 248)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 54));

            var gridId = MakeGrid(new (string, int)[] { ("항목", 130), ("Byte", 60), ("값", 260), ("근거", 140) });
            foreach (var r in SpdChecker.GetIdentityRules()) gridId.Rows.Add((object[])r);

            var gridTm = MakeGrid(new (string, int)[] { ("속도", 150), ("코드", 60), ("CL-tRCD-tRP", 130), ("tCK", 90) });
            foreach (var r in SpdParser.GetTimingRules()) gridTm.Rows.Add((object[])r);

            root.Controls.Add(MakeSectionLabel("식별 규칙 (Identity)"), 0, 0);
            root.Controls.Add(gridId, 0, 1);
            root.Controls.Add(MakeSectionLabel("타이밍 디폴트 규칙 (JEDEC POD / XMP — CL-tRCD-tRP)"), 0, 2);
            root.Controls.Add(gridTm, 0, 3);

            Controls.Add(root);
            Controls.Add(note);
            Controls.Add(scope);
            Controls.Add(header);
        }

        private static Label MakeSectionLabel(string text) => new Label
        {
            Dock      = DockStyle.Fill,
            Text      = text,
            Font      = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 40, 40),
            TextAlign = ContentAlignment.MiddleLeft
        };

        private static DataGridView MakeGrid((string Name, int Weight)[] cols)
        {
            var g = new DataGridView
            {
                Dock                      = DockStyle.Fill,
                BackgroundColor           = Color.White,
                BorderStyle               = BorderStyle.FixedSingle,
                RowHeadersVisible         = false,
                AllowUserToAddRows        = false,
                AllowUserToDeleteRows     = false,
                AllowUserToResizeRows     = false,
                ReadOnly                  = true,
                SelectionMode             = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode       = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode          = DataGridViewAutoSizeRowsMode.AllCells,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight       = 30,
                EnableHeadersVisualStyles = false,
                GridColor                 = Color.FromArgb(224, 227, 231)
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(28, 57, 95);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            g.ColumnHeadersDefaultCellStyle.Font      = new Font("Segoe UI", 9F, FontStyle.Bold);
            g.DefaultCellStyle.Font                   = new Font("Consolas", 9F);
            g.DefaultCellStyle.WrapMode               = DataGridViewTriState.True;
            g.DefaultCellStyle.SelectionBackColor     = Color.FromArgb(210, 224, 244);
            g.DefaultCellStyle.SelectionForeColor     = Color.Black;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            foreach (var (name, weight) in cols)
                g.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = name,
                    FillWeight = weight,
                    SortMode   = DataGridViewColumnSortMode.NotSortable
                });
            return g;
        }
    }
}
