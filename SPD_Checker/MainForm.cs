using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SPD_Checker.Logic;
using SPD_Checker.Models;

namespace SPD_Checker
{
    public class MainForm : Form
    {
        // ── Controls ─────────────────────────────────────────────────────────
        private Panel        pnlDropInner;
        private Label        lblDropText;
        private Label        lblFileCount;
        private Button       btnBrowse;
        private Button       btnClear;
        private Button       btnRun;
        private Button       btnExport;
        private Button       btnFix;
        private ContextMenuStrip _fixMenu;
        private Button       _btnFilterPass;
        private Button       _btnFilterFail;
        private Button       _btnFilterSkip;
        private ProgressBar  progressBar;
        private Label        lblProgress;
        private DataGridView dgvResults;
        private Label        lblSummary;
        private Label        lblStatsContent;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly List<string>                        _files       = new List<string>();
        private readonly List<CheckResult>                   _results     = new List<CheckResult>();
        private readonly Dictionary<string,int>              _failStats   = new Dictionary<string, int>();
        private readonly Dictionary<string,List<CheckResult>> _fileResults = new Dictionary<string, List<CheckResult>>();
        private BackgroundWorker                             _worker;

        private int _filePass;
        private int _fileFail;
        private int _fileSkip;

        private bool _showPass = true;
        private bool _showFail = true;
        private bool _showSkip = true;

        // ── Check Items (side panel static list) ──────────────────────────────
        private static readonly (string Phase, string Item)[] CHECK_ITEMS_LIST =
        {
            ("Ph.1", "Part Number"),
            ("Ph.2", "Module Mfr ID"),
            ("",     "DRAM Mfr ID"),
            ("Ph.3", "DRAM Type"),
            ("",     "Module Type"),
            ("",     "DIMM Type (XMP)"),
            ("",     "Die Density"),
            ("",     "I/O Width"),
            ("",     "Bank Groups"),
            ("",     "VDD Nominal"),
            ("",     "tCKAVGmin"),
            ("",     "tAA / tRCD / tRP"),
            ("",     "Module Rank"),
            ("",     "Module Density"),
            ("Ph.4", "CRC"),
            ("XMP",  "ID / Profiles / Global CRC"),
            ("",     "P1: VPP / VDD / VDDQ"),
            ("",     "P1: tCK / tAA / tRCD / tRP"),
            ("",     "P1: CL Mask"),
            ("",     "P1: Name String"),
            ("",     "P1: CRC"),
            ("",     "P2: (6000 제외 동일)"),
        };

        // ─────────────────────────────────────────────────────────────────────
        public MainForm()
        {
            BuildUI();
            SetupWorker();
        }

        // ── UI Construction ──────────────────────────────────────────────────
        private void BuildUI()
        {
            Text          = "DDR5 SPD Checker  v1.0";
            Size          = new Size(1140, 730);
            MinimumSize   = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 9F);
            BackColor     = Color.FromArgb(245, 246, 248);

            // Header
            var pnlHeader = MakePanel(DockStyle.Top, 50, Color.FromArgb(28, 57, 95));
            var lblTitle  = MakeLabel("DDR5 SPD Checker", new Font("Segoe UI", 14F, FontStyle.Bold),
                                      Color.White, DockStyle.Fill, ContentAlignment.MiddleLeft);
            lblTitle.Padding = new Padding(15, 0, 0, 0);
            var lblVer = MakeLabel("v1.0  |  Ph.1 ~ 4",
                                   new Font("Segoe UI", 8F), Color.FromArgb(170, 195, 220),
                                   DockStyle.Right, ContentAlignment.MiddleCenter);
            lblVer.Width = 140;
            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblVer);

            // Drop Zone
            var pnlDrop = MakePanel(DockStyle.Top, 85, Color.FromArgb(245, 246, 248));
            pnlDrop.Padding   = new Padding(10, 8, 10, 8);
            pnlDrop.AllowDrop = true;
            pnlDrop.DragEnter += OnDragEnter;
            pnlDrop.DragDrop  += OnDragDrop;

            pnlDropInner = new Panel
            {
                Dock        = DockStyle.Fill,
                BackColor   = Color.FromArgb(230, 240, 255),
                BorderStyle = BorderStyle.FixedSingle,
                AllowDrop   = true,
                Cursor      = Cursors.Hand
            };
            pnlDropInner.DragEnter += OnDragEnter;
            pnlDropInner.DragDrop  += OnDragDrop;
            pnlDropInner.Click     += (s, e) => BtnBrowse_Click(s, e);

