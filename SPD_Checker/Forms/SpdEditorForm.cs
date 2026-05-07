using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SPD_Checker.Logic;
using SPD_Checker.Models;

namespace SPD_Checker.Forms
{
    public class SpdEditorForm : Form
    {
        private const int SPD_SIZE = SpdParser.SPD_FULL_SIZE;
        private const int COLS = 16;
        private const int ROWS = SPD_SIZE / COLS;   // 64

        // ── State ────────────────────────────────────────────────────────────
        private byte[]  _data         = new byte[SPD_SIZE];
        private byte[]  _originalData = new byte[SPD_SIZE]; // 마지막 로드/저장 시점 스냅샷
        private SpdInfo _info;
        private string  _filePath;
        private bool    _dirty;
        private Dictionary<string, CheckResult> _checkResults = new Dictionary<string, CheckResult>();

        // ── UI ───────────────────────────────────────────────────────────────
        private Button         _btnRevert;
        private DataGridView   _hexGrid;
        private Panel          _rightPanel;
        private Label          _rightInfoLabel;
        private Label          _statusLabel;
        private HudTooltipForm _hudTooltip;

        // Part Info 입력 컨트롤 (E-3.5 단계 1: PartNo TextBox만 활성)
        private TextBox  _partNoTextBox;
        private ComboBox _sourcingCombo;
        private ComboBox _dimmTypeCombo;
        private ComboBox _densityCombo;
        private ComboBox _bankCombo;
        private ComboBox _compCombo;
        private ComboBox _dieDensityCombo;
        private ComboBox _rankCombo;
        private ComboBox _dramMfrCombo;
        private ComboBox _speedCombo;
        private bool     _suppressEvents;

        public AppMode? NextMode { get; private set; }

        // ── Key Byte 그룹 (관련 바이트 묶기 + 의미 디코딩) ──────────────────
        private struct KeyByteGroup
        {
            public int    Offset;
            public int    Length;
            public string Name;
            public string CheckItem;   // SpdChecker 결과의 CheckItem 이름 (null = 검증 없음)
            public Func<byte[], int, (string Raw, string Meaning)> Decode;
        }

        private static readonly KeyByteGroup[] KEY_BYTE_GROUPS = new[]
        {
            new KeyByteGroup { Offset = 2,   Length = 1, Name = "DRAM Type",      CheckItem = "DRAM Type",              Decode = DecDramType    },
            new KeyByteGroup { Offset = 3,   Length = 1, Name = "Module Type",    CheckItem = "Module Type",            Decode = DecModuleType  },
            new KeyByteGroup { Offset = 4,   Length = 1, Name = "Die Density",    CheckItem = "Die Density",            Decode = DecDieDensity  },
            new KeyByteGroup { Offset = 6,   Length = 1, Name = "I/O Width",      CheckItem = "I/O Width",              Decode = DecIoWidth     },
            new KeyByteGroup { Offset = 7,   Length = 1, Name = "Bank Groups",    CheckItem = "Bank Groups",            Decode = DecBank        },
            new KeyByteGroup { Offset = 16,  Length = 1, Name = "VDD Nominal",    CheckItem = "VDD Nominal",            Decode = DecVdd         },
            new KeyByteGroup { Offset = 20,  Length = 2, Name = "tCKAVGmin",      CheckItem = "tCKAVGmin",              Decode = DecTck         },
            new KeyByteGroup { Offset = 30,  Length = 2, Name = "tAAmin",         CheckItem = "tAA min",                Decode = DecTimingPs    },
            new KeyByteGroup { Offset = 32,  Length = 2, Name = "tRCDmin",        CheckItem = "tRCD min",               Decode = DecTimingPs    },
            new KeyByteGroup { Offset = 34,  Length = 2, Name = "tRPmin",         CheckItem = "tRP min",                Decode = DecTimingPs    },
            new KeyByteGroup { Offset = 234, Length = 1, Name = "Module Rank",    CheckItem = "Module Rank",            Decode = DecRank        },
            new KeyByteGroup { Offset = 510, Length = 2, Name = "JEDEC CRC",      CheckItem = "CRC",                    Decode = DecCrcLE       },
            new KeyByteGroup { Offset = 512, Length = 2,  Name = "Module Mfr ID",  CheckItem = "Module Mfr ID",          Decode = DecMfr         },
            new KeyByteGroup { Offset = 521, Length = 30, Name = "Part Number",    CheckItem = "Part Number",            Decode = DecPartNumber  },
            new KeyByteGroup { Offset = 552, Length = 2,  Name = "DRAM Mfr ID",   CheckItem = "DRAM Mfr ID",            Decode = DecMfr         },
            new KeyByteGroup { Offset = 640, Length = 3, Name = "XMP ID + Ver",   CheckItem = "[XMP] ID",               Decode = DecXmpId       },
            new KeyByteGroup { Offset = 643, Length = 1, Name = "XMP Profiles",   CheckItem = "[XMP] Profiles Enabled", Decode = DecXmpProfiles },
            new KeyByteGroup { Offset = 702, Length = 2, Name = "XMP Global CRC", CheckItem = "[XMP] Global CRC",       Decode = DecCrcLE       },
            new KeyByteGroup { Offset = 766, Length = 2, Name = "XMP P1 CRC",     CheckItem = "[XMP] P1 CRC",           Decode = DecCrcLE       },
            new KeyByteGroup { Offset = 830, Length = 2, Name = "XMP P2 CRC",     CheckItem = "[XMP] P2 CRC",           Decode = DecCrcLE       },
        };

        public SpdEditorForm() : this(null, null) { }

        public SpdEditorForm(string loadPath, int? scrollToOffset)
        {
            BuildUI();
            if (!string.IsNullOrEmpty(loadPath) && File.Exists(loadPath))
            {
                LoadFile(loadPath);
                if (scrollToOffset.HasValue) ScrollToOffset(scrollToOffset.Value);
            }
            else
            {
                NewFile();
            }
        }

        private void ScrollToOffset(int offset)
        {
            if (offset < 0 || offset >= SPD_SIZE) return;
            int row = offset / COLS;
            int col = offset % COLS;
            _hexGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, row - 4);
            _hexGrid.ClearSelection();
            _hexGrid.CurrentCell = _hexGrid[col, row];
            _hexGrid[col, row].Selected = true;
        }

        // CheckItem 이름 → 해당 byte offset (없으면 null). MainForm DetailForm 점프용.
        public static int? FindOffsetByCheckItem(string checkItem)
        {
            if (string.IsNullOrEmpty(checkItem)) return null;
            foreach (var g in KEY_BYTE_GROUPS)
                if (g.CheckItem == checkItem) return g.Offset;
            return null;
        }

