using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SPD_Checker.Logic;
using SPD_Checker.Models;

namespace SPD_Checker
{
    public class MainForm : Form
    {
        // ── Controls ─────────────────────────────────────────────────────────
        private Panel           pnlDropInner;
        private Label           lblDropText;
        private Label           lblFileCount;
        private Button          btnBrowse;
        private Button          btnClear;
        private Button          btnRun;
        private Button          btnExport;
        private ProgressBar     progressBar;
        private Label           lblProgress;
        private DataGridView    dgvResults;
        private Label           lblSummary;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly List<string>      _files   = new List<string>();
        private readonly List<CheckResult> _results = new List<CheckResult>();  // 로그 export용 전체 결과
        private BackgroundWorker           _worker;

        // 파일 단위 카운터
        private int _filePass;
        private int _fileFail;
        private int _fileSkip;

        // ─────────────────────────────────────────────────────────────────────
        public MainForm()
        {
            BuildUI();
            SetupWorker();
        }

        // ── UI Construction ──────────────────────────────────────────────────
        private void BuildUI()
        {
            Text            = "DDR5 SPD Checker  v1.0";
            Size            = new Size(1140, 730);
            MinimumSize     = new Size(900,  600);
            StartPosition   = FormStartPosition.CenterScreen;
            Font            = new Font("Segoe UI", 9F);
            BackColor       = Color.FromArgb(245, 246, 248);

            // Header
            var pnlHeader = MakePanel(DockStyle.Top, 50, Color.FromArgb(28, 57, 95));
            var lblTitle  = MakeLabel("DDR5 SPD Checker", new Font("Segoe UI", 14F, FontStyle.Bold),
                                      Color.White, DockStyle.Fill, ContentAlignment.MiddleLeft);
            lblTitle.Padding = new Padding(15, 0, 0, 0);
            var lblVer    = MakeLabel("v1.0  |  Phase 1: Part Number",
                                      new Font("Segoe UI", 8F), Color.FromArgb(170, 195, 220),
                                      DockStyle.Right, ContentAlignment.MiddleCenter);
            lblVer.Width  = 230;
            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblVer);

            // Drop Zone
            var pnlDrop = MakePanel(DockStyle.Top, 85, Color.FromArgb(245, 246, 248));
            pnlDrop.Padding  = new Padding(10, 8, 10, 8);
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

            btnBrowse    = MakeButton("Browse Files",  Color.FromArgb(65, 125, 190), 0);
            btnClear     = MakeButton("Clear",         Color.FromArgb(108, 117, 125), 120);
            lblFileCount = new Label
            {
                Text      = "Files selected: 0",
                Location  = new Point(205, 10),
                Size      = new Size(220, 22),
                ForeColor = Color.FromArgb(50, 50, 50)
            };
            btnRun    = MakeButton("▶  Run Check", Color.FromArgb(34, 153, 60), 0);
            btnRun.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRun.Size   = new Size(130, 32);
            btnExport = MakeButton("Export Log",   Color.FromArgb(20, 155, 175), 0);
            btnExport.Anchor  = AnchorStyles.Top | AnchorStyles.Right;
            btnExport.Size    = new Size(110, 32);
            btnExport.Enabled = false;

            pnlCtrl.Controls.AddRange(new Control[]
                { btnBrowse, btnClear, lblFileCount, btnRun, btnExport });
            pnlCtrl.Layout += (s, e) =>
            {
                btnRun.Location    = new Point(pnlCtrl.Width - 255, 7);
                btnExport.Location = new Point(pnlCtrl.Width - 120, 7);
            };

            // Progress Row
            var pnlProg = MakePanel(DockStyle.Top, 38, Color.FromArgb(237, 239, 243));
            pnlProg.Padding = new Padding(10, 5, 10, 5);

            progressBar = new ProgressBar
            {
                Dock   = DockStyle.Left,
                Width  = 460,
                Height = 22,
                Style  = ProgressBarStyle.Continuous
            };
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

            // Results Grid
            dgvResults = new DataGridView
            {
                Dock                            = DockStyle.Fill,
                BackgroundColor                 = Color.White,
                BorderStyle                     = BorderStyle.None,
                RowHeadersVisible               = false,
                AllowUserToAddRows              = false,
                AllowUserToDeleteRows           = false,
                ReadOnly                        = true,
                SelectionMode                   = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode             = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeight             = 32,
                EnableHeadersVisualStyles       = false,
                GridColor                       = Color.FromArgb(220, 224, 228),
                CellBorderStyle                 = DataGridViewCellBorderStyle.SingleHorizontal
            };
            dgvResults.ColumnHeadersDefaultCellStyle.BackColor  = Color.FromArgb(28, 57, 95);
            dgvResults.ColumnHeadersDefaultCellStyle.ForeColor  = Color.White;
            dgvResults.ColumnHeadersDefaultCellStyle.Font       = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvResults.DefaultCellStyle.Font                    = new Font("Consolas", 9F);
            dgvResults.DefaultCellStyle.SelectionBackColor      = Color.FromArgb(200, 220, 245);
            dgvResults.DefaultCellStyle.SelectionForeColor      = Color.Black;
            dgvResults.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            dgvResults.RowTemplate.Height                        = 26;
            dgvResults.CellFormatting += DgvResults_CellFormatting;

            AddColumn(dgvResults, "colFile",   "File Name",     35);
            AddColumn(dgvResults, "colResult", "Result",         8, centered: true, bold: true);
            AddColumn(dgvResults, "colFailed", "Failed Checks", 57);