            lblDropText = new Label
            {
                Text      = "  Drag & Drop  .sp5  files here  —  or click to Browse",
                Font      = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(60, 110, 180),
                TextAlign = ContentAlignment.MiddleLeft,
                Dock      = DockStyle.Fill,
                AllowDrop = true,
                Cursor    = Cursors.Hand
            };
            lblDropText.DragEnter += OnDragEnter;
            lblDropText.DragDrop  += OnDragDrop;
            lblDropText.Click     += (s, e) => BtnBrowse_Click(s, e);

            pnlDropInner.Controls.Add(lblDropText);
            pnlDrop.Controls.Add(pnlDropInner);

            // Control Row
            var pnlCtrl = MakePanel(DockStyle.Top, 46, Color.FromArgb(245, 246, 248));
            pnlCtrl.Padding = new Padding(10, 6, 10, 6);

            btnBrowse    = MakeButton("Browse Files", Color.FromArgb(65, 125, 190), 0);
            btnClear     = MakeButton("Clear",        Color.FromArgb(108, 117, 125), 120);
            lblFileCount = new Label
            {
                Text      = "Files selected: 0",
                Location  = new Point(236, 10),
                Size      = new Size(185, 22),
                ForeColor = Color.FromArgb(50, 50, 50)
            };
            btnRun = MakeButton("▶  Run Check", Color.FromArgb(34, 153, 60), 0);
            btnRun.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRun.Size   = new Size(130, 32);
            btnExport = MakeButton("Export Log", Color.FromArgb(20, 155, 175), 0);
            btnExport.Anchor  = AnchorStyles.Top | AnchorStyles.Right;
            btnExport.Size    = new Size(110, 32);
            btnExport.Enabled = false;

            btnFix = MakeButton("Fix FAILs ▼", Color.FromArgb(170, 70, 20), 0);
            btnFix.Anchor  = AnchorStyles.Top | AnchorStyles.Right;
            btnFix.Size    = new Size(115, 32);
            btnFix.Enabled = false;
            _fixMenu = new ContextMenuStrip();
            _fixMenu.Items.Add("Save as _FIXED  (원본 보존)", null, (s, e) => RunFix(overwrite: false));
            _fixMenu.Items.Add(new ToolStripSeparator());
            _fixMenu.Items.Add("⚠  Overwrite Original", null, (s, e) => RunFix(overwrite: true));
            btnFix.Click += (s, e) => _fixMenu.Show(btnFix, new Point(0, btnFix.Height));

            _btnFilterPass = MakeFilterButton("PASS", Color.FromArgb(34, 153, 60),  430);
            _btnFilterFail = MakeFilterButton("FAIL", Color.FromArgb(210, 45, 55),  514);
            _btnFilterSkip = MakeFilterButton("SKIP", Color.FromArgb(150, 150, 150), 598);

            pnlCtrl.Controls.AddRange(new Control[] { btnBrowse, btnClear, lblFileCount, _btnFilterPass, _btnFilterFail, _btnFilterSkip, btnRun, btnExport, btnFix });
            pnlCtrl.Layout += (s, e) =>
            {
                btnRun.Location    = new Point(pnlCtrl.Width - 385, 7);
                btnExport.Location = new Point(pnlCtrl.Width - 245, 7);
                btnFix.Location    = new Point(pnlCtrl.Width - 125, 7);
            };

            // Progress Row
            var pnlProg = MakePanel(DockStyle.Top, 38, Color.FromArgb(237, 239, 243));
            pnlProg.Padding = new Padding(10, 5, 10, 5);

            progressBar = new ProgressBar
            {
                Dock      = DockStyle.Left,
                Width     = 460,
                Height    = 22,
                Style     = ProgressBarStyle.Continuous,
                ForeColor = Color.FromArgb(34, 153, 60)
            };
            this.Load += (s, e) => SetWindowTheme(progressBar.Handle, "", "");
            lblProgress = new Label
            {
                Dock      = DockStyle.Fill,
                Text      = "Ready",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(50, 50, 50),
                Padding   = new Padding(8, 0, 0, 0)
            };
            pnlProg.Controls.Add(lblProgress);
            pnlProg.Controls.Add(progressBar);

            // Side Panel
            var pnlSide = BuildSidePanel();