        // ── UI Construction ──────────────────────────────────────────────────
        private void BuildUI()
        {
            Text          = "DDR5 SPD Editor  v1.0";
            Size          = new Size(1240, 760);
            MinimumSize   = new Size(1000, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 9F);
            BackColor     = Color.FromArgb(245, 246, 248);
            FormClosing  += OnFormClosing;

            _hudTooltip = new HudTooltipForm();
            Shown += (s, e) => { if (_hudTooltip != null) _hudTooltip.Owner = this; };

            // 1. Hex grid (Fill) — added first
            _hexGrid = BuildHexGrid();
            Controls.Add(_hexGrid);

            // 2. Right panel
            _rightPanel = new Panel
            {
                Dock       = DockStyle.Right,
                Width      = 500,
                BackColor  = Color.White,
                AutoScroll = true,
                Padding    = new Padding(10)
            };
            _rightInfoLabel = new Label
            {
                AutoSize    = true,
                MaximumSize = new Size(460, 0),
                Margin      = new Padding(0, 4, 0, 0),
                Font        = new Font("Consolas", 9F),
                ForeColor   = Color.FromArgb(40, 40, 50)
            };

            var stack = new TableLayoutPanel
            {
                Dock          = DockStyle.Top,
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                ColumnCount   = 1,
                RowCount      = 2,
                BackColor     = Color.White,
                Padding       = new Padding(0)
            };
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            stack.Controls.Add(BuildPartInfoSection(), 0, 0);
            stack.Controls.Add(_rightInfoLabel,        0, 1);

            _rightPanel.Controls.Add(stack);
            Controls.Add(_rightPanel);

            // 3. Status bar (Bottom)
            var statusBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 24,
                BackColor = Color.FromArgb(225, 230, 235)
            };
            _statusLabel = new Label
            {
                Dock      = DockStyle.Fill,
                Padding   = new Padding(10, 4, 10, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(60, 60, 60)
            };
            statusBar.Controls.Add(_statusLabel);
            Controls.Add(statusBar);

            // 4. Toolbar (Top)
            Controls.Add(BuildToolbar());

            // 5. Header (Top — last added appears at very top)
            Controls.Add(BuildHeader());
        }

