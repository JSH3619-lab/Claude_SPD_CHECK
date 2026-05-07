using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SPD_Checker.Logic;

namespace SPD_Checker.Forms
{
    public class AutoGenForm : Form
    {
        // ── State ────────────────────────────────────────────────────────────
        private string _folderPath;

        // ── UI ───────────────────────────────────────────────────────────────
        private Label    _lblFolder;
        private TextBox  _txtPartNo;
        private Panel    _pnlResult;
        private Label    _lblResultIcon;
        private Label    _lblResultTitle;
        private Label    _lblResultDetail;
        private ListView _lvHistory;

        public AppMode? NextMode { get; private set; }

        // ── 설정 파일 (앱 실행 경로에 저장) ─────────────────────────────────
        private static readonly string _settingsFile =
            Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath) ?? "",
                "autogen_settings.cfg");

        private static string LoadSavedFolder()
        {
            try
            {
                if (!File.Exists(_settingsFile)) return null;
                string path = File.ReadAllText(_settingsFile, System.Text.Encoding.UTF8).Trim();
                return Directory.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        private static void SaveFolder(string path)
        {
            try { File.WriteAllText(_settingsFile, path, System.Text.Encoding.UTF8); }
            catch { }
        }

        public AutoGenForm()
        {
            _folderPath = LoadSavedFolder();
            BuildUI();
            UpdateFolderUi();
        }

        // ── UI Construction ──────────────────────────────────────────────────
        private void BuildUI()
        {
            Text          = "DDR5 SPD Studio — Auto-Gen  v1.0";
            Size          = new Size(900, 700);
            MinimumSize   = new Size(720, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 10F);
            BackColor     = Color.FromArgb(245, 246, 248);

            Controls.Add(BuildHistory());     // Bottom — Fill 영역과 충돌 없게 먼저
            Controls.Add(BuildBottomBar());
            Controls.Add(BuildResultArea());
            Controls.Add(BuildInputArea());
            Controls.Add(BuildFolderArea());
            Controls.Add(BuildHeader());
        }

        private Panel BuildHeader()
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(170, 70, 20) };
            p.Controls.Add(new Label
            {
                Text      = "🤖   Auto-Gen Mode",
                Font      = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(20, 0, 0, 0)
            });
            var btnMode = ModeDropdown.Create(AppMode.AutoGen,
                m => { NextMode = m; Close(); });
            p.Controls.Add(btnMode);
            return p;
        }

        private Panel BuildFolderArea()
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(237, 239, 243) };
            p.Padding = new Padding(20, 12, 20, 12);

            _lblFolder = new Label
            {
                Dock      = DockStyle.Fill,
                Font      = new Font("Consolas", 10F),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(70, 70, 70)
            };
            var btn = new Button
            {
                Text      = "📁  폴더 선택",
                Dock      = DockStyle.Right,
                Width     = 140,
                Font      = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(65, 125, 190),
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => SelectFolder();
            p.Controls.Add(_lblFolder);
            p.Controls.Add(btn);
            return p;
        }

        private Panel BuildInputArea()
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Color.FromArgb(245, 246, 248) };
            p.Padding = new Padding(20, 14, 20, 14);

            var lbl = new Label
            {
                Text      = "Part Number / QR 스캔",
                Dock      = DockStyle.Top,
                Height    = 22,
                Font      = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 40, 40),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _txtPartNo = new TextBox
            {
                Dock         = DockStyle.Fill,
                Font         = new Font("Consolas", 14F, FontStyle.Bold),
                BorderStyle  = BorderStyle.FixedSingle,
                BackColor    = Color.White
            };
            _txtPartNo.KeyDown += OnPartNoKeyDown;

            // Dock fill must come AFTER top label
            p.Controls.Add(_txtPartNo);
            p.Controls.Add(lbl);
            return p;
        }

        private Panel BuildResultArea()
        {
            _pnlResult = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 130,
                BackColor = Color.FromArgb(225, 230, 235)
            };
            _pnlResult.Padding = new Padding(20, 14, 20, 14);

            _lblResultIcon = new Label
            {
                Dock      = DockStyle.Left,
                Width     = 80,
                Font      = new Font("Segoe UI", 38F, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                TextAlign = ContentAlignment.MiddleCenter,
                Text      = "—"
            };
            var pnlText = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            _lblResultTitle = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 36,
                Font      = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 60, 60),
                TextAlign = ContentAlignment.MiddleLeft,
                Text      = "대기 중"
            };
            _lblResultDetail = new Label
            {
                Dock      = DockStyle.Fill,
                Font      = new Font("Consolas", 9.5F),
                ForeColor = Color.FromArgb(80, 80, 80),
                TextAlign = ContentAlignment.TopLeft,
                Text      = "폴더를 선택하고 Part Number를 입력하세요."
            };
            pnlText.Controls.Add(_lblResultDetail);
            pnlText.Controls.Add(_lblResultTitle);

            _pnlResult.Controls.Add(pnlText);
            _pnlResult.Controls.Add(_lblResultIcon);
            return _pnlResult;
        }

        private Panel BuildHistory()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20, 8, 20, 8) };

            var lbl = new Label
            {
                Text      = "처리 이력 (이번 세션)",
                Dock      = DockStyle.Top,
                Height    = 26,
                Font      = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 40, 40),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _lvHistory = new ListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                GridLines     = true,
                Font          = new Font("Consolas", 9F),
                BorderStyle   = BorderStyle.FixedSingle
            };
            _lvHistory.Columns.Add("시각",       80);
            _lvHistory.Columns.Add("결과",       80);
            _lvHistory.Columns.Add("Part No",   240);
            _lvHistory.Columns.Add("템플릿",     220);
            _lvHistory.Columns.Add("비고",       260);

            p.Controls.Add(_lvHistory);
            p.Controls.Add(lbl);
            return p;
        }

        private Panel BuildBottomBar()
        {
            var p = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(28, 57, 95) };
            p.Controls.Add(new Label
            {
                Text      = "  Templates 폴더에서 매칭되는 골든 샘플만 자동 생성. 실패 시 엔지니어 호출.",
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 8.5F),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft
            });
            return p;
        }

        // ── 동작 ─────────────────────────────────────────────────────────────
        private void SelectFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description         = "SPD 파일을 저장할 폴더 선택 (Templates 하위 폴더 포함)";
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _folderPath = dlg.SelectedPath;
                SaveFolder(_folderPath);
                UpdateFolderUi();
            }
        }

        private void UpdateFolderUi()
        {
            if (string.IsNullOrEmpty(_folderPath))
            {
                _lblFolder.Text      = "  ⚠  폴더 미선택";
                _lblFolder.ForeColor = Color.FromArgb(170, 70, 70);
                _txtPartNo.Enabled   = false;
                return;
            }

            string tpl = Path.Combine(_folderPath, SpdAutoGen.TEMPLATES_DIR);
            bool tplOk = Directory.Exists(tpl);
            _lblFolder.Text = tplOk
                ? $"  📁 {_folderPath}     (Templates ✓)"
                : $"  📁 {_folderPath}     ⚠ Templates 하위 폴더 없음";
            _lblFolder.ForeColor = tplOk
                ? Color.FromArgb(40, 100, 60)
                : Color.FromArgb(170, 70, 70);
            _txtPartNo.Enabled = true;
            _txtPartNo.Focus();
        }

        private void OnPartNoKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;

            string partNo = _txtPartNo.Text.Trim();
            if (string.IsNullOrEmpty(partNo)) return;

            var result = SpdAutoGen.GenerateFromTemplate(partNo, _folderPath);
            ShowResult(result);
            AddHistory(result);

            _txtPartNo.Clear();
            _txtPartNo.Focus();
        }

        private void ShowResult(AutoGenResult r)
        {
            if (r.Success && r.AlreadyExists)
            {
                _pnlResult.BackColor    = Color.FromArgb(255, 248, 220);
                _lblResultIcon.Text     = "ℹ";
                _lblResultIcon.ForeColor = Color.FromArgb(180, 140, 30);
                _lblResultTitle.Text    = "기존 파일 사용";
                _lblResultTitle.ForeColor = Color.FromArgb(120, 90, 20);
                _lblResultDetail.Text   = $"{r.PartNo}.sp5 — 이미 폴더에 존재 (생성 스킵)";
                _lblResultDetail.ForeColor = Color.FromArgb(100, 80, 30);
            }
            else if (r.Success)
            {
                _pnlResult.BackColor      = Color.FromArgb(220, 245, 220);
                _lblResultIcon.Text       = "✓";
                _lblResultIcon.ForeColor  = Color.FromArgb(34, 130, 50);
                _lblResultTitle.Text      = "생성 완료";
                _lblResultTitle.ForeColor = Color.FromArgb(20, 90, 35);
                _lblResultDetail.Text     =
                    $"Part No   : {r.PartNo}\r\n" +
                    $"템플릿    : {r.TemplateUsed}\r\n" +
                    $"저장 경로 : {r.OutputPath}";
                _lblResultDetail.ForeColor = Color.FromArgb(40, 80, 50);
            }
            else
            {
                _pnlResult.BackColor      = Color.FromArgb(250, 215, 215);
                _lblResultIcon.Text       = "✗";
                _lblResultIcon.ForeColor  = Color.FromArgb(200, 50, 60);
                _lblResultTitle.Text      = "엔지니어 호출 필요";
                _lblResultTitle.ForeColor = Color.FromArgb(150, 30, 40);
                _lblResultDetail.Text     =
                    $"Part No : {r.PartNo}\r\n" +
                    $"사유    : {r.ErrorMessage}";
                _lblResultDetail.ForeColor = Color.FromArgb(120, 30, 40);
            }
        }

        private void AddHistory(AutoGenResult r)
        {
            string status = r.Success ? (r.AlreadyExists ? "EXISTS" : "OK") : "FAIL";
            string note   = r.Success
                ? (r.AlreadyExists ? "기존 사용" : "신규 생성")
                : (r.ErrorMessage ?? "");

            var item = new ListViewItem(DateTime.Now.ToString("HH:mm:ss"));
            item.SubItems.Add(status);
            item.SubItems.Add(r.PartNo ?? "");
            item.SubItems.Add(r.TemplateUsed ?? "");
            item.SubItems.Add(note);

            item.ForeColor = r.Success
                ? (r.AlreadyExists ? Color.FromArgb(150, 110, 20) : Color.FromArgb(20, 100, 40))
                : Color.FromArgb(170, 40, 50);

            _lvHistory.Items.Insert(0, item);   // 최신이 위에
        }
    }
}
