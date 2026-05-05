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
        private byte[]  _data = new byte[SPD_SIZE];
        private SpdInfo _info;
        private string  _filePath;
        private bool    _dirty;
        private Dictionary<string, CheckResult> _checkResults = new Dictionary<string, CheckResult>();

        // ── UI ───────────────────────────────────────────────────────────────
        private DataGridView _hexGrid;
        private Panel        _rightPanel;
        private Label        _rightInfoLabel;
        private Label        _statusLabel;

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
            new KeyByteGroup { Offset = 512, Length = 2, Name = "Module Mfr ID",  CheckItem = "Module Mfr ID",          Decode = DecMfr         },
            new KeyByteGroup { Offset = 552, Length = 2, Name = "DRAM Mfr ID",    CheckItem = "DRAM Mfr ID",            Decode = DecMfr         },
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
            Text          = "DDR5 SPD Editor  v2.0";
            Size          = new Size(1240, 760);
            MinimumSize   = new Size(1000, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 9F);
            BackColor     = Color.FromArgb(245, 246, 248);
            FormClosing  += OnFormClosing;

            // 1. Hex grid (Fill) — added first
            _hexGrid = BuildHexGrid();
            Controls.Add(_hexGrid);

            // 2. Right panel
            _rightPanel = new Panel
            {
                Dock       = DockStyle.Right,
                Width      = 380,
                BackColor  = Color.White,
                AutoScroll = true,
                Padding    = new Padding(10)
            };
            _rightInfoLabel = new Label
            {
                Dock      = DockStyle.Top,
                AutoSize  = true,
                Font      = new Font("Consolas", 9F),
                ForeColor = Color.FromArgb(40, 40, 50)
            };
            _rightPanel.Controls.Add(_rightInfoLabel);
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

            btnLoad.Click    += (s, e) => LoadFileDialog();
            btnNew.Click     += (s, e) => NewFile();
            btnSave.Click    += (s, e) => SaveFile();
            btnSaveAs.Click  += (s, e) => SaveFileAs();
            btnCrc.Click     += (s, e) => RecalculateCrcs();
            btnAutoFix.Click += (s, e) => AutoFix();

            toolbar.Controls.AddRange(new Control[] { btnLoad, btnNew, btnSave, btnSaveAs, btnCrc, btnAutoFix });
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
                RowHeadersWidth             = 56,
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
                    Alignment = DataGridViewContentAlignment.MiddleRight,
                    Padding   = new Padding(0, 0, 6, 0)
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
            grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.EnableResizing;
            grid.Rows.Add(ROWS);
            for (int r = 0; r < ROWS; r++)
                grid.Rows[r].HeaderCell.Value = r.ToString("X2");   // 00 ~ 3F (high byte of 16-byte row)

            grid.CellEndEdit += OnCellEndEdit;
            return grid;
        }

        // ── Key Byte 툴팁 (셀 hover 시 어떤 byte인지 설명) ──────────────────
        private void ApplyKeyByteTooltips()
        {
            foreach (var g in KEY_BYTE_GROUPS)
            {
                string range = g.Length == 1
                    ? $"Byte 0x{g.Offset:X3}"
                    : $"Byte 0x{g.Offset:X3}-0x{g.Offset + g.Length - 1:X3}";
                string tip = $"{range} — {g.Name}";
                for (int i = 0; i < g.Length; i++)
                {
                    int off = g.Offset + i;
                    if (off >= SPD_SIZE) continue;
                    _hexGrid[off % COLS, off / COLS].ToolTipText = tip;
                }
            }
        }

        // ── File Operations ──────────────────────────────────────────────────
        private void NewFile()
        {
            if (!ConfirmDirtyDiscard()) return;
            _data     = new byte[SPD_SIZE];
            _filePath = null;
            _dirty    = false;
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
                _filePath = path;
                _dirty    = false;
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
            if (!ConfirmDirtyDiscard()) e.Cancel = true;
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

        private void HighlightKeyBytes()
        {
            foreach (var g in KEY_BYTE_GROUPS)
            {
                Color color = CLR_KEY_DEFAULT;
                if (g.CheckItem != null && _checkResults.TryGetValue(g.CheckItem, out var r))
                {
                    if (r.Status == CheckStatus.Pass)      color = CLR_PASS;
                    else if (r.Status == CheckStatus.Fail) color = CLR_FAIL;
                }
                for (int i = 0; i < g.Length; i++)
                {
                    int off = g.Offset + i;
                    if (off >= SPD_SIZE) continue;
                    _hexGrid[off % COLS, off / COLS].Style.BackColor = color;
                }
            }
            ApplyKeyByteTooltips();
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

            var sb = new StringBuilder();
            AppendPartInfo(sb);
            sb.AppendLine();
            AppendTemplate(sb);
            sb.AppendLine();
            AppendKeyBytes(sb);
            _rightInfoLabel.Text = sb.ToString();

            HighlightKeyBytes();   // 검증 결과 기반 색상 갱신
            UpdateStatusBar();
            UpdateTitle();
        }

        private void AppendPartInfo(StringBuilder sb)
        {
            sb.AppendLine("─── Part Information ─────────────");
            sb.AppendLine();
            sb.AppendLine($"  Part No   {_info.PartNumberAscii ?? "-"}");
            sb.AppendLine($"  Speed     {_info.SpeedName ?? "-"}");
            sb.AppendLine($"  Density   {(_info.DensityGb.HasValue ? _info.DensityGb + " GB" : "-")}");
            sb.AppendLine($"  Module    {_info.ModuleTypeName ?? "-"}");
            sb.AppendLine($"  Rank      {_info.RankName ?? "-"}");
            sb.AppendLine($"  Mfr (Mod) {_info.ModuleMfrName ?? "-"}");
            sb.AppendLine($"  Mfr (DRAM){_info.DramMfrName ?? "-"}");
            sb.AppendLine($"  XMP       {(_info.XmpEnabled ? "✓ Enabled" : "✗ Disabled")}");
            if (!string.IsNullOrEmpty(_info.Fields.Error))
                sb.AppendLine($"  ⚠ {_info.Fields.Error}");
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
            Text = $"DDR5 SPD Editor  v2.0   —   {GetDisplayName()}{(_dirty ? "  *" : "")}";
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

                _filePath = path;
                _dirty    = false;
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
