using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RP2040AutoFlash
{
    class MainForm : Form
    {
        const int ScanIntervalMs = 1000;
        // 刺した直後はドライブが落ち着いていないことがあるので、自動で書く前に少し待つ
        const int SettleDelayMs = 800;
        // 書いたボードがすぐ書き込みモードで戻ってきたとき（UF2 が起動しなかったときなど）に、
        // 自動書き込みを繰り返さないための猶予
        const int ReflashGuardMs = 4000;
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "RP2040AutoFlash";

        readonly AppSettings settings;
        readonly Queue<BootDrive> queue = new Queue<BootDrive>();
        readonly Dictionary<string, DateTime> lastFlashed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        List<BootDrive> boards = new List<BootDrive>();
        bool startHidden;
        bool exiting;
        bool scanning;
        bool flashing;
        bool initialScanDone;
        bool loading;
        bool trayHintShown;
        string uf2Stamp;   // 表示中の UF2 情報が古くなったかを見るための「パス|更新日時|大きさ」

        TextBox pathBox;
        Label uf2InfoLabel;
        RadioButton autoRadio;
        RadioButton manualRadio;
        Label boardsLabel;
        Button flashButton;
        CheckBox rp2350Check;
        CheckBox notifyCheck;
        CheckBox soundCheck;
        CheckBox startupCheck;
        TextBox logBox;
        NotifyIcon tray;
        ToolStripMenuItem flashMenuItem;
        ToolStripMenuItem autoMenuItem;
        ToolStripMenuItem manualMenuItem;
        Icon autoIcon;
        Icon manualIcon;
        Icon busyIcon;
        Timer scanTimer;

        public MainForm(bool startHidden)
        {
            settings = AppSettings.Load();
            // UF2 が未設定のうちは、スタートアップからでも画面を出す
            this.startHidden = startHidden && settings.Uf2Path.Length > 0;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Text = "RP2040 AutoFlash";
            ClientSize = new Size(560, 680);
            MinimumSize = new Size(480, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            BuildLayout();
            BuildTray();
            LoadSettingsToUi();
            EnableFileDrop(this);

            Log("起動しました。BOOT ボタンを押しながら RP2040 を USB に刺してください。");
            RefreshUf2Info(true);
            UpdateUi();

            scanTimer = new Timer { Interval = ScanIntervalMs };
            scanTimer.Tick += OnScanTick;
            scanTimer.Start();
            OnScanTick(null, EventArgs.Empty);
        }

        void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            pathBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            pathBox.TextChanged += OnPathChanged;
            var browseButton = NewButton("参照...");
            browseButton.Click += OnBrowse;
            uf2InfoLabel = new Label { AutoSize = true, Margin = new Padding(3, 4, 3, 2) };
            var uf2Table = NewTable();
            uf2Table.Controls.Add(pathBox, 0, 0);
            uf2Table.Controls.Add(browseButton, 1, 0);
            uf2Table.Controls.Add(uf2InfoLabel, 0, 1);
            uf2Table.SetColumnSpan(uf2InfoLabel, 2);
            root.Controls.Add(NewGroup("書き込む UF2 ファイル", uf2Table));

            autoRadio = new RadioButton { Text = "自動：BOOT を押しながら刺すと、すぐに書き込む", AutoSize = true };
            manualRadio = new RadioButton { Text = "手動：刺したあと「書き込む」ボタンを押して書き込む", AutoSize = true };
            autoRadio.CheckedChanged += OnModeChanged;
            manualRadio.CheckedChanged += OnModeChanged;
            root.Controls.Add(NewGroup("モード", NewStack(autoRadio, manualRadio)));

            boardsLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            flashButton = NewButton("書き込む");
            flashButton.MinimumSize = new Size(130, 40);
            flashButton.Font = new Font(Font.FontFamily, Font.SizeInPoints + 1.5f, FontStyle.Bold);
            flashButton.Anchor = AnchorStyles.Right;
            flashButton.Click += (s, e) => FlashNow();
            var boardTable = NewTable();
            boardTable.Controls.Add(boardsLabel, 0, 0);
            boardTable.Controls.Add(flashButton, 1, 0);
            root.Controls.Add(NewGroup("接続中のボード", boardTable));

            rp2350Check = new CheckBox { Text = "RP2350（Pico 2）にも書き込む", AutoSize = true };
            notifyCheck = new CheckBox { Text = "書き込みの結果を通知する", AutoSize = true };
            soundCheck = new CheckBox { Text = "書き込みが終わったら音を鳴らす", AutoSize = true };
            startupCheck = new CheckBox { Text = "Windows の起動時に通知領域で起動する", AutoSize = true };
            rp2350Check.CheckedChanged += OnOptionChanged;
            notifyCheck.CheckedChanged += OnOptionChanged;
            soundCheck.CheckedChanged += OnOptionChanged;
            startupCheck.CheckedChanged += OnStartupChanged;
            root.Controls.Add(NewGroup("その他", NewStack(rp2350Check, notifyCheck, soundCheck, startupCheck)));

            root.Controls.Add(new Label { Text = "履歴", AutoSize = true, Margin = new Padding(3, 8, 3, 2) });
            logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window };
            root.Controls.Add(logBox);

            var exitButton = NewButton("終了");
            exitButton.Click += (s, e) => ExitApp();
            var hideButton = NewButton("閉じる（通知領域で待機）");
            hideButton.Click += (s, e) => Close();
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            buttons.Controls.Add(exitButton);
            buttons.Controls.Add(hideButton);
            root.Controls.Add(buttons);

            // 履歴の欄だけが伸び縮みする
            for (int i = 0; i < root.Controls.Count; i++)
                root.RowStyles.Add(root.Controls[i] == logBox ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));

            Controls.Add(root);
        }

        static TableLayoutPanel NewTable()
        {
            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return table;
        }

        static FlowLayoutPanel NewStack(params Control[] controls)
        {
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
            stack.Controls.AddRange(controls);
            return stack;
        }

        static GroupBox NewGroup(string text, Control content)
        {
            var group = new GroupBox { Text = text, Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 6) };
            group.Controls.Add(content);
            return group;
        }

        static Button NewButton(string text)
        {
            return new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        }

        void BuildTray()
        {
            int size = SystemInformation.SmallIconSize.Width;
            autoIcon = IconFactory.Create(IconFactory.AutoColor, size);
            manualIcon = IconFactory.Create(IconFactory.ManualColor, size);
            busyIcon = IconFactory.Create(IconFactory.BusyColor, size);

            var menu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("画面を開く", null, (s, e) => ShowFromTray());
            openItem.Font = new Font(menu.Font, FontStyle.Bold);
            flashMenuItem = new ToolStripMenuItem("今すぐ書き込む", null, (s, e) => FlashNow());
            autoMenuItem = new ToolStripMenuItem("自動モード", null, (s, e) => SetMode(FlashMode.Auto));
            manualMenuItem = new ToolStripMenuItem("手動モード", null, (s, e) => SetMode(FlashMode.Manual));
            menu.Items.AddRange(new ToolStripItem[]
            {
                openItem,
                new ToolStripSeparator(),
                flashMenuItem,
                autoMenuItem,
                manualMenuItem,
                new ToolStripSeparator(),
                new ToolStripMenuItem("終了", null, (s, e) => ExitApp()),
            });

            tray = new NotifyIcon { ContextMenuStrip = menu, Icon = autoIcon, Text = "RP2040 AutoFlash", Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
            tray.BalloonTipClicked += (s, e) => ShowFromTray();
        }

        void LoadSettingsToUi()
        {
            loading = true;
            pathBox.Text = settings.Uf2Path;
            autoRadio.Checked = settings.Mode == FlashMode.Auto;
            manualRadio.Checked = settings.Mode == FlashMode.Manual;
            rp2350Check.Checked = settings.IncludeRP2350;
            notifyCheck.Checked = settings.Notify;
            soundCheck.Checked = settings.Sound;
            startupCheck.Checked = IsStartupRegistered();
            loading = false;
        }

        // ---- 設定の変更 ----

        void OnPathChanged(object sender, EventArgs e)
        {
            if (loading) return;
            settings.Uf2Path = pathBox.Text.Trim().Trim('"');
            SaveSettings();
            RefreshUf2Info(true);
            UpdateUi();
        }

        void OnBrowse(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Filter = "UF2 ファイル (*.uf2)|*.uf2|すべてのファイル (*.*)|*.*", Title = "書き込む UF2 ファイルを選ぶ" })
            {
                try
                {
                    if (settings.Uf2Path.Length > 0)
                    {
                        string dir = Path.GetDirectoryName(settings.Uf2Path);
                        if (Directory.Exists(dir)) dialog.InitialDirectory = dir;
                        dialog.FileName = Path.GetFileName(settings.Uf2Path);
                    }
                }
                catch (ArgumentException) { }
                if (dialog.ShowDialog(this) == DialogResult.OK) pathBox.Text = dialog.FileName;
            }
        }

        void OnModeChanged(object sender, EventArgs e)
        {
            if (loading || !((RadioButton)sender).Checked) return;
            SetMode(autoRadio.Checked ? FlashMode.Auto : FlashMode.Manual);
        }

        void SetMode(FlashMode mode)
        {
            loading = true;
            autoRadio.Checked = mode == FlashMode.Auto;
            manualRadio.Checked = mode == FlashMode.Manual;
            loading = false;
            if (settings.Mode == mode) return;

            settings.Mode = mode;
            SaveSettings();
            Log(mode == FlashMode.Auto ? "自動モードにしました（刺すとすぐ書き込みます）" : "手動モードにしました（「書き込む」で書き込みます）");
            UpdateUi();
        }

        void OnOptionChanged(object sender, EventArgs e)
        {
            if (loading) return;
            bool targetsChanged = settings.IncludeRP2350 != rp2350Check.Checked;
            settings.IncludeRP2350 = rp2350Check.Checked;
            settings.Notify = notifyCheck.Checked;
            settings.Sound = soundCheck.Checked;
            SaveSettings();
            // 対象を広げた直後に、刺さったままのボードへ自動で書かないよう、次の検出を起動直後と同じ扱いにする
            if (targetsChanged) initialScanDone = false;
            RefreshUf2Info(true);
        }

        void OnStartupChanged(object sender, EventArgs e)
        {
            if (loading) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (startupCheck.Checked) key.SetValue(RunValueName, StartupCommand);
                    else key.DeleteValue(RunValueName, false);
                }
                Log(startupCheck.Checked ? "Windows の起動時に起動するよう登録しました" : "Windows の起動時の起動をやめました");
            }
            catch (Exception ex)
            {
                Log("起動時の登録を変えられませんでした：" + ex.Message);
                loading = true;
                startupCheck.Checked = IsStartupRegistered();
                loading = false;
            }
        }

        static string StartupCommand
        {
            get { return "\"" + Application.ExecutablePath + "\" --tray"; }
        }

        // exe を動かしたあとの古い登録は、登録していないものとして見せる
        static bool IsStartupRegistered()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                var value = key == null ? null : key.GetValue(RunValueName) as string;
                return string.Equals(value, StartupCommand, StringComparison.OrdinalIgnoreCase);
            }
        }

        void SaveSettings()
        {
            try { settings.Save(); }
            catch (Exception ex) { Log("設定を保存できませんでした：" + ex.Message); }
        }

        void EnableFileDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += (s, e) => e.Effect = DroppedUf2(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
            control.DragDrop += (s, e) =>
            {
                string path = DroppedUf2(e);
                if (path != null) pathBox.Text = path;
            };
            foreach (Control child in control.Controls) EnableFileDrop(child);
        }

        static string DroppedUf2(DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length != 1) return null;
            return files[0].EndsWith(".uf2", StringComparison.OrdinalIgnoreCase) ? files[0] : null;
        }

        // ---- ボードの検出 ----

        async void OnScanTick(object sender, EventArgs e)
        {
            if (scanning) return;
            scanning = true;
            try
            {
                bool includeRP2350 = settings.IncludeRP2350;
                var found = await Task.Run(() => BootDrive.Scan(includeRP2350));
                var arrived = found.Where(f => !boards.Any(b => SameRoot(b, f))).ToList();
                bool changed = arrived.Count > 0 || found.Count != boards.Count;
                boards = found;

                if (!initialScanDone)
                {
                    // 起動したときに刺さっていたボードには自動で書かない
                    initialScanDone = true;
                    if (found.Count > 0)
                        Log("書き込みモードのボードがつながっています（" + string.Join("、", found.Select(b => b.Name)) + "）。「書き込む」で書き込めます");
                }
                else if (arrived.Count > 0)
                {
                    OnBoardsArrived(arrived);
                }

                RefreshUf2Info(false);
                if (changed) UpdateUi();
            }
            finally
            {
                scanning = false;
            }
        }

        void OnBoardsArrived(List<BootDrive> arrived)
        {
            foreach (var board in arrived) Log(board.Name + " を見つけました");

            if (settings.Mode == FlashMode.Manual)
            {
                Notify(arrived[0].Name + " を見つけました", "「書き込む」を押すと書き込みます", ToolTipIcon.Info);
                ShowFromTray();
                return;
            }

            var targets = new List<BootDrive>();
            foreach (var board in arrived)
            {
                DateTime flashedAt;
                if (lastFlashed.TryGetValue(board.Root, out flashedAt) && (DateTime.Now - flashedAt).TotalMilliseconds < ReflashGuardMs)
                {
                    const string message = "書き込んだ直後に書き込みモードへ戻ったので、自動では書き込みません。UF2 が起動しなかった可能性があります";
                    Log("注意：" + board.Name + " — " + message);
                    Notify("書き込みモードに戻りました", message, ToolTipIcon.Warning);
                    continue;
                }
                targets.Add(board);
            }
            if (targets.Count > 0) AutoFlashAfterSettle(targets);
        }

        async void AutoFlashAfterSettle(List<BootDrive> targets)
        {
            await Task.Delay(SettleDelayMs);
            if (settings.Mode != FlashMode.Auto) return;   // 待つ間に手動へ切り替えられた
            Enqueue(targets);
        }

        // ---- 書き込み ----

        void FlashNow()
        {
            if (boards.Count == 0)
            {
                Log("書き込みモードのボードが見つかりません");
                return;
            }
            Enqueue(boards);
        }

        void Enqueue(IEnumerable<BootDrive> targets)
        {
            foreach (var board in targets)
                if (!queue.Any(q => SameRoot(q, board))) queue.Enqueue(board);
            if (!flashing) ProcessQueue();
        }

        async void ProcessQueue()
        {
            flashing = true;
            UpdateUi();
            while (queue.Count > 0)
            {
                var board = queue.Dequeue();
                if (!board.StillPresent)
                {
                    Log(board.Name + " が見えなくなったので、書き込みをやめました");
                    continue;
                }
                await FlashOne(board);
            }
            flashing = false;
            UpdateUi();
        }

        async Task FlashOne(BootDrive board)
        {
            string path = settings.Uf2Path;
            if (path.Length == 0 || !File.Exists(path))
            {
                Fail(board, path.Length == 0 ? "UF2 ファイルが設定されていません" : "UF2 ファイルが見つかりません");
                return;
            }

            string fileName = Path.GetFileName(path);
            Log(board.Name + " に " + fileName + " を書き込んでいます…");
            try
            {
                // 書くたびに読み直すので、ビルドし直した UF2 がそのまま使われる
                var outcome = await Task.Run(() =>
                {
                    var image = Uf2Image.Load(path);
                    if (!image.SupportsChip(board.Chip))
                        throw new InvalidDataException("この UF2 は " + image.FamilyText + " 用なので、" + board.Chip + " には書き込めません");
                    return Flasher.Flash(image, fileName, board);
                });
                lastFlashed[board.Root] = DateTime.Now;

                if (outcome == FlashOutcome.Rebooted)
                {
                    Log("完了：" + board.Name + " に書き込み、ボードが再起動しました");
                    Notify("書き込み完了", fileName + " を " + board.Name + " に書き込みました", ToolTipIcon.Info);
                    PlaySound(SystemSounds.Asterisk);
                }
                else
                {
                    const string message = "書き込みましたが、ボードが再起動しません。UF2 の中身を確かめてください";
                    Log("注意：" + board.Name + " — " + message);
                    Notify("ボードが再起動しません", message, ToolTipIcon.Warning);
                    PlaySound(SystemSounds.Exclamation);
                }
            }
            catch (Exception ex)
            {
                Fail(board, ex.Message);
            }
        }

        void Fail(BootDrive board, string message)
        {
            Log("失敗：" + board.Name + " — " + message);
            Notify("書き込めませんでした", message, ToolTipIcon.Error);
            PlaySound(SystemSounds.Hand);
            ShowFromTray();
        }

        // ---- 表示 ----

        void RefreshUf2Info(bool force)
        {
            string path = settings.Uf2Path;
            FileInfo file = null;
            try
            {
                if (path.Length > 0) file = new FileInfo(path);
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }

            bool exists = file != null && file.Exists;
            string stamp = path + "|" + (exists ? file.LastWriteTimeUtc.Ticks + "|" + file.Length : "-") + "|" + settings.IncludeRP2350;
            if (!force && stamp == uf2Stamp) return;
            uf2Stamp = stamp;

            string text;
            Color color = Color.Firebrick;
            if (path.Length == 0)
            {
                text = "UF2 ファイルを選んでください（この画面へドラッグ＆ドロップしても指定できます）";
                color = SystemColors.GrayText;
            }
            else if (!exists)
            {
                text = "ファイルが見つかりません";
            }
            else
            {
                try
                {
                    var image = Uf2Image.Load(path);
                    text = string.Format("{0} 用 ・ {1:N0} KB ・ 更新 {2:M/d HH:mm}", image.FamilyText, (image.PayloadBytes + 1023) / 1024, file.LastWriteTime);
                    bool usable = image.SupportsChip(Chip.RP2040) || (settings.IncludeRP2350 && image.SupportsChip(Chip.RP2350));
                    if (usable)
                    {
                        color = SystemColors.ControlText;
                    }
                    else
                    {
                        text += "（このままでは書き込めません）";
                        color = Color.DarkOrange;
                    }
                }
                catch (Exception ex)
                {
                    text = ex.Message;
                }
            }
            uf2InfoLabel.Text = text;
            uf2InfoLabel.ForeColor = color;
        }

        void UpdateUi()
        {
            if (boards.Count == 0)
            {
                boardsLabel.Text = "つながっていません\nBOOT ボタンを押しながら USB に刺してください";
                boardsLabel.ForeColor = SystemColors.GrayText;
            }
            else
            {
                boardsLabel.Text = string.Join("\n", boards.Select(b => b.Name + " の書き込みモード"));
                boardsLabel.ForeColor = SystemColors.ControlText;
            }

            bool canFlash = boards.Count > 0 && !flashing;
            flashButton.Enabled = canFlash;
            flashButton.Text = flashing ? "書き込み中…" : "書き込む";
            flashMenuItem.Enabled = canFlash;
            autoMenuItem.Checked = settings.Mode == FlashMode.Auto;
            manualMenuItem.Checked = settings.Mode == FlashMode.Manual;

            var icon = flashing ? busyIcon : settings.Mode == FlashMode.Auto ? autoIcon : manualIcon;
            string state = flashing ? "書き込み中" : settings.Mode == FlashMode.Auto ? "自動モード" : "手動モード";
            if (tray.Icon != icon) tray.Icon = icon;
            tray.Text = "RP2040 AutoFlash（" + state + "）";
        }

        void Log(string message)
        {
            if (logBox.TextLength > 100000) logBox.Text = logBox.Text.Substring(50000);
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        void Notify(string title, string text, ToolTipIcon icon)
        {
            if (settings.Notify) tray.ShowBalloonTip(5000, title, text, icon);
        }

        void PlaySound(SystemSound sound)
        {
            if (settings.Sound) sound.Play();
        }

        static bool SameRoot(BootDrive a, BootDrive b)
        {
            return string.Equals(a.Root, b.Root, StringComparison.OrdinalIgnoreCase);
        }

        // ---- 画面と終了 ----

        public void ShowFromTray()
        {
            startHidden = false;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // パス欄が全選択で出ないよう、カーソルを末尾（ファイル名の側）に置く
            pathBox.Select(pathBox.TextLength, 0);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                // 画面は出さないが、別に起動された exe からの呼び出しを受けるためにハンドルは作っておく
                startHidden = false;
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!trayHintShown)
                {
                    trayHintShown = true;
                    tray.ShowBalloonTip(3000, "RP2040 AutoFlash", "通知領域で待機しています。終了するときは右クリックメニューから。", ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            scanTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }

        void ExitApp()
        {
            if (flashing && MessageBox.Show(this, "書き込み中です。終了しますか？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            exiting = true;
            Close();
        }
    }
}