        private Panel BuildPartInfoSection()
        {
            var panel = new Panel
            {
                AutoSize     = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor    = Color.White,
                Padding      = new Padding(0, 0, 0, 8),
                Dock         = DockStyle.Top
            };

            var grid = new TableLayoutPanel
            {
                AutoSize     = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount  = 2,
                BackColor    = Color.White,
                Dock         = DockStyle.Top,
                Padding      = new Padding(0, 4, 0, 0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350F));

            int row = 0;

            _partNoTextBox = new TextBox
            {
                Font      = new Font("Consolas", 9F),
                Width     = 320,
                MaxLength = 64   // 입력 단계에선 넉넉히, validation에서 30 이하 강제
            };
            _partNoTextBox.Leave   += (s, e) => OnPartNumberCommit();
            _partNoTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    OnPartNumberCommit();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _partNoTextBox.Text = GetPartNumberFromBytes();
                    e.SuppressKeyPress = true;
                }
            };
            AddPartInfoRow(grid, row++, "Part No",     _partNoTextBox);

            _sourcingCombo   = MakePartCombo(new[] { "RM", "TM", "CM", "BM" });
            AddPartInfoRow(grid, row++, "Sourcing",    _sourcingCombo);

            _dimmTypeCombo   = MakePartCombo(new[] { "S (SODIMM)", "D (UDIMM)", "G (Gaming)", "C (Comp)" });
            AddPartInfoRow(grid, row++, "DIMM Type",   _dimmTypeCombo);

            _densityCombo    = MakePartCombo(new[] { "1G", "2G", "4G", "8G", "AG (16G)", "BG (32G)", "CG (64G)" });
            AddPartInfoRow(grid, row++, "Density",     _densityCombo);

            _bankCombo       = MakePartCombo(new[] { "4 (16Bk/1.2V)", "5 (32Bk/1.1V)", "6 (32Bk/1.35V)", "7 (32Bk/1.4V)" });
            AddPartInfoRow(grid, row++, "Bank/VDD",    _bankCombo);

            _compCombo       = MakePartCombo(new[] { "4 (X4)", "8 (X8)", "6 (X16)" });
            AddPartInfoRow(grid, row++, "Composition", _compCombo);

            _dieDensityCombo = MakePartCombo(new[] { "4 (4Gb)", "8 (8Gb)", "A (16Gb)", "H (24Gb)", "B (32Gb)" });
            AddPartInfoRow(grid, row++, "Die Density", _dieDensityCombo);

            _rankCombo       = MakePartCombo(new[] { "0 (Comp)", "1 (1R)", "2 (2R)" });
            AddPartInfoRow(grid, row++, "Rank",        _rankCombo);

            _dramMfrCombo    = MakePartCombo(new[] { "S (RAmos)", "G (GIGA)", "H (Hynix)", "M (Micron)", "C (CXMT)", "N (Nanya)" });
            AddPartInfoRow(grid, row++, "DRAM Mfr",    _dramMfrCombo);

            _speedCombo      = MakePartCombo(new[] { "QK (4800)", "WM (5600)", "CM (6000)", "CP (6400)", "CQ (6400)", "CR (6800)", "CS (7200)" });
            AddPartInfoRow(grid, row++, "Speed",       _speedCombo);

            _sourcingCombo.SelectedIndexChanged   += (s, e) => OnFieldChanged("Sourcing",    _sourcingCombo);
            _dimmTypeCombo.SelectedIndexChanged   += (s, e) => OnFieldChanged("DIMM Type",   _dimmTypeCombo);
            _densityCombo.SelectedIndexChanged    += (s, e) => OnFieldChanged("Density",     _densityCombo);
            _bankCombo.SelectedIndexChanged       += (s, e) => OnFieldChanged("Bank/VDD",    _bankCombo);
            _compCombo.SelectedIndexChanged       += (s, e) => OnFieldChanged("Composition", _compCombo);
            _dieDensityCombo.SelectedIndexChanged += (s, e) => OnFieldChanged("Die Density", _dieDensityCombo);
            _rankCombo.SelectedIndexChanged       += (s, e) => OnFieldChanged("Rank",        _rankCombo);
            _dramMfrCombo.SelectedIndexChanged    += (s, e) => OnFieldChanged("DRAM Mfr",    _dramMfrCombo);
            _speedCombo.SelectedIndexChanged      += (s, e) => OnFieldChanged("Speed",       _speedCombo);

            var header = new Label
            {
                Text      = "─── Part Information ──────────────",
                Dock      = DockStyle.Top,
                AutoSize  = true,
                Font      = new Font("Consolas", 9F),
                ForeColor = Color.FromArgb(40, 40, 50),
                Margin    = new Padding(0)
            };

            panel.Controls.Add(grid);
            panel.Controls.Add(header);   // Dock=Top 추가 순서: header 나중에 추가 → 위쪽에 배치
            return panel;
        }

        private static ComboBox MakePartCombo(string[] items)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font          = new Font("Segoe UI", 9F),
                Width         = 200
            };
            c.Items.AddRange(items);
            return c;
        }

        private static void AddPartInfoRow(TableLayoutPanel grid, int row, string labelText, Control control)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label
            {
                Text      = labelText,
                AutoSize  = true,
                Anchor    = AnchorStyles.Left,
                Margin    = new Padding(0, 6, 6, 4),
                Font      = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(60, 60, 60)
            };
            grid.Controls.Add(lbl,     0, row);
            grid.Controls.Add(control, 1, row);
        }

        private string GetPartNumberFromBytes()
        {
            return Encoding.ASCII.GetString(_data,
                SpdParser.PART_NUMBER_OFFSET, SpdParser.PART_NUMBER_LENGTH).TrimEnd(' ', '\0');
        }

        private Panel BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 57, 95) };
            var lblTitle = new Label
            {
                Text      = "DDR5 SPD Editor",
                Font      = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(15, 0, 0, 0)
            };
            var btnMode = ModeDropdown.Create(
                AppMode.Editor,
                m => { NextMode = m; Close(); },
                ConfirmDirtyDiscard);
            header.Controls.Add(lblTitle);
            header.Controls.Add(btnMode);
            return header;
        }

        private Panel BuildToolbar()
        {
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(245, 246, 248) };
            toolbar.Padding = new Padding(10, 7, 10, 7);

            var btnLoad    = MakeToolBtn("📂  Load File", Color.FromArgb(65, 125, 190),   0,   120);
            var btnNew     = MakeToolBtn("🆕  New",       Color.FromArgb(108, 117, 125), 130, 90);
            var btnSave    = MakeToolBtn("💾  Save",      Color.FromArgb(34, 153, 60),   230, 90);
            var btnSaveAs  = MakeToolBtn("Save As",       Color.FromArgb(20, 155, 175),  330, 90);
            var btnCrc     = MakeToolBtn("🔄  CRC 재계산", Color.FromArgb(170, 70, 20),   440, 140);
            var btnAutoFix = MakeToolBtn("🔧  자동 수정",  Color.FromArgb(120, 40, 170),  590, 130);
            _btnRevert     = MakeToolBtn("↩  초기화",     Color.FromArgb(160, 90, 20),   730, 110);

            btnLoad.Click    += (s, e) => LoadFileDialog();
            btnNew.Click     += (s, e) => NewFile();
            btnSave.Click    += (s, e) => SaveFile();
            btnSaveAs.Click  += (s, e) => SaveFileAs();
            btnCrc.Click     += (s, e) => RecalculateCrcs();
            btnAutoFix.Click += (s, e) => AutoFix();
            _btnRevert.Click += (s, e) => RevertFile();

            toolbar.Controls.AddRange(new Control[] { btnLoad, btnNew, btnSave, btnSaveAs, btnCrc, btnAutoFix, _btnRevert });
            return toolbar;
        }

        private static Button MakeToolBtn(string text, Color bg, int x, int width)
        {
            var btn = new Button
            {
                Text      = text,
                Location  = new Point(x, 0),
                Size      = new Size(width, 32),
                Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = bg,
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private DataGridView BuildHexGrid()
        {
            var grid = new DataGridView
            {
                Dock                        = DockStyle.Fill,
                AllowUserToAddRows          = false,
                AllowUserToDeleteRows       = false,
                AllowUserToResizeRows       = false,
                AllowUserToResizeColumns    = false,
                AllowUserToOrderColumns     = false,
                RowHeadersWidth             = 55,
                RowHeadersWidthSizeMode     = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
                ColumnHeadersHeight         = 26,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                BackgroundColor             = Color.White,
                BorderStyle                 = BorderStyle.None,
                SelectionMode               = DataGridViewSelectionMode.CellSelect,
                ScrollBars                  = ScrollBars.Vertical,
                Font                        = new Font("Consolas", 9.5F),
                EnableHeadersVisualStyles   = false,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    Font      = new Font("Consolas", 9F, FontStyle.Bold),
                    BackColor = Color.FromArgb(225, 230, 240),
                    ForeColor = Color.Black,
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                },
                RowHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    Font      = new Font("Consolas", 9F, FontStyle.Bold),
                    BackColor = Color.FromArgb(225, 230, 240),
                    ForeColor = Color.Black,
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Padding   = new Padding(0)
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font      = new Font("Consolas", 9.5F),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                }
            };

            for (int c = 0; c < COLS; c++)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name           = "col" + c.ToString("X"),
                    HeaderText     = c.ToString("X"),
                    Width          = 36,
                    SortMode       = DataGridViewColumnSortMode.NotSortable,
                    MaxInputLength = 2
                });
            }
            grid.Rows.Add(ROWS);
            for (int r = 0; r < ROWS; r++)
                grid.Rows[r].HeaderCell.Value = r.ToString("X2");   // 00 ~ 3F

            grid.ShowCellToolTips = false;
            grid.CellEndEdit      += OnCellEndEdit;
            grid.CellClick        += OnGridCellClick;
            grid.KeyDown          += (s, e) => { if (e.KeyCode == Keys.Escape) _hudTooltip?.Hide(); };
            return grid;
        }

        // ── HUD Tooltip ──────────────────────────────────────────────────────
        private static KeyByteGroup? FindKeyByteGroup(int byteOffset)
        {
            foreach (var g in KEY_BYTE_GROUPS)
                if (byteOffset >= g.Offset && byteOffset < g.Offset + g.Length)
                    return g;
            return null;
        }

        private void OnGridCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) { _hudTooltip?.Hide(); return; }
            int byteOffset = e.RowIndex * COLS + e.ColumnIndex;
            var grp = FindKeyByteGroup(byteOffset);
            if (grp == null) { _hudTooltip?.Hide(); return; }

            var grid     = (DataGridView)sender;
            var cellRect = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
            if (cellRect.IsEmpty) return;

            var anchor = grid.PointToScreen(new Point(cellRect.Right, cellRect.Top));

            string addr = grp.Value.Length == 1
                ? grp.Value.Offset.ToString("X3")
                : $"{grp.Value.Offset:X3}-{grp.Value.Offset + grp.Value.Length - 1:X3}";

            var (raw, meaning) = grp.Value.Decode(_data, grp.Value.Offset);

            bool? pass = null;
            if (grp.Value.CheckItem != null && _checkResults.TryGetValue(grp.Value.CheckItem, out var cr))
                pass = cr.Status == CheckStatus.Pass ? true
                     : cr.Status == CheckStatus.Fail ? false
                     : (bool?)null;

            _hudTooltip.ShowAt(anchor, addr, grp.Value.Name, raw, meaning, pass);
        }

        // ── File Operations ──────────────────────────────────────────────────
        private void NewFile()
        {
            if (!ConfirmDirtyDiscard()) return;
            _data         = new byte[SPD_SIZE];
            _originalData = new byte[SPD_SIZE];
            _filePath     = null;
            _dirty        = false;
            SyncGridFromData();
            RefreshDisplay();
        }

        private void LoadFileDialog()
        {
            if (!ConfirmDirtyDiscard()) return;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "SPD Files (*.sp5;*.bin)|*.sp5;*.bin|All Files (*.*)|*.*";
                dlg.Title  = "SPD 파일 로드";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                LoadFile(dlg.FileName);
            }
        }

        private void LoadFile(string path)
        {
            try
            {
                byte[] raw = SpdParser.ParseFile(path);
                _data = new byte[SPD_SIZE];
                Array.Copy(raw, _data, Math.Min(raw.Length, SPD_SIZE));
                _originalData = (byte[])_data.Clone();
                _filePath     = path;
                _dirty        = false;
                SyncGridFromData();
                RefreshDisplay();

                if (raw.Length != SPD_SIZE)
                    MessageBox.Show(this,
                        $"파일 크기가 {raw.Length} bytes입니다. {SPD_SIZE} bytes로 정규화 (부족분 0x00 패딩 / 초과분 잘라냄).",
                        "정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"파일 로드 실패:\n{ex.Message}",
                    "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ConfirmDirtyDiscard()
        {
            if (!_dirty) return true;
            var r = MessageBox.Show(this,
                "수정된 내용이 저장되지 않았습니다. 무시하고 계속하시겠습니까?",
                "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            return r == DialogResult.Yes;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmDirtyDiscard()) { e.Cancel = true; return; }
            _hudTooltip?.Dispose();
            _hudTooltip = null;
        }

        // ── Hex Grid ↔ Data Sync ─────────────────────────────────────────────
        private void SyncGridFromData()
        {
            for (int r = 0; r < ROWS; r++)
                for (int c = 0; c < COLS; c++)
                    _hexGrid[c, r].Value = _data[r * COLS + c].ToString("X2");
            HighlightKeyBytes();
        }

        private static readonly Color CLR_KEY_DEFAULT = Color.FromArgb(220, 235, 255);
        private static readonly Color CLR_PASS        = Color.FromArgb(220, 245, 220);
        private static readonly Color CLR_FAIL        = Color.FromArgb(250, 215, 215);

        private void SetCellBg(int offset, Color color)
        {
            var cell = _hexGrid[offset % COLS, offset / COLS];
            if (cell.Style.BackColor != color)
                cell.Style.BackColor = color;
        }

        private void HighlightKeyBytes()
        {
            foreach (var g in KEY_BYTE_GROUPS)
            {
                Color color = CLR_KEY_DEFAULT;
                CheckResult r = null;
                if (g.CheckItem != null && _checkResults.TryGetValue(g.CheckItem, out r))
                {
                    if (r.Status == CheckStatus.Pass)      color = CLR_PASS;
                    else if (r.Status == CheckStatus.Fail) color = CLR_FAIL;
                }

                // Part Number FAIL → byte 단위 차이만 빨강, 일치 부분은 초록
                if (g.CheckItem == "Part Number" && r != null && r.Status == CheckStatus.Fail)
                {
                    string expected = (r.Expected ?? "").PadRight(g.Length, ' ');
                    for (int i = 0; i < g.Length; i++)
                    {
                        int off = g.Offset + i;
                        if (off >= SPD_SIZE) continue;
                        byte actByte = _data[off];
                        char expCh   = i < expected.Length ? expected[i] : ' ';
                        bool match = expCh == ' '
                            ? (actByte == 0x20 || actByte == 0x00)
                            : char.ToUpperInvariant((char)actByte) == char.ToUpperInvariant(expCh);
                        SetCellBg(off, match ? CLR_PASS : CLR_FAIL);
                    }
                    continue;
                }

                for (int i = 0; i < g.Length; i++)
                {
                    int off = g.Offset + i;
                    if (off >= SPD_SIZE) continue;
                    SetCellBg(off, color);
                }
            }
        }

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            int address = e.RowIndex * COLS + e.ColumnIndex;
            string val  = (_hexGrid[e.ColumnIndex, e.RowIndex].Value ?? "").ToString().Trim();

            if (byte.TryParse(val, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                if (_data[address] != b)
                {
                    _data[address] = b;
                    _dirty = true;
                }
                _hexGrid[e.ColumnIndex, e.RowIndex].Value = b.ToString("X2");
                RefreshDisplay();
            }
            else
            {
                _hexGrid[e.ColumnIndex, e.RowIndex].Value = _data[address].ToString("X2");
                MessageBox.Show(this,
                    $"잘못된 hex 값입니다: '{val}'\n" +
                    $"00 ~ FF 범위의 16진수만 허용됩니다 (한글/특수문자/3자리 이상 불가).",
                    "Byte 입력 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ── Right Panel + Status ─────────────────────────────────────────────
        private void RefreshDisplay()
        {
            _info = SpdInfo.FromBytes(_data, GetDisplayName(), _filePath);

            // 실시간 검증: 현재 byte[]를 SpdChecker로 돌려서 CheckItem→Result 매핑
            string partNo = SpdParser.StripSuffix(_info.PartNumberAscii ?? "");
            var resultsList = SpdChecker.CheckBytes(
                _data, GetDisplayName(), partNo, skipFilenameChecks: true);
            _checkResults.Clear();
            foreach (var r in resultsList)
                if (!_checkResults.ContainsKey(r.CheckItem))
                    _checkResults[r.CheckItem] = r;

            // 파일 로드 시: 파일명 vs bytes 521~550 Part Number 대조 (AppendKeyBytes 전에)
            if (_filePath != null)
            {
                string expectedPn = SpdParser.StripSuffix(Path.GetFileNameWithoutExtension(_filePath));
                string actualPn   = Encoding.ASCII.GetString(_data,
                    SpdParser.PART_NUMBER_OFFSET, SpdParser.PART_NUMBER_LENGTH).TrimEnd(' ', '\0');
                bool pnPass = string.Equals(expectedPn, actualPn, StringComparison.OrdinalIgnoreCase);
                _checkResults["Part Number"] = new CheckResult
                {
                    FileName  = GetDisplayName(),
                    CheckItem = "Part Number",
                    Expected  = expectedPn,
                    Actual    = actualPn,
                    Pass      = pnPass,
                    Status    = pnPass ? CheckStatus.Pass : CheckStatus.Fail
                };
            }

            UpdatePartInfoControls();

            var sb = new StringBuilder();
            AppendTemplate(sb);
            sb.AppendLine();
            AppendKeyBytes(sb);
            _rightInfoLabel.Text = sb.ToString();

            HighlightKeyBytes();   // 검증 결과 기반 색상 갱신
            UpdateStatusBar();
            UpdateTitle();
        }

        // ── Part Info 컨트롤 갱신 (bytes → UI) ─────────────────────────────
        private void UpdatePartInfoControls()
        {
            if (_partNoTextBox == null) return;
            _suppressEvents = true;
            try
            {
                _partNoTextBox.Text = GetPartNumberFromBytes();

                var f = _info != null ? _info.Fields : default(PartFields);
                SelectComboByPrefix(_sourcingCombo,   f.Sourcing);
                SelectComboByPrefix(_dimmTypeCombo,   f.DimmType        != '\0' ? f.DimmType.ToString()        : null);
                SelectComboByPrefix(_densityCombo,    f.DensityCode);
                SelectComboByPrefix(_bankCombo,       f.BankCode        != '\0' ? f.BankCode.ToString()        : null);
                SelectComboByPrefix(_compCombo,       f.CompositionCode != '\0' ? f.CompositionCode.ToString() : null);
                SelectComboByPrefix(_dieDensityCombo, f.DieDensityCode  != '\0' ? f.DieDensityCode.ToString()  : null);
                SelectComboByPrefix(_rankCombo,       f.RankCode        != '\0' ? f.RankCode.ToString()        : null);
                SelectComboByPrefix(_dramMfrCombo,    f.DramMfrCode     != '\0' ? f.DramMfrCode.ToString()     : null);
                SelectComboByPrefix(_speedCombo,      f.SpeedCode);
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private static void SelectComboByPrefix(ComboBox c, string prefix)
        {
            if (c == null) return;
            if (string.IsNullOrEmpty(prefix)) { c.SelectedIndex = -1; return; }
            for (int i = 0; i < c.Items.Count; i++)
            {
                string item = c.Items[i].ToString();
                if (string.Equals(item, prefix, StringComparison.OrdinalIgnoreCase)
                    || item.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                {
                    c.SelectedIndex = i;
                    return;
                }
            }
            c.SelectedIndex = -1;
        }

        // ── Part Number TextBox 입력 검증 + 적용 ─────────────────────────────
        private void OnPartNumberCommit()
        {
            if (_suppressEvents) return;
            if (_partNoTextBox == null) return;

            string newPN     = _partNoTextBox.Text ?? "";
            string currentPN = GetPartNumberFromBytes();
            if (newPN == currentPN) return;

            string error = ValidatePartNumber(newPN);
            if (error != null)
            {
                MessageBox.Show(this, error, "Part Number 입력 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _suppressEvents = true;
                try { _partNoTextBox.Text = currentPN; }
                finally { _suppressEvents = false; }
                return;
            }

            var ascii = Encoding.ASCII.GetBytes(newPN.PadRight(SpdParser.PART_NUMBER_LENGTH, ' '));
            Array.Copy(ascii, 0, _data, SpdParser.PART_NUMBER_OFFSET, SpdParser.PART_NUMBER_LENGTH);
            for (int i = 0; i < SpdParser.PART_NUMBER_LENGTH; i++)
            {
                int off = SpdParser.PART_NUMBER_OFFSET + i;
                _hexGrid[off % COLS, off / COLS].Value = _data[off].ToString("X2");
            }
            _dirty = true;
            RefreshDisplay();
        }

        private static string ValidatePartNumber(string pn)
        {
            foreach (char ch in pn)
                if (ch < 0x20 || ch > 0x7E)
                    return $"ASCII 인쇄 가능 문자만 허용됩니다 (한글/제어문자 불가).\n" +
                           $"잘못된 문자: '{ch}' (코드 0x{(int)ch:X4})";

            if (pn.Length == 0 || pn.Length > SpdParser.PART_NUMBER_LENGTH)
                return $"Part Number 길이는 1~{SpdParser.PART_NUMBER_LENGTH}자여야 합니다 (현재: {pn.Length}자).";

            var f = SpdParser.ParsePartFields(SpdParser.StripSuffix(pn));
            if (!f.Valid)
                return $"Part Number 형식이 올바르지 않습니다.\n오류: {f.Error}";

            return null;
        }

        // ── ComboBox 필드 변경 핸들러 ────────────────────────────────────────
        private void OnFieldChanged(string fieldName, ComboBox combo)
        {
            if (_suppressEvents) return;
            if (combo.SelectedIndex < 0) return;

            // 변경 전 상태 저장 (실패 시 복원용)
            byte[] oldData = (byte[])_data.Clone();

            // 콤보 항목 첫 토큰 = 코드 (예: "AG (16G)" → "AG", "1G" → "1G")
            string item    = combo.SelectedItem.ToString();
            string newCode = item.Split(' ')[0];

            string error = ApplyFieldChange(fieldName, newCode);
            if (error != null)
            {
                // 복원: bytes + 컨트롤
                Array.Copy(oldData, _data, _data.Length);
                MessageBox.Show(this, error,
                    $"{fieldName} 변경 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _suppressEvents = true;
                try { SyncGridFromData(); UpdatePartInfoControls(); }
                finally { _suppressEvents = false; }
                return;
            }

            _dirty = true;
            SyncGridFromData();
            RefreshDisplay();
        }

        private string ApplyFieldChange(string fieldName, string newCode)
        {
            if (string.IsNullOrEmpty(newCode))
                return "코드가 비어 있습니다.";

            string currentPN = GetPartNumberFromBytes();
            var cur = SpdParser.ParsePartFields(SpdParser.StripSuffix(currentPN));
            if (!cur.Valid)
                return $"현재 Part Number를 파싱할 수 없습니다: {cur.Error}\n" +
                       $"Part Number TextBox에 유효한 값을 먼저 입력하세요.";

            string newPN = currentPN;
            char   c0    = newCode[0];

            switch (fieldName)
            {
                case "Sourcing":
                    if (newCode.Length != 2) return "Sourcing 코드는 2자여야 합니다.";
                    newPN = newCode + currentPN.Substring(Math.Min(2, currentPN.Length));
                    break;

                case "DIMM Type":
                    newPN = ReplaceBodyChar(currentPN, 1, c0);
                    if (!SpdFixer.TryFixModuleType(_data, c0)) return "DIMM Type 코드 오류.";
                    break;

                case "Density":
                    if (newCode.Length != 2) return "Density 코드는 2자여야 합니다 (예: 8G, AG).";
                    newPN = ReplaceBodyDensity(currentPN, newCode);
                    break;

                case "Bank/VDD":
                    if (!ValidateBankSpeed(c0, cur.SpeedCode))
                        return $"Bank '{c0}'는 Speed '{cur.SpeedCode}'와 호환되지 않습니다.\n" +
                               $"허용 조합: 4800/5600 → 5, 6000/6400 → 6, 6800/7200 → 7";
                    newPN = ReplaceBodyChar(currentPN, 4, c0);
                    if (!SpdFixer.TryFixBankGroups(_data, c0)) return "Bank 코드 오류.";
                    break;

                case "Composition":
                    newPN = ReplaceBodyChar(currentPN, 5, c0);
                    if (!SpdFixer.TryFixIoWidth(_data, c0)) return "Composition 코드 오류.";
                    break;

                case "Die Density":
                    newPN = ReplaceBodyChar(currentPN, 6, c0);
                    if (!SpdFixer.TryFixDieDensity(_data, c0)) return "Die Density 코드 오류.";
                    break;

                case "Rank":
                    newPN = ReplaceBodyChar(currentPN, 7, c0);
                    if (c0 == '0') break;   // Comp: byte 변경 없음, PN만 갱신
                    if (!SpdFixer.TryFixRank(_data, c0)) return "Rank 코드 오류.";
                    break;

                case "DRAM Mfr":
                    newPN = ReplaceDramMfr(currentPN, c0);
                    if (!SpdFixer.TryFixDramMfrId(_data, c0)) return "DRAM Mfr 코드 오류.";
                    break;

                case "Speed":
                    if (!ValidateBankSpeed(cur.BankCode, newCode))
                        return $"Speed '{newCode}'는 Bank '{cur.BankCode}'와 호환되지 않습니다.\n" +
                               $"허용 조합: 4800/5600 → 5, 6000/6400 → 6, 6800/7200 → 7";
                    if (string.IsNullOrEmpty(cur.SpeedCode))
                        return "현재 Part Number에서 Speed 코드를 찾을 수 없어 변경할 수 없습니다.";
                    newPN = ReplaceSpeedInSuffix(currentPN, cur.SpeedCode, newCode);
                    if (!SpdFixer.TryFixJedecTimings(_data, newCode))
                        return $"Speed 코드 '{newCode}' 매핑 없음.";
                    break;

                default:
                    return $"알 수 없는 필드: {fieldName}";
            }

            // 변경 후 PN 검증 (길이 + 파싱)
            if (newPN.Length > SpdParser.PART_NUMBER_LENGTH)
                return $"변경 후 Part Number 길이 초과 ({newPN.Length} > {SpdParser.PART_NUMBER_LENGTH}).";
            var newFields = SpdParser.ParsePartFields(SpdParser.StripSuffix(newPN));
            if (!newFields.Valid)
                return $"변경 후 Part Number 형식 오류: {newFields.Error}";

            // bytes 521~550 갱신
            var ascii = Encoding.ASCII.GetBytes(newPN.PadRight(SpdParser.PART_NUMBER_LENGTH, ' '));
            Array.Copy(ascii, 0, _data, SpdParser.PART_NUMBER_OFFSET, SpdParser.PART_NUMBER_LENGTH);
            return null;
        }

        // ── Bank ↔ Speed 조합 검증 ───────────────────────────────────────────
        private static bool ValidateBankSpeed(char bankCode, string speedCode)
        {
            if (string.IsNullOrEmpty(speedCode)) return true;
            switch (speedCode.ToUpperInvariant())
            {
                case "QK":
                case "WM": return bankCode == '5';
                case "CM":
                case "CP":
                case "CQ": return bankCode == '6';
                case "CR":
                case "CS": return bankCode == '7';
                default:   return true;
            }
        }

        // ── Part Number 부분 치환 헬퍼 ───────────────────────────────────────
        // PN core 위치(prefix 2자 제외)에서 1글자 교체
        private static string ReplaceBodyChar(string pn, int corePos, char newCh)
        {
            int dashIdx = pn.IndexOf('-');
            string body  = dashIdx >= 0 ? pn.Substring(0, dashIdx) : pn;
            string after = dashIdx >= 0 ? pn.Substring(dashIdx)    : "";
            int absPos = 2 + corePos;
            if (absPos >= body.Length) return pn;
            var chars = body.ToCharArray();
            chars[absPos] = newCh;
            return new string(chars) + after;
        }

        // Density는 2자 (core[2..3])
        private static string ReplaceBodyDensity(string pn, string newDensity)
        {
            int dashIdx = pn.IndexOf('-');
            string body  = dashIdx >= 0 ? pn.Substring(0, dashIdx) : pn;
            string after = dashIdx >= 0 ? pn.Substring(dashIdx)    : "";
            if (body.Length < 6) return pn;
            return body.Substring(0, 4) + newDensity + body.Substring(6) + after;
        }

        // DRAM Mfr는 dash 직후 1자 (suffix[0])
        private static string ReplaceDramMfr(string pn, char newMfr)
        {
            int dashIdx = pn.IndexOf('-');
            if (dashIdx < 0 || dashIdx + 1 >= pn.Length) return pn;
            var chars = pn.ToCharArray();
            chars[dashIdx + 1] = newMfr;
            return new string(chars);
        }

        // Speed 코드는 suffix 내 임의 위치 — substring 검색해서 교체
        private static string ReplaceSpeedInSuffix(string pn, string oldSpeed, string newSpeed)
        {
            int dashIdx = pn.IndexOf('-');
            if (dashIdx < 0) return pn;
            string suffix = pn.Substring(dashIdx + 1);
            int idx = suffix.IndexOf(oldSpeed, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return pn;
            string newSuffix = suffix.Substring(0, idx) + newSpeed + suffix.Substring(idx + oldSpeed.Length);
            return pn.Substring(0, dashIdx + 1) + newSuffix;
        }

        private void AppendTemplate(StringBuilder sb)
        {
            sb.AppendLine("─── 템플릿 파일명 ──────────────────");
            sb.AppendLine();
            sb.AppendLine($"  {_info.TemplateFileName ?? "(파싱 실패)"}");
        }

        private void AppendKeyBytes(StringBuilder sb)
        {
            sb.AppendLine("─── Key Bytes ─────────────────────────────────");
            sb.AppendLine();
            foreach (var g in KEY_BYTE_GROUPS)
            {
                if (g.Offset + g.Length > _data.Length) continue;
                string addr = g.Length == 1
                    ? g.Offset.ToString("X3")
                    : $"{g.Offset:X3}-{g.Offset + g.Length - 1:X3}";
                var (raw, meaning) = g.Decode(_data, g.Offset);

                string status = "  ";
                if (g.CheckItem != null && _checkResults.TryGetValue(g.CheckItem, out var r))
                {
                    status = r.Status == CheckStatus.Pass ? "✅"
                           : r.Status == CheckStatus.Fail ? "❌"
                           : "⊘ ";
                }
                sb.AppendLine($"  {status} {addr,-9} {raw,-11} {g.Name,-15} {meaning}");
            }
        }

        // ── Revert (마지막 로드/저장 상태로 복원) ────────────────────────────
        private void RevertFile()
        {
            if (!_dirty)
            {
                MessageBox.Show(this, "변경된 내용이 없습니다.",
                    "초기화", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var r = MessageBox.Show(this,
                "마지막으로 로드/저장한 상태로 되돌립니다.\n수정 내용이 모두 사라집니다. 계속하시겠습니까?",
                "초기화 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            Array.Copy(_originalData, _data, SPD_SIZE);
            _dirty = false;
            SyncGridFromData();
            RefreshDisplay();
        }

        // ── Auto-Fix ─────────────────────────────────────────────────────────
        private void AutoFix()
        {
            string partNo = SpdParser.StripSuffix((_info?.PartNumberAscii ?? "").Trim());
            if (string.IsNullOrEmpty(partNo))
            {
                MessageBox.Show(this,
                    "Part Number가 없습니다.\nByte 521~550 (0x209~0x226)에 Part Number를 먼저 입력하세요.",
                    "Auto-Fix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // SpdFixer는 filePath에서 Part Number를 파싱 — 가상 경로로 전달
            string dir         = _filePath != null ? Path.GetDirectoryName(_filePath) ?? "" : "";
            string virtualPath = Path.Combine(dir, partNo + ".sp5");

            try
            {
                byte[] fixedData = SpdFixer.ApplyFixes(_data, virtualPath);

                int changed = 0;
                for (int i = 0; i < _data.Length && i < fixedData.Length; i++)
                    if (_data[i] != fixedData[i]) changed++;

                if (changed == 0)
                {
                    MessageBox.Show(this, "수정할 항목이 없습니다. 모든 바이트가 이미 정상입니다.",
                        "Auto-Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Array.Copy(fixedData, _data, Math.Min(fixedData.Length, _data.Length));
                _dirty = true;
                SyncGridFromData();
                RefreshDisplay();

                MessageBox.Show(this,
                    $"Auto-Fix 완료: {changed}개 바이트 수정됨.\n저장은 [💾 Save] 버튼으로 확정하세요.",
                    "Auto-Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Auto-Fix 실패:\n{ex.Message}",
                    "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── CRC 일괄 재계산 (JEDEC + XMP Global / P1 / P2) ───────────────────
        private void RecalculateCrcs()
        {
            var report  = new StringBuilder();
            bool changed = false;

            // JEDEC CRC: Bytes 0~509 → 510~511
            if (_data.Length >= 512)
            {
                ushort old = (ushort)(_data[510] | (_data[511] << 8));
                ushort fresh = SpdParser.ComputeCrc16(_data, 0, 510);
                if (old != fresh)
                {
                    _data[510] = (byte)(fresh & 0xFF);
                    _data[511] = (byte)(fresh >> 8);
                    changed = true;
                }
                report.AppendLine($"  ✅ JEDEC       0x{old:X4}  →  0x{fresh:X4}");
            }

            // XMP CRCs (XMP 활성 시)
            if (SpdParser.IsXmpEnabled(_data))
            {
                if (_data.Length >= 704)
                {
                    ushort old = (ushort)(_data[702] | (_data[703] << 8));
                    ushort fresh = SpdParser.ComputeCrc16(_data, 640, 62);
                    if (old != fresh)
                    {
                        _data[702] = (byte)(fresh & 0xFF);
                        _data[703] = (byte)(fresh >> 8);
                        changed = true;
                    }
                    report.AppendLine($"  ✅ XMP Global  0x{old:X4}  →  0x{fresh:X4}");
                }

                if (_data.Length >= 768)
                {
                    ushort old = (ushort)(_data[766] | (_data[767] << 8));
                    ushort fresh = SpdParser.ComputeCrc16(_data, 704, 62);
                    if (old != fresh)
                    {
                        _data[766] = (byte)(fresh & 0xFF);
                        _data[767] = (byte)(fresh >> 8);
                        changed = true;
                    }
                    report.AppendLine($"  ✅ XMP P1      0x{old:X4}  →  0x{fresh:X4}");
                }

                bool p2Enabled = _data.Length >= 644 && (_data[643] & 0x02) != 0;
                if (_data.Length >= 832 && p2Enabled)
                {
                    ushort old = (ushort)(_data[830] | (_data[831] << 8));
                    ushort fresh = SpdParser.ComputeCrc16(_data, 768, 62);
                    if (old != fresh)
                    {
                        _data[830] = (byte)(fresh & 0xFF);
                        _data[831] = (byte)(fresh >> 8);
                        changed = true;
                    }
                    report.AppendLine($"  ✅ XMP P2      0x{old:X4}  →  0x{fresh:X4}");
                }
                else
                {
                    report.AppendLine("  ⊘  XMP P2      (P2 미활성)");
                }
            }
            else
            {
                report.AppendLine("  ⊘  XMP         (XMP 미활성)");
            }

            if (changed) _dirty = true;
            SyncGridFromData();
            RefreshDisplay();

            MessageBox.Show(this, report.ToString(), "CRC 일괄 재계산",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ── Key Byte 디코더 (raw 표시 + 의미 텍스트) ────────────────────────
        private static (string Raw, string Meaning) DecPartNumber(byte[] d, int o)
        {
            string ascii = Encoding.ASCII.GetString(d, o, SpdParser.PART_NUMBER_LENGTH)
                                         .TrimEnd(' ', '\0');
            string raw = ascii.Length > 10 ? ascii.Substring(0, 9) + "…" : ascii;
            return (raw, "bytes 209~226");
        }

        private static (string Raw, string Meaning) DecDramType(byte[] d, int o)
        {
            byte b = d[o];
            return ($"0x{b:X2}", b == 0x12 ? "DDR5 SDRAM" : "Unknown");
        }

        private static (string Raw, string Meaning) DecModuleType(byte[] d, int o)
        {
            byte b = d[o];
            byte v = (byte)(b & 0x0F);
            string name = v == 0x01 ? "RDIMM"
                        : v == 0x02 ? "UDIMM"
                        : v == 0x03 ? "SODIMM"
                        : "Unknown";
            return ($"0x{b:X2}", name);
        }

        private static (string Raw, string Meaning) DecDieDensity(byte[] d, int o)
        {
            byte b = d[o];
            byte densityCode = (byte)(b & 0x1F);
            byte diesCode    = (byte)((b >> 5) & 0x07);
            string density = SpdParser.DIE_DENSITY_GB_MAP.TryGetValue(densityCode, out int gb) ? $"{gb} Gb" : "?";
            string dies    = diesCode == 0 ? "Mono"
                           : diesCode == 1 ? "DDP"
                           : diesCode == 2 ? "2H 3DS"
                           : diesCode == 3 ? "4H 3DS"
                           : diesCode == 4 ? "8H 3DS"
                           : diesCode == 5 ? "16H 3DS"
                           : "?";
            return ($"0x{b:X2}", $"{density} / {dies}");
        }

        private static (string Raw, string Meaning) DecIoWidth(byte[] d, int o)
        {
            byte b = d[o];
            byte v = (byte)((b >> 5) & 0x07);
            string m = v == 0 ? "X4" : v == 1 ? "X8" : v == 2 ? "X16" : "?";
            return ($"0x{b:X2}", m);
        }

        private static (string Raw, string Meaning) DecBank(byte[] d, int o)
        {
            byte b = d[o];
            byte bgBits   = (byte)((b >> 5) & 0x07);
            byte bankBits = (byte)(b & 0x07);
            int bgs = bgBits == 0 ? 1 : bgBits == 1 ? 2 : bgBits == 2 ? 4 : bgBits == 3 ? 8 : -1;
            int bnk = bankBits == 0 ? 1 : bankBits == 1 ? 2 : bankBits == 2 ? 4 : -1;
            string m = (bgs > 0 && bnk > 0) ? $"{bgs * bnk} Bank ({bgs}BG×{bnk})" : "Unknown";
            return ($"0x{b:X2}", m);
        }

        private static (string Raw, string Meaning) DecVdd(byte[] d, int o)
        {
            byte b = d[o];
            return ($"0x{b:X2}", b == 0x00 ? "1.1V" : $"raw 0x{b:X2}");
        }

        private static (string Raw, string Meaning) DecTck(byte[] d, int o)
        {
            int v = d[o] | (d[o + 1] << 8);
            string speedName = null;
            foreach (var kv in SpdParser.SPEED_MAP)
                if (kv.Value.TckAvgMin == v) { speedName = kv.Value.Name; break; }
            string m = speedName != null ? $"{v} ps ({speedName})" : $"{v} ps";
            return ($"0x{v:X4}", m);
        }

        private static (string Raw, string Meaning) DecTimingPs(byte[] d, int o)
        {
            int v   = d[o] | (d[o + 1] << 8);
            int tck = d[SpdParser.TCK_AVG_MIN_OFFSET] | (d[SpdParser.TCK_AVG_MIN_OFFSET + 1] << 8);
            if (tck > 0)
            {
                int nck = (int)Math.Truncate((v * 997.0 / tck + 1000.0) / 1000.0);
                return ($"0x{v:X4}", $"{v} ps → {nck} nCK");
            }
            return ($"0x{v:X4}", $"{v} ps");
        }

        private static (string Raw, string Meaning) DecRank(byte[] d, int o)
        {
            byte b = d[o];
            byte v = (byte)((b >> 3) & 0x07);
            return ($"0x{b:X2}", $"{v + 1} Rank");
        }

        private static (string Raw, string Meaning) DecMfr(byte[] d, int o)
        {
            byte b1 = d[o], b2 = d[o + 1];
            string name = SpdInfo.LookupMfrName(b1, b2);
            return ($"{b1:X2}/{b2:X2}", name ?? "Unknown");
        }

        private static (string Raw, string Meaning) DecCrcLE(byte[] d, int o)
        {
            int v = d[o] | (d[o + 1] << 8);
            return ($"0x{v:X4}", "(stored)");
        }

        private static (string Raw, string Meaning) DecXmpId(byte[] d, int o)
        {
            byte b1 = d[o], b2 = d[o + 1], b3 = d[o + 2];
            bool ok = b1 == 0x0C && b2 == 0x4A && b3 == 0x30;
            return ($"{b1:X2}/{b2:X2}/{b3:X2}", ok ? "XMP 3.0 ✓" : "Disabled");
        }

        private static (string Raw, string Meaning) DecXmpProfiles(byte[] d, int o)
        {
            byte b = d[o];
            string m = b == 0x00 ? "None"
                     : b == 0x01 ? "P1 only"
                     : b == 0x03 ? "P1 + P2"
                     : b == 0x07 ? "P1 + P2 + P3"
                     : $"raw 0x{b:X2}";
            return ($"0x{b:X2}", m);
        }

        private void UpdateStatusBar()
        {
            string xmp = _info != null && _info.XmpEnabled ? "ON" : "OFF";
            _statusLabel.Text =
                $"{GetDisplayName()}{(_dirty ? "  *" : "")}   |   {_data.Length} bytes   |   XMP: {xmp}";
        }

        private void UpdateTitle()
        {
            Text = $"DDR5 SPD Editor  v1.0   —   {GetDisplayName()}{(_dirty ? "  *" : "")}";
        }

        private string GetDisplayName() =>
            _filePath != null ? Path.GetFileName(_filePath) : "(새 파일)";

        // ── Save / Save As ───────────────────────────────────────────────────
        private void SaveFile()
        {
            if (_filePath == null) { SaveFileAs(); return; }
            WriteFile(_filePath);
        }

        private void SaveFileAs()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter   = "SPD Files (*.sp5)|*.sp5|Binary Files (*.bin)|*.bin";
                dlg.FileName = _filePath != null ? Path.GetFileName(_filePath) : "new_file.sp5";
                dlg.Title    = "SPD 파일 저장";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                WriteFile(dlg.FileName);
            }
        }

        private void WriteFile(string path)
        {
            try
            {
                if (string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase))
                    File.WriteAllBytes(path, _data);
                else
                    File.WriteAllText(path, SpdFixer.SerializeToSp5(_data), Encoding.ASCII);

                _originalData = (byte[])_data.Clone();
                _filePath     = path;
                _dirty        = false;
                UpdateStatusBar();
                UpdateTitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"저장 실패:\n{ex.Message}",
                    "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowComingSoon(string message) =>
            MessageBox.Show(message, "준비 중", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