            // Results Grid
            dgvResults = new DataGridView
            {
                Dock                      = DockStyle.Fill,
                BackgroundColor           = Color.White,
                BorderStyle               = BorderStyle.None,
                RowHeadersVisible         = false,
                AllowUserToAddRows        = false,
                AllowUserToDeleteRows     = false,
                ReadOnly                  = true,
                SelectionMode             = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode       = DataGridViewAutoSizeColumnsMode.None,
                ColumnHeadersHeight       = 32,
                EnableHeadersVisualStyles = false,
                GridColor                 = Color.FromArgb(220, 224, 228),
                CellBorderStyle           = DataGridViewCellBorderStyle.SingleHorizontal
            };
            dgvResults.ColumnHeadersDefaultCellStyle.BackColor  = Color.FromArgb(28, 57, 95);
            dgvResults.ColumnHeadersDefaultCellStyle.ForeColor  = Color.White;
            dgvResults.ColumnHeadersDefaultCellStyle.Font       = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvResults.DefaultCellStyle.Font                    = new Font("Consolas", 9F);
            dgvResults.DefaultCellStyle.SelectionBackColor      = Color.FromArgb(200, 220, 245);
            dgvResults.DefaultCellStyle.SelectionForeColor      = Color.Black;
            dgvResults.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            dgvResults.RowTemplate.Height                       = 26;
            dgvResults.CellFormatting += DgvResults_CellFormatting;

            // File Name: auto-size to displayed content
            var colFile = new DataGridViewTextBoxColumn
            {
                Name         = "colFile",
                HeaderText   = "File Name",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells,
                MinimumWidth = 160,
                SortMode     = DataGridViewColumnSortMode.Automatic
            };
            // Result: fixed 80px, centered, bold
            var colResult = new DataGridViewTextBoxColumn
            {
                Name         = "colResult",
                HeaderText   = "Result",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width        = 80,
                MinimumWidth = 80,
                SortMode     = DataGridViewColumnSortMode.Automatic
            };
            colResult.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colResult.DefaultCellStyle.Font      = new Font("Segoe UI", 9F, FontStyle.Bold);
            // Failed Items: fill remaining
            var colFailed = new DataGridViewTextBoxColumn
            {
                Name         = "colFailed",
                HeaderText   = "Failed Items",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode     = DataGridViewColumnSortMode.Automatic
            };

            dgvResults.Columns.AddRange(colFile, colResult, colFailed);

            // Content area: grid (Fill) + side panel (Right)
            // pnlSide must be added AFTER dgvResults so it has higher index → docked first
            var pnlContent = new Panel { Dock = DockStyle.Fill };
            pnlContent.Controls.Add(dgvResults);
            pnlContent.Controls.Add(pnlSide);

            // Bottom Summary Bar
            var pnlBottom = MakePanel(DockStyle.Bottom, 32, Color.FromArgb(28, 57, 95));
            pnlBottom.Padding = new Padding(15, 0, 0, 0);
            lblSummary = MakeLabel("Total: 0  |  PASS: 0  |  FAIL: 0",
                                   new Font("Segoe UI", 9F, FontStyle.Bold),
                                   Color.White, DockStyle.Fill, ContentAlignment.MiddleLeft);
            pnlBottom.Controls.Add(lblSummary);

            // Compose (reverse order for DockStyle.Top stacking)
            Controls.Add(pnlContent);
            Controls.Add(pnlProg);
            Controls.Add(pnlCtrl);
            Controls.Add(pnlDrop);
            Controls.Add(pnlHeader);
            Controls.Add(pnlBottom);

            _btnFilterPass.Click += (s, e) => { _showPass = !_showPass; UpdateFilterButton(_btnFilterPass, _showPass, Color.FromArgb(34, 153, 60));  ApplyFilter(); };
            _btnFilterFail.Click += (s, e) => { _showFail = !_showFail; UpdateFilterButton(_btnFilterFail, _showFail, Color.FromArgb(210, 45, 55));  ApplyFilter(); };
            _btnFilterSkip.Click += (s, e) => { _showSkip = !_showSkip; UpdateFilterButton(_btnFilterSkip, _showSkip, Color.FromArgb(150, 150, 150)); ApplyFilter(); };

