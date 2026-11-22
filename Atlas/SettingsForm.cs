using System;
using System.Drawing;
using System.Windows.Forms;

namespace Atlas
{
    /// <summary>Окно настроек приложения.</summary>
    internal class SettingsForm : Form
    {
        private readonly Action onSave;
        private readonly Action onCheckUpdates;
        private readonly Action<string, string> onImport; // тип, путь

        public SettingsForm(Form1.AppSettings settings, Action<Form, string> darkHeader, Action onSave, Action onCheckUpdates,
            Action<string, string> onImport)
        {
            this.onSave = onSave;
            this.onCheckUpdates = onCheckUpdates;
            this.onImport = onImport;
            Text = "Atlas — Настройки";
            Width = 650; Height = 580;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(3, 7, 17);
            ForeColor = Color.FromArgb(226, 236, 252);
            Font = new Font("Segoe UI", 9.5F);
            ShowIcon = false;

            int labelW = 180, fieldX = 200, fieldW = 380;

            Label lTheme = new Label { Text = "Тема:", Location = new Point(24, 24), Width = labelW, AutoSize = false };
            ComboBox cbTheme = CreateDarkCombo(fieldX, 20, fieldW, new[] { "Тёмная", "Светлая" },
                settings.Theme == "light" ? 1 : 0);

            Label lSearch = new Label { Text = "Поисковик:", Location = new Point(24, 64), Width = labelW, AutoSize = false };
            var engines = new[] { "google", "duckduckgo", "bing", "yandex" };
            ComboBox cbSearch = CreateDarkCombo(fieldX, 60, fieldW, new[] { "Google", "DuckDuckGo", "Bing", "Yandex" },
                Math.Max(0, Array.IndexOf(engines, settings.SearchEngine?.ToLowerInvariant() ?? "google")));

            Label lFolder = new Label { Text = "Папка загрузок:", Location = new Point(24, 104), Width = labelW, AutoSize = false };
            TextBox tbFolder = new TextBox
            {
                Location = new Point(fieldX, 100), Width = 330,
                Text = settings.DownloadFolder,
                BackColor = Color.FromArgb(10, 22, 42), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Button btnBrowse = new Button
            {
                Text = "...", Location = new Point(fieldX + 338, 100), Size = new Size(42, 23),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(10, 22, 42), ForeColor = Color.White
            };
            btnBrowse.FlatAppearance.BorderSize = 0;
            btnBrowse.Click += (s, e) =>
            {
                using var fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog() == DialogResult.OK) tbFolder.Text = fbd.SelectedPath;
            };

            CheckBox cbAutoUpdate = new CheckBox
            {
                Text = "Проверять обновления при запуске",
                Location = new Point(24, 144), AutoSize = true,
                Checked = settings.AutoUpdate
            };
            CheckBox cbAutoInstall = new CheckBox
            {
                Text = "Скачивать и запускать установщик автоматически",
                Location = new Point(24, 170), AutoSize = true,
                Checked = settings.AutoInstall
            };
            CheckBox cbSingleProcess = new CheckBox
            {
                Text = "Экономичный режим (один процесс WebView2 — может быть нестабилен)",
                Location = new Point(24, 192), AutoSize = true,
                Checked = settings.SingleProcessMode
            };

            Label lRepo = new Label { Text = "Репозиторий обновлений:", Location = new Point(24, 224), Width = labelW, AutoSize = false };
            Label tbRepo = new Label
            {
                Location = new Point(fieldX, 220), Width = fieldW, Height = 26,
                Text = "werkot20000009853-bot/Atlas-Browser",
                ForeColor = Color.FromArgb(56, 139, 253),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Label repoHint = new Label
            {
                Text = "Официальный репозиторий Atlas (изменению не подлежит)",
                Location = new Point(fieldX, 246), AutoSize = true,
                ForeColor = Color.FromArgb(139, 164, 196), Font = new Font("Segoe UI", 8.5F)
            };

            Button btnCheckNow = MakeButton("Проверить сейчас", fieldX, 274, 160);
            btnCheckNow.Click += (s, e) => onCheckUpdates();

            Button btnImport = MakeButton("Импорт данных из браузера", 214, 340, 240);
            btnImport.BackColor = Color.FromArgb(17, 34, 60);
            btnImport.Click += (s, e) =>
            {
                var menu = new ContextMenuStrip();
                menu.BackColor = Color.FromArgb(10, 22, 42);
                menu.ForeColor = Color.White;
                menu.Items.Add("Закладки из HTML (Chrome/Firefox/Edge)", null, (_, __) =>
                {
                    var ofd = new OpenFileDialog { Filter = "HTML files|*.html;*.htm", Title = "Экспорт закладок" };
                    if (ofd.ShowDialog() == DialogResult.OK) onImport("bookmarks_html", ofd.FileName);
                });
                menu.Items.Add("Закладки из Chrome/Edge (Bookmarks JSON)", null, (_, __) =>
                {
                    var ofd = new OpenFileDialog { Filter = "Bookmarks|Bookmarks;*.json", Title = "Файл Bookmarks" };
                    if (ofd.ShowDialog() == DialogResult.OK) onImport("bookmarks_json", ofd.FileName);
                });
                menu.Items.Add("История из Chrome/Edge (SQLite)", null, (_, __) =>
                {
                    var ofd = new OpenFileDialog { Filter = "History DB|History;*.db;*.sqlite", Title = "Файл History" };
                    if (ofd.ShowDialog() == DialogResult.OK) onImport("history_sqlite", ofd.FileName);
                });
                menu.Show(Cursor.Position);
            };

            Button btnSave = MakeButton("Сохранить", 24, 390, 180);
            btnSave.BackColor = Color.FromArgb(56, 139, 253);
            btnSave.DialogResult = DialogResult.OK;

            Controls.AddRange(new Control[] { lTheme, cbTheme, lSearch, cbSearch, lFolder, tbFolder, btnBrowse,
                cbAutoUpdate, cbAutoInstall, cbSingleProcess, lRepo, tbRepo, repoHint, btnCheckNow, btnImport, btnSave });
            darkHeader(this, "Atlas — Настройки");

            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.OK) return;
                settings.Theme = cbTheme.SelectedIndex == 1 ? "light" : "dark";
                settings.SearchEngine = engines[cbSearch.SelectedIndex];
                settings.DownloadFolder = tbFolder.Text.Trim();
                settings.AutoUpdate = cbAutoUpdate.Checked;
                settings.AutoInstall = cbAutoInstall.Checked;
                bool oldSingle = settings.SingleProcessMode;
                settings.SingleProcessMode = cbSingleProcess.Checked;
                onSave();
                if (oldSingle != settings.SingleProcessMode)
                {
                    MessageBox.Show("Режим процессов изменён.\nПерезапустите Atlas, чтобы применить.",
                        "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
        }

        private static Button MakeButton(string text, int left, int top, int width)
        {
            var b = new Button
            {
                Text = text, Location = new Point(left, top), Size = new Size(width, 30),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(28, 70, 128),
                ForeColor = Color.White, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private static ComboBox CreateDarkCombo(int left, int top, int width, string[] items, int selected)
        {
            var cb = new ComboBox
            {
                Location = new Point(left, top), Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24,
                BackColor = Color.FromArgb(10, 22, 42), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F), Cursor = Cursors.Hand
            };
            cb.Items.AddRange(items);
            if (selected >= 0 && selected < items.Length) cb.SelectedIndex = selected;
            cb.DrawItem += (s, e) =>
            {
                e.DrawBackground();
                string text = e.Index >= 0 ? cb.Items[e.Index].ToString() : "";
                Color c = (e.State & DrawItemState.Selected) != 0 ? Color.White : Color.FromArgb(226, 236, 252);
                TextRenderer.DrawText(e.Graphics, text, cb.Font, e.Bounds, c,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };
            return cb;
        }
    }
}