            // Bottom Summary Bar
            var pnlBottom = MakePanel(DockStyle.Bottom, 32, Color.FromArgb(28, 57, 95));
            pnlBottom.Padding = new Padding(15, 0, 0, 0);
            lblSummary = MakeLabel("Total: 0  |  PASS: 0  |  FAIL: 0",
                                   new Font("Segoe UI", 9F, FontStyle.Bold),
                                   Color.White, DockStyle.Fill, ContentAlignment.MiddleLeft);
            pnlBottom.Controls.Add(lblSummary);

            // Compose (reverse order for DockStyle.Top stacking)
            Controls.Add(dgvResults);
            Controls.Add(pnlProg);
            Controls.Add(pnlCtrl);
            Controls.Add(pnlDrop);
            Controls.Add(pnlHeader);
            Controls.Add(pnlBottom);

            // Wire events
            btnBrowse.Click += BtnBrowse_Click;
            btnClear.Click  += BtnClear_Click;
            btnRun.Click    += BtnRun_Click;
            btnExport.Click += BtnExport_Click;
        }

        // ── Background Worker ────────────────────────────────────────────────
        private void SetupWorker()
        {
            _worker = new BackgroundWorker
            {
                WorkerReportsProgress    = true,
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

            // 로그용 전체 결과 저장
            foreach (var r in info.Results)
                _results.Add(r);

            // 파일 단위 결과 판정
            bool isSkip  = info.Results.Count == 1 && info.Results[0].Status == CheckStatus.Skip;
            bool allPass = !isSkip && info.Results.All(r => r.Status == CheckStatus.Pass);

            // FAIL 항목 텍스트 조합 ([번호] CheckItem  Expected → Actual)
            string failedStr = "";
            if (!allPass && !isSkip)
            {
                var parts = new List<string>();
                for (int i = 0; i < info.Results.Count; i++)
                {
                    var r = info.Results[i];
                    if (r.Status == CheckStatus.Fail)
                        parts.Add(string.Format("[{0}] {1}   Expected: {2}   /   Actual: {3}",
                            i + 1, r.CheckItem, r.Expected, r.Actual));
                }
                failedStr = string.Join("     |     ", parts);
            }
            else if (isSkip)
            {
                failedStr = info.Results[0].Note;
            }

            // 행 추가 (파일 1개 = 행 1개)
            string overallResult = isSkip ? "SKIP" : (allPass ? "PASS" : "FAIL");
            int rowIdx = dgvResults.Rows.Add(info.FileName, overallResult, failedStr);

            // 행 배경색 설정
            var row = dgvResults.Rows[rowIdx];
            if (allPass)
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 255, 240);
            else if (isSkip)
                row.DefaultCellStyle.BackColor = Color.FromArgb(235, 235, 235);
            else
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);

            // 파일 단위 카운터
            if (allPass)       _filePass++;
            else if (isSkip)   _fileSkip++;
            else               _fileFail++;

            UpdateSummary();
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetRunning(false);
            if (e.Cancelled)
            {
                lblProgress.Text = "Cancelled.";
            }
            else if (e.Error != null)
            {
                lblProgress.Text = "Error: " + e.Error.Message;
            }
            else
            {
                lblProgress.Text = string.Format(
                    "Complete.  Files: {0}   PASS: {1}   FAIL: {2}   SKIP: {3}",
                    _filePass + _fileFail + _fileSkip, _filePass, _fileFail, _fileSkip);
                progressBar.Value = progressBar.Maximum;
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
            dgvResults.Rows.Clear();
            _filePass = _fileFail = _fileSkip = 0;
            progressBar.Value = 0;
            lblProgress.Text  = "Ready";
            btnExport.Enabled = false;
            UpdateFileCount();
            UpdateSummary();
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
            dgvResults.Rows.Clear();
            progressBar.Value = 0;
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
            string val = e.Value.ToString();
            if (val == "PASS")
            {
                e.CellStyle.BackColor = Color.FromArgb(34, 153, 60);
                e.CellStyle.ForeColor = Color.White;
            }
            else if (val == "FAIL")
            {
                e.CellStyle.BackColor = Color.FromArgb(210, 45, 55);
                e.CellStyle.ForeColor = Color.White;
            }
            else if (val == "SKIP")
            {
                e.CellStyle.BackColor = Color.FromArgb(150, 150, 150);
                e.CellStyle.ForeColor = Color.White;
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

        private void SetRunning(bool running)
        {
            btnRun.Enabled    = !running;
            btnBrowse.Enabled = !running;
            btnClear.Enabled  = !running;
            btnExport.Enabled = !running && _results.Count > 0;
        }

        // ── UI Factory Methods ───────────────────────────────────────────────
        private static Panel MakePanel(DockStyle dock, int height, Color bg)
        {
            return new Panel { Dock = dock, Height = height, BackColor = bg };
        }

        private static Label MakeLabel(string text, Font font, Color fore,
                                       DockStyle dock, ContentAlignment align)
        {
            return new Label
            {
                Text      = text,
                Font      = font,
                ForeColor = fore,
                Dock      = dock,
                TextAlign = align
            };
        }

        private static Button MakeButton(string text, Color bg, int x)
        {
            return new Button
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
        }

        private static void AddColumn(DataGridView dgv, string name, string header,
                                      int fillWeight, bool centered = false, bool bold = false)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name       = name,
                HeaderText = header,
                FillWeight = fillWeight,
                SortMode   = DataGridViewColumnSortMode.Automatic
            };
            if (centered)
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            if (bold)
                col.DefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgv.Columns.Add(col);
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