            btnBrowse.Click            += BtnBrowse_Click;
            btnClear.Click             += BtnClear_Click;
            btnRun.Click               += BtnRun_Click;
            btnExport.Click            += BtnExport_Click;
            dgvResults.CellDoubleClick += DgvResults_CellDoubleClick;
            dgvResults.CellMouseEnter  += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                string r = dgvResults.Rows[e.RowIndex].Cells["colResult"].Value?.ToString();
                dgvResults.Cursor = (r == "PASS" || r == "FAIL") ? Cursors.Hand : Cursors.Default;
            };
            dgvResults.CellMouseLeave  += (s, e) => dgvResults.Cursor = Cursors.Default;
            dgvResults.KeyDown         += DgvResults_KeyDown;
            dgvResults.CellToolTipTextNeeded += DgvResults_CellToolTipTextNeeded;
        }

        private Panel BuildSidePanel()
        {
            const int SIDE_W = 242;
            const int ROW_H  = 19;

            var pnlSide = new Panel
            {
                Dock      = DockStyle.Right,
                Width     = SIDE_W,
                BackColor = Color.FromArgb(240, 242, 246)
            };
            // Left border line
            pnlSide.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(Color.FromArgb(200, 206, 215), 1), 0, 0, 0, ((Panel)s).Height);

            // ── Check Items header ─────────────────────────────────────────
            var pnlCheckHdr = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.FromArgb(28, 57, 95) };
            pnlCheckHdr.Controls.Add(MakeLabel("  Check Items",
                new Font("Segoe UI", 8.5F, FontStyle.Bold), Color.White, DockStyle.Fill, ContentAlignment.MiddleLeft));

            // ── Check Items list ───────────────────────────────────────────
            int listH = CHECK_ITEMS_LIST.Length * ROW_H + 8;
            var pnlCheckList = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = listH,
                BackColor = Color.FromArgb(240, 242, 246)
            };
            for (int i = 0; i < CHECK_ITEMS_LIST.Length; i++)
            {
                var (phase, item) = CHECK_ITEMS_LIST[i];
                bool hasPhase = phase.Length > 0;

                var lblPhase = new Label
                {
                    Text      = phase,
                    Location  = new Point(6, 4 + i * ROW_H),
                    Size      = new Size(34, ROW_H),
                    Font      = new Font("Segoe UI", 7.5F, hasPhase ? FontStyle.Bold : FontStyle.Regular),
                    ForeColor = hasPhase ? Color.FromArgb(65, 125, 190) : Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                var lblItem = new Label
                {
                    Text      = item,
                    Location  = new Point(44, 4 + i * ROW_H),
                    Size      = new Size(SIDE_W - 50, ROW_H),
                    Font      = new Font("Segoe UI", 8.5F),
                    ForeColor = Color.FromArgb(35, 45, 60),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                pnlCheckList.Controls.Add(lblPhase);
                pnlCheckList.Controls.Add(lblItem);
            }

            // ── Divider ────────────────────────────────────────────────────
            var pnlDiv = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(200, 206, 215) };

            // ── FAIL Stats header ──────────────────────────────────────────
            var pnlStatsHdr = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.FromArgb(28, 57, 95) };
            pnlStatsHdr.Controls.Add(MakeLabel("  Session FAIL Stats",
                new Font("Segoe UI", 8.5F, FontStyle.Bold), Color.White, DockStyle.Fill, ContentAlignment.MiddleLeft));

            // ── FAIL Stats content ─────────────────────────────────────────
            var pnlStatsBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 242, 246) };
            lblStatsContent = new Label
            {
                Dock      = DockStyle.Fill,
                Text      = "  —",
                Font      = new Font("Consolas", 8.5F),
                ForeColor = Color.FromArgb(55, 65, 80),
                TextAlign = ContentAlignment.TopLeft,
                Padding   = new Padding(6, 6, 0, 0)
            };
            pnlStatsBody.Controls.Add(lblStatsContent);

            // Add in order: Fill first (index 0), then Top controls (higher index = docked first = appears higher)
            pnlSide.Controls.Add(pnlStatsBody);   // index 0 → Fill
            pnlSide.Controls.Add(pnlStatsHdr);    // index 1 → Top (bottom-most Top)
            pnlSide.Controls.Add(pnlDiv);          // index 2 → Top
            pnlSide.Controls.Add(pnlCheckList);    // index 3 → Top
            pnlSide.Controls.Add(pnlCheckHdr);     // index 4 → Top (appears at very top)

            return pnlSide;
        }

        // ── Background Worker ────────────────────────────────────────────────
        private void SetupWorker()
        {
            _worker = new BackgroundWorker
            {
                WorkerReportsProgress      = true,
                WorkerSupportsCancellation = true
            };
            _worker.DoWork             += Worker_DoWork;
            _worker.ProgressChanged    += Worker_ProgressChanged;
            _worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            var files = (List<string>)e.Argument;
            for (int i = 0; i < files.Count; i++)
            {
                if (_worker.CancellationPending) { e.Cancel = true; return; }
                string file    = files[i];
                var    results = SpdChecker.CheckFile(file);
                _worker.ReportProgress(0, new ProgressInfo
                {
                    FileIndex  = i + 1,
                    TotalFiles = files.Count,
                    FileName   = Path.GetFileName(file),
                    Results    = results
                });
            }
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            var info = (ProgressInfo)e.UserState;
            progressBar.Maximum = info.TotalFiles;
            progressBar.Value   = info.FileIndex;
            lblProgress.Text    = string.Format("Checking ({0}/{1}): {2}",
                                                info.FileIndex, info.TotalFiles, info.FileName);

            foreach (var r in info.Results)
                _results.Add(r);

            _fileResults[info.FileName] = info.Results;

            bool isSkip  = info.Results.Count == 1 && info.Results[0].Status == CheckStatus.Skip;
            bool allPass = !isSkip && info.Results.All(r => r.Status == CheckStatus.Pass);

            // Failed Items: 항목명만 표시 (Expected/Actual 제거)
            string failedStr = "";
            if (!allPass && !isSkip)
            {
                var failNames = info.Results
                    .Where(r => r.Status == CheckStatus.Fail)
                    .Select(r => r.CheckItem)
                    .ToList();
                failedStr = string.Join(",  ", failNames);

                foreach (string name in failNames)
                {
                    if (!_failStats.ContainsKey(name)) _failStats[name] = 0;
                    _failStats[name]++;
                }
                UpdateSidePanel();
            }
            else if (isSkip)
            {
                failedStr = info.Results[0].Note;
            }

            string overallResult = isSkip ? "SKIP" : (allPass ? "PASS" : "FAIL");
            int rowIdx = dgvResults.Rows.Add(info.FileName, overallResult, failedStr);

            var row = dgvResults.Rows[rowIdx];
            if (allPass)
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 255, 240);
            else if (isSkip)
                row.DefaultCellStyle.BackColor = Color.FromArgb(235, 235, 235);
            else
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);

            row.Visible = overallResult switch { "PASS" => _showPass, "FAIL" => _showFail, "SKIP" => _showSkip, _ => true };

            if (allPass)     _filePass++;
            else if (isSkip) _fileSkip++;
            else             _fileFail++;

            UpdateSummary();
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetRunning(false);
            if (e.Cancelled)
                lblProgress.Text = "Cancelled.";
            else if (e.Error != null)
                lblProgress.Text = "Error: " + e.Error.Message;
            else
            {
                lblProgress.Text = string.Format(
                    "Complete.  Files: {0}   PASS: {1}   FAIL: {2}   SKIP: {3}",
                    _filePass + _fileFail + _fileSkip, _filePass, _fileFail, _fileSkip);
                progressBar.Value    = progressBar.Maximum;
                progressBar.ForeColor = _fileFail > 0
                    ? Color.FromArgb(210, 45, 55)
                    : Color.FromArgb(34, 153, 60);
            }
            UpdateSummary();
        }

        // ── Button Events ────────────────────────────────────────────────────
        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter      = "SPD Files (*.sp5)|*.sp5|All files (*.*)|*.*";
                dlg.Multiselect = true;
                dlg.Title       = "Select SPD Files";
                if (dlg.ShowDialog() == DialogResult.OK)
                    AddFiles(dlg.FileNames);
            }
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            _files.Clear();
            _results.Clear();
            _failStats.Clear();
            _fileResults.Clear();
            dgvResults.Rows.Clear();
            _filePass = _fileFail = _fileSkip = 0;
            progressBar.Value = 0;
            lblProgress.Text  = "Ready";
            btnExport.Enabled = false;
            UpdateFileCount();
            UpdateSummary();
            UpdateSidePanel();
        }

        private void BtnRun_Click(object sender, EventArgs e)
        {
            if (_files.Count == 0)
            {
                MessageBox.Show("Please select .sp5 files first.",
                    "No Files Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _results.Clear();
            _failStats.Clear();
            _fileResults.Clear();
            dgvResults.Rows.Clear();
            _filePass = _fileFail = _fileSkip = 0;
            progressBar.Value     = 0;
            progressBar.ForeColor = Color.FromArgb(34, 153, 60);
            UpdateSidePanel();
            SetRunning(true);
            _worker.RunWorkerAsync(_files.ToList());
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter   = "CSV Files (*.csv)|*.csv|Text Files (*.txt)|*.txt";
                dlg.FileName = "SPD_Check_Log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                dlg.Title    = "Export Log";
                if (dlg.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("DDR5 SPD Checker — Export Log");
                    sb.AppendLine("Generated : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    sb.AppendLine(string.Format("Total: {0}  |  PASS: {1}  |  FAIL: {2}",
                        _results.Count, _results.Count(r => r.Pass), _results.Count(r => !r.Pass)));
                    sb.AppendLine();
                    sb.AppendLine("File Name,Check Item,Expected,Actual,Result,Note");
                    foreach (var r in _results)
                        sb.AppendLine(string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\",{4},\"{5}\"",
                            r.FileName, r.CheckItem, r.Expected, r.Actual, r.Result, r.Note));

                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("Log exported:\n" + dlg.FileName,
                        "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Export failed: " + ex.Message,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ── Drag & Drop ──────────────────────────────────────────────────────
        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                pnlDropInner.BackColor = Color.FromArgb(195, 220, 255);
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            pnlDropInner.BackColor = Color.FromArgb(230, 240, 255);
            var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
            AddFiles(dropped);
        }

        // ── Grid Formatting ──────────────────────────────────────────────────
        private void DgvResults_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (dgvResults.Columns[e.ColumnIndex].Name != "colResult" || e.Value == null) return;
            switch (e.Value.ToString())
            {
                case "PASS":
                    e.CellStyle.BackColor = Color.FromArgb(34, 153, 60);
                    e.CellStyle.ForeColor = Color.White;
                    break;
                case "FAIL":
                    e.CellStyle.BackColor = Color.FromArgb(210, 45, 55);
                    e.CellStyle.ForeColor = Color.White;
                    break;
                case "SKIP":
                    e.CellStyle.BackColor = Color.FromArgb(150, 150, 150);
                    e.CellStyle.ForeColor = Color.White;
                    break;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private void AddFiles(string[] paths)
        {
            foreach (string path in paths)
            {
                if (path.EndsWith(".sp5", StringComparison.OrdinalIgnoreCase)
                    && !_files.Contains(path))
                    _files.Add(path);
            }
            UpdateFileCount();
        }

        private void UpdateFileCount()
        {
            lblFileCount.Text = string.Format("Files selected: {0}", _files.Count);
            lblDropText.Text  = _files.Count == 0
                ? "  Drag & Drop  .sp5  files here  —  or click to Browse"
                : string.Format("  {0} file(s) loaded  —  drag more to add, or click Browse", _files.Count);
        }

        private void UpdateSummary()
        {
            int total = _filePass + _fileFail + _fileSkip;
            lblSummary.Text = string.Format(
                "Files: {0}  |  PASS: {1}  |  FAIL: {2}  |  SKIP: {3}",
                total, _filePass, _fileFail, _fileSkip);
            lblSummary.ForeColor = _fileFail > 0 ? Color.FromArgb(255, 120, 120) : Color.White;
        }

        private void UpdateSidePanel()
        {
            if (_failStats.Count == 0)
            {
                lblStatsContent.Text = "  —";
                return;
            }
            var sb = new StringBuilder();
            foreach (var kvp in _failStats.OrderByDescending(k => k.Value))
                sb.AppendLine(string.Format("  {0} ×{1}", kvp.Key.PadRight(16), kvp.Value));
            lblStatsContent.Text = sb.ToString();
        }

        private void SetRunning(bool running)
        {
            btnRun.Enabled    = !running;
            btnBrowse.Enabled = !running;
            btnClear.Enabled  = !running;
            btnExport.Enabled = !running && _results.Count > 0;
            btnFix.Enabled    = !running && _results.Any(r => r.Status == CheckStatus.Fail);
        }

        private void RunFix(bool overwrite)
        {
            if (overwrite)
            {
                var confirm = MessageBox.Show(
                    "원본 파일을 덮어씁니다. 계속할까요?",
                    "Overwrite Original", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            var failFileNames = _results
                .Where(r => r.Status == CheckStatus.Fail)
                .Select(r => r.FileName)
                .Distinct()
                .ToList();

            var fixed_ = 0;
            var errors = new List<string>();

            foreach (string name in failFileNames)
            {
                string path = _files.FirstOrDefault(f =>
                    string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
                if (path == null) { errors.Add(name + ": 경로 없음"); continue; }
                try
                {
                    string text    = File.ReadAllText(path);
                    byte[] data    = SpdChecker.ParseSpdText(text);
                    byte[] fixedData = SpdFixer.ApplyFixes(data, path);
                    if (overwrite)
                        SpdFixer.SaveOverwrite(path, fixedData);
                    else
                        SpdFixer.SaveAsFixed(path, fixedData);
                    fixed_++;
                }
                catch (Exception ex)
                {
                    errors.Add(name + ": " + ex.Message);
                }
            }

            string msg = string.Format("{0}개 파일 수정 완료.", fixed_);
            if (errors.Count > 0) msg += "\n\n오류:\n" + string.Join("\n", errors);
            msg += "\n\nRun 버튼을 눌러 재검사하세요.";
            MessageBox.Show(msg, "Fix 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            UpdateFileCount();
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

        // ── UI Factory Methods ───────────────────────────────────────────────
        private static Panel MakePanel(DockStyle dock, int height, Color bg)
            => new Panel { Dock = dock, Height = height, BackColor = bg };

        private static Label MakeLabel(string text, Font font, Color fore,
                                       DockStyle dock, ContentAlignment align)
            => new Label { Text = text, Font = font, ForeColor = fore, Dock = dock, TextAlign = align };

        private static Button MakeButton(string text, Color bg, int x)
            => new Button
            {
                Text      = text,
                Location  = new Point(x, 7),
                Size      = new Size(108, 32),
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
                FlatAppearance = { BorderSize = 0 }
            };

        private static Button MakeFilterButton(string text, Color activeColor, int x)
        {
            var btn = new Button
            {
                Text      = text,
                Location  = new Point(x, 7),
                Size      = new Size(80, 32),
                BackColor = activeColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private static void UpdateFilterButton(Button btn, bool active, Color activeColor)
        {
            btn.BackColor = active ? activeColor : Color.FromArgb(200, 200, 200);
            btn.ForeColor = active ? Color.White  : Color.FromArgb(100, 100, 100);
        }

        private void ApplyFilter()
        {
            foreach (DataGridViewRow row in dgvResults.Rows)
            {
                string result = row.Cells["colResult"].Value?.ToString();
                row.Visible = result switch
                {
                    "PASS" => _showPass,
                    "FAIL" => _showFail,
                    "SKIP" => _showSkip,
                    _      => true
                };
            }
        }

        // ── Double-click Detail ───────────────────────────────────────────────
        private void DgvResults_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string fileName = dgvResults.Rows[e.RowIndex].Cells["colFile"].Value?.ToString();
            string result   = dgvResults.Rows[e.RowIndex].Cells["colResult"].Value?.ToString();
            if (result == "SKIP" || fileName == null) return;
            if (!_fileResults.TryGetValue(fileName, out var results)) return;
            using (var dlg = new DetailForm(fileName, results))
                dlg.ShowDialog(this);
        }

        private void DgvResults_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled        = true;
            e.SuppressKeyPress = true;
            if (dgvResults.CurrentRow == null) return;
            int    rowIdx  = dgvResults.CurrentRow.Index;
            string fileName = dgvResults.Rows[rowIdx].Cells["colFile"].Value?.ToString();
            string result   = dgvResults.Rows[rowIdx].Cells["colResult"].Value?.ToString();
            if (result == "SKIP" || fileName == null) return;
            if (!_fileResults.TryGetValue(fileName, out var results)) return;
            using (var dlg = new DetailForm(fileName, results))
                dlg.ShowDialog(this);
        }

        private void DgvResults_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string fileName = dgvResults.Rows[e.RowIndex].Cells["colFile"].Value?.ToString();
            if (fileName == null || !_fileResults.TryGetValue(fileName, out var results)) return;
            var lines = results
                .Where(r => r.Status == CheckStatus.Fail && !string.IsNullOrEmpty(r.Note))
                .Select(r => $"[{r.CheckItem}]  {r.Note}");
            e.ToolTipText = string.Join("\n", lines);
        }

        // ── Detail Dialog ─────────────────────────────────────────────────────
        private class DetailForm : Form
        {
            public DetailForm(string fileName, List<CheckResult> results)
            {
                Text          = fileName + "  —  Detail";
                Size          = new Size(820, 480);
                MinimumSize   = new Size(600, 360);
                StartPosition = FormStartPosition.CenterParent;
                Font          = new Font("Segoe UI", 9F);
                BackColor     = Color.FromArgb(245, 246, 248);

                // Header
                var pnlHdr = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(28, 57, 95) };
                var lblHdr = new Label
                {
                    Text      = "  " + fileName,
                    Font      = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Dock      = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                pnlHdr.Controls.Add(lblHdr);

                // Grid
                var dgv = new DataGridView
                {
                    Dock                      = DockStyle.Fill,
                    BackgroundColor           = Color.White,
                    BorderStyle               = BorderStyle.None,
                    RowHeadersVisible         = false,
                    AllowUserToAddRows        = false,
                    AllowUserToDeleteRows     = false,
                    ReadOnly                  = true,
                    SelectionMode             = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode       = DataGridViewAutoSizeColumnsMode.None,
                    ColumnHeadersHeight       = 30,
                    EnableHeadersVisualStyles = false,
                    GridColor                 = Color.FromArgb(220, 224, 228),
                    CellBorderStyle           = DataGridViewCellBorderStyle.SingleHorizontal
                };
                dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(28, 57, 95);
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
                dgv.ColumnHeadersDefaultCellStyle.Font      = new Font("Segoe UI", 9F, FontStyle.Bold);
                dgv.DefaultCellStyle.Font                   = new Font("Consolas", 9F);
                dgv.RowTemplate.Height                      = 24;

                var colItem = new DataGridViewTextBoxColumn { Name = "Item",     HeaderText = "Check Item", AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells, MinimumWidth = 120 };
                var colExp  = new DataGridViewTextBoxColumn { Name = "Expected", HeaderText = "Expected",   AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 };
                var colAct  = new DataGridViewTextBoxColumn { Name = "Actual",   HeaderText = "Actual",     AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 };
                var colRes  = new DataGridViewTextBoxColumn
                {
                    Name         = "Result",
                    HeaderText   = "Result",
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                    Width        = 72,
                    MinimumWidth = 72
                };
                colRes.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                colRes.DefaultCellStyle.Font      = new Font("Segoe UI", 9F, FontStyle.Bold);
                var colNote = new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Note", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 30 };
                colNote.DefaultCellStyle.ForeColor = Color.FromArgb(90, 100, 115);

                dgv.Columns.AddRange(colItem, colExp, colAct, colRes, colNote);

                foreach (var r in results)
                {
                    int idx = dgv.Rows.Add(r.CheckItem, r.Expected, r.Actual,
                                           r.Pass ? "PASS" : "FAIL", r.Note);
                    var row = dgv.Rows[idx];
                    row.DefaultCellStyle.BackColor = r.Pass
                        ? Color.FromArgb(240, 255, 240)
                        : Color.FromArgb(255, 235, 235);
                }

                dgv.CellFormatting += (s, e) =>
                {
                    if (dgv.Columns[e.ColumnIndex].Name != "Result" || e.Value == null) return;
                    if (e.Value.ToString() == "PASS")
                    { e.CellStyle.BackColor = Color.FromArgb(34, 153, 60); e.CellStyle.ForeColor = Color.White; }
                    else
                    { e.CellStyle.BackColor = Color.FromArgb(210, 45, 55); e.CellStyle.ForeColor = Color.White; }
                };

                // Bottom close button
                var pnlBot = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Color.FromArgb(237, 239, 243) };
                var btnClose = new Button
                {
                    Text      = "Close",
                    Size      = new Size(90, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(108, 117, 125),
                    ForeColor = Color.White,
                    Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Anchor    = AnchorStyles.Right | AnchorStyles.Top
                };
                btnClose.FlatAppearance.BorderSize = 0;
                btnClose.Click += (s, e) => Close();
                pnlBot.Layout += (s, e) => btnClose.Location = new Point(pnlBot.Width - 104, 7);
                pnlBot.Controls.Add(btnClose);

                Controls.Add(dgv);
                Controls.Add(pnlBot);
                Controls.Add(pnlHdr);
            }
        }

        // ── Progress Info ─────────────────────────────────────────────────────
        private class ProgressInfo
        {
            public int               FileIndex  { get; set; }
            public int               TotalFiles { get; set; }
            public string            FileName   { get; set; }
            public List<CheckResult> Results    { get; set; }
        }
    }
}
