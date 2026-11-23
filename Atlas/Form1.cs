using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Atlas
{
    public partial class Form1 : Form
    {
        private FlowLayoutPanel tabsHeaderPanel;
        private Panel contentPanel;
        private TextBox txtUrl;
        private Button btnBack, btnForward, btnRefresh, btnHome, btnHistory, btnBookmarks, btnAddBookmark, btnGo, btnNewTab, btnDownloads;
        private CoreWebView2Environment webEnvironment;
        private CoreWebView2Environment incognitoEnvironment;
        private string incognitoProfileDir;

        private Panel activeBrowserContainer = null;
        private Panel activeTabContainer = null;
        private readonly List<DownloadEntry> downloads = new List<DownloadEntry>();
        private DownloadManagerForm downloadManagerForm;

        // Поиск по странице
        private Panel findPanel;
        private TextBox findBox;

        // Закреплённые вкладки
        private readonly Dictionary<Panel, bool> pinnedTabs = new Dictionary<Panel, bool>();

        // Недавно закрытые вкладки (для Ctrl+Shift+T)
        private readonly Stack<string> recentlyClosed = new Stack<string>();

        // Полноэкранный режим видео
        private bool videoFullScreen = false;
        private FormWindowState prevWindowState;
        private FormBorderStyle prevBorderStyle;
        private bool prevMaximizeBox, prevMinimizeBox;

        // Настройки приложения
        private AppSettings appSettings = new AppSettings();
        private readonly string settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.dat");

        // Логирование
        private readonly string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "atlas.log");

        // Единый HTTP-клиент для всех сетевых запросов (избегаем исчерпания сокетов)
        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            // GitHub API требует User-Agent — без него возвращает 403
            client.DefaultRequestHeaders.Add("User-Agent", "Atlas-Browser/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return client;
        }

        private const string AppVersion = "1.0.18";
        private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";
        private const string UpdateRepoFixed = "werkot20000009853-bot/Atlas-Browser";

        // Современные иконки (Segoe MDL2 Assets — встроен в Windows 10/11)
        private const string IconFont = "Segoe MDL2 Assets";
        private const string IcoBack      = "\uE0A6";
        private const string IcoForward   = "\uE0A7";
        private const string IcoRefresh   = "\uE72C";
        private const string IcoHome      = "\uE80F";
        private const string IcoHistory   = "\uE81C";
        private const string IcoStar      = "\uE728"; // контур звезды
        private const string IcoStarFill  = "\uE735"; // залитая звезда
        private const string IcoDownload  = "\uE896";
        private const string IcoAdd       = "\uE710";
        private const string IcoIncognito = "\uE971";
        private const string IcoShield    = "\uE7EE";
        private const string IcoPicker    = "\uE7B3";
        private const string IcoSettings  = "\uE713";
        private const string IcoExtension = "\uE9E7"; // пазл-расширение
        private const string IcoExternal = "\uE71B"; // открыть во внешнем приложении
        private const string IcoClose     = "\uE711";
        private const string IcoMin       = "\uE70D";
        private const string IcoMax       = "\uE737";
        private const string IcoPause     = "\uE769";
        private const string IcoFolder    = "\uE838";
        private const string IcoDelete    = "\uE74D";

        // ---------- Космическая цветовая палитра Atlas ----------
        private static readonly Color SpaceDark   = Color.FromArgb(2, 5, 12);   // фон формы
        private static readonly Color PanelDark   = Color.FromArgb(6, 13, 28);  // панели
        private static readonly Color Surface     = Color.FromArgb(10, 22, 42); // поверхности (вкладки)
        private static readonly Color SurfaceAlt  = Color.FromArgb(14, 28, 52); // альтернативная поверхность
        private static readonly Color Accent      = Color.FromArgb(56, 139, 253); // основной акцент (синий)
        private static readonly Color AccentHover = Color.FromArgb(82, 165, 255);
        private static readonly Color AccentDim   = Color.FromArgb(28, 70, 128);
        private static readonly Color TextMain    = Color.FromArgb(226, 236, 252); // основной текст
        private static readonly Color TextMuted   = Color.FromArgb(139, 164, 196); // secondairesй текст
        private static readonly Color BorderCol   = Color.FromArgb(45, 70, 105);  // границы
        private static readonly Color BorderHover = Color.FromArgb(90, 130, 180);

        private class DownloadEntry
        {
            public string FileName { get; set; }
            public string FilePath { get; set; }
            public string Url { get; set; }
            public string Status { get; set; }
            public long Received { get; set; }
            public long Total { get; set; }
            public CoreWebView2DownloadOperation Operation { get; set; }
        }

        // Настройки приложения
        internal class AppSettings
        {
            public string Theme { get; set; } = "dark";       // dark / light
            public string SearchEngine { get; set; } = "google"; // google / duckduckgo / bing / yandex
            public string DownloadFolder { get; set; } = "";  // пусто = папка Downloads
            public bool AutoUpdate { get; set; } = true;
            public bool AutoInstall { get; set; } = false;  // скачивать и запускать установщик автоматически
            public string LastUpdateCheck { get; set; } = "";
            public string UpdateRepo { get; set; } = "werkot20000009853-bot/Atlas-Browser";
            public bool SingleProcessMode { get; set; } = false; // false = стабильный режим (много процессов)
        }

        // Категория правил блокировщика
        private class BlockerCategory
        {
            public string Name { get; set; }
            public bool Enabled { get; set; } = true;
            public List<string> Domains { get; set; } = new List<string>();

            public BlockerCategory() { }
            public BlockerCategory(string name, bool enabled, params string[] domains)
            {
                Name = name;
                Enabled = enabled;
                Domains = new List<string>(domains);
            }
        }

        // Настройки встроенного блокировщика
        private class BlockerSettings
        {
            public bool Enabled { get; set; } = true;
            public List<string> BlockedDomains { get; set; } = new List<string>();
            public List<BlockerCategory> Categories { get; set; } = new List<BlockerCategory>();
            public int BlockedCount { get; set; } = 0;
            public int Version { get; set; } = 4;
        }

        private class DownloadManagerForm : Form
        {
            public FlowLayoutPanel CardsPanel { get; private set; }
            public Label CountLabel { get; private set; }
            public Label EmptyLabel { get; private set; }
            public Button OpenFolderBtn { get; private set; }
            public Button ClearBtn { get; private set; }

            public DownloadManagerForm()
            {
                Text = "Atlas — Загрузки";
                Width = 920;
                Height = 650;
                MinimumSize = new Size(680, 430);
                BackColor = Color.FromArgb(4, 10, 23);
                ForeColor = Color.White;
                StartPosition = FormStartPosition.CenterParent;
                Font = new Font("Segoe UI", 9F);
                ShowIcon = false;

                Panel header = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.FromArgb(7, 17, 36), Padding = new Padding(28, 20, 24, 12) };
                Label title = new Label { Text = "Загрузки", AutoSize = true, Location = new Point(26, 18), ForeColor = Color.FromArgb(239, 246, 255), Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold) };
                Label subtitle = new Label { Text = "Все загруженные файлы находятся под контролем", AutoSize = true, Location = new Point(29, 59), ForeColor = Color.FromArgb(126, 153, 190), Font = new Font("Segoe UI", 9.5F) };
                CountLabel = new Label { Text = "0 файлов", AutoSize = true, Location = new Point(29, 83), ForeColor = Color.FromArgb(102, 177, 255), Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold) };
                header.Controls.Add(title);
                header.Controls.Add(subtitle);
                header.Controls.Add(CountLabel);

                OpenFolderBtn = new Button
                {
                    Text = "Папка",
                    Location = new Point(680, 20),
                    Size = new Size(100, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(17, 37, 64),
                    ForeColor = Color.FromArgb(218, 234, 255),
                    Cursor = Cursors.Hand
                };
                OpenFolderBtn.FlatAppearance.BorderSize = 0;
                ClearBtn = new Button
                {
                    Text = "Очистить",
                    Location = new Point(790, 20),
                    Size = new Size(100, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(65, 30, 35),
                    ForeColor = Color.FromArgb(255, 160, 160),
                    Cursor = Cursors.Hand
                };
                ClearBtn.FlatAppearance.BorderSize = 0;
                header.Controls.Add(OpenFolderBtn);
                header.Controls.Add(ClearBtn);

                Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(4, 10, 23), Padding = new Padding(18, 16, 8, 12) };
                CardsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(4, 10, 23), Padding = new Padding(4, 2, 10, 12) };
                CardsPanel.SizeChanged += (s, e) => { foreach (Control c in CardsPanel.Controls) c.Width = Math.Max(300, CardsPanel.ClientSize.Width - 30); };
                EmptyLabel = new Label { Text = "↓\r\n\r\nЗдесь пока пусто\r\n\r\nКогда вы скачаете файл, он появится здесь", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(105, 132, 169), Font = new Font("Segoe UI", 12F), BackColor = Color.FromArgb(4, 10, 23) };
                body.Controls.Add(CardsPanel);
                body.Controls.Add(EmptyLabel);
                Controls.Add(body);
                Controls.Add(header);
            }

            public void ApplyDarkHeader(Action<Form, string> applier)
            {
                applier(this, "Atlas — Загрузки");
            }
        }

        // ---------- Окно настроек блокировщика ----------
        private class BlockerSettingsForm : Form
        {
            public CheckBox EnableCheck { get; private set; }
            public List<BlockerCategory> WorkingCategories { get; private set; }
            public Label StatsLabel { get; private set; }

            private CheckedListBox categoryList;
            private ListBox domainList;
            private TextBox newDomainBox;
            private int selectedCategory = -1;

            public BlockerSettingsForm(BlockerSettings settings, int sessionCount)
            {
                Text = "Atlas — Блокировщик";
                Width = 720;
                Height = 620;
                MinimumSize = new Size(640, 540);
                StartPosition = FormStartPosition.CenterParent;
                BackColor = Color.FromArgb(3, 7, 17);
                ForeColor = Color.FromArgb(226, 236, 252);
                Font = new Font("Segoe UI", 9.5F);
                ShowIcon = false;

                // Рабочая копия категорий (не изменяем оригинал до сохранения)
                WorkingCategories = new List<BlockerCategory>();
                foreach (var c in settings.Categories)
                {
                    WorkingCategories.Add(new BlockerCategory
                    {
                        Name = c.Name,
                        Enabled = c.Enabled,
                        Domains = new List<string>(c.Domains)
                    });
                }

                Panel header = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 86,
                    BackColor = Color.FromArgb(7, 17, 36)
                };
                Label title = new Label
                {
                    Text = "Блокировщик рекламы и трекеров",
                    AutoSize = true,
                    Location = new Point(24, 16),
                    ForeColor = Color.FromArgb(226, 236, 252),
                    Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold)
                };
                StatsLabel = new Label
                {
                    Text = $"Заблокировано за сессию: {sessionCount}  •  Всего: {settings.BlockedCount}",
                    AutoSize = true,
                    Location = new Point(26, 50),
                    ForeColor = Color.FromArgb(56, 139, 253),
                    Font = new Font("Segoe UI", 9F)
                };
                header.Controls.Add(title);
                header.Controls.Add(StatsLabel);

                EnableCheck = new CheckBox
                {
                    Text = "Включить блокировку",
                    Checked = settings.Enabled,
                    Location = new Point(24, 102),
                    AutoSize = true,
                    ForeColor = Color.FromArgb(226, 236, 252)
                };

                Label catLabel = new Label
                {
                    Text = "Категории (галочка = блокировать):",
                    Location = new Point(24, 132),
                    AutoSize = true,
                    ForeColor = Color.FromArgb(139, 164, 196)
                };

                categoryList = new CheckedListBox
                {
                    Location = new Point(24, 154),
                    Size = new Size(310, 370),
                    BackColor = Color.FromArgb(10, 22, 42),
                    ForeColor = Color.FromArgb(226, 236, 252),
                    BorderStyle = BorderStyle.FixedSingle,
                    CheckOnClick = true
                };
                for (int i = 0; i < WorkingCategories.Count; i++)
                {
                    categoryList.Items.Add(WorkingCategories[i].Name, WorkingCategories[i].Enabled);
                }
                categoryList.SelectedIndexChanged += (s, e) =>
                {
                    selectedCategory = categoryList.SelectedIndex;
                    RefreshDomains();
                };
                categoryList.ItemCheck += (s, e) =>
                {
                    // تحديث состояния категории при изменении галочки
                    if (e.Index >= 0 && e.Index < WorkingCategories.Count)
                        WorkingCategories[e.Index].Enabled = e.NewValue == CheckState.Checked;
                };

                Label domLabel = new Label
                {
                    Text = "Домены выбранной категории:",
                    Location = new Point(350, 132),
                    AutoSize = true,
                    ForeColor = Color.FromArgb(139, 164, 196)
                };

                domainList = new ListBox
                {
                    Location = new Point(350, 154),
                    Size = new Size(338, 320),
                    BackColor = Color.FromArgb(10, 22, 42),
                    ForeColor = Color.FromArgb(226, 236, 252),
                    BorderStyle = BorderStyle.FixedSingle
                };

                newDomainBox = new TextBox
                {
                    Location = new Point(350, 482),
                    Size = new Size(240, 28),
                    BackColor = Color.FromArgb(10, 18, 34),
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 10F)
                };

                Button addBtn = new Button
                {
                    Text = "Добавить",
                    Location = new Point(598, 482),
                    Size = new Size(90, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(28, 70, 128),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold)
                };
                addBtn.FlatAppearance.BorderSize = 0;
                addBtn.Click += (s, e) =>
                {
                    if (selectedCategory < 0) { MessageBox.Show("Выберите категорию.", "Atlas"); return; }
                    string rule = newDomainBox.Text.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(rule)) return;
                    if (!WorkingCategories[selectedCategory].Domains.Contains(rule))
                    {
                        WorkingCategories[selectedCategory].Domains.Add(rule);
                        RefreshDomains();
                        newDomainBox.Clear();
                    }
                };

                Button removeBtn = new Button
                {
                    Text = "Удалить",
                    Location = new Point(598, 516),
                    Size = new Size(90, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(65, 30, 35),
                    ForeColor = Color.FromArgb(255, 160, 160),
                    Cursor = Cursors.Hand,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold)
                };
                removeBtn.FlatAppearance.BorderSize = 0;
                removeBtn.Click += (s, e) =>
                {
                    if (selectedCategory < 0) return;
                    if (domainList.SelectedIndex >= 0)
                    {
                        WorkingCategories[selectedCategory].Domains.RemoveAt(domainList.SelectedIndex);
                        RefreshDomains();
                    }
                };

                Button selectAllBtn = new Button
                {
                    Text = "Выбрать все",
                    Location = new Point(24, 532),
                    Size = new Size(95, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(17, 34, 60),
                    ForeColor = TextMuted,
                    Cursor = Cursors.Hand,
                    Font = new Font("Segoe UI", 8.5F)
                };
                selectAllBtn.FlatAppearance.BorderSize = 0;
                selectAllBtn.Click += (s, e) =>
                {
                    for (int i = 0; i < categoryList.Items.Count; i++)
                    {
                        categoryList.SetItemChecked(i, true);
                        WorkingCategories[i].Enabled = true;
                    }
                };

                Button deselectAllBtn = new Button
                {
                    Text = "Снять все",
                    Location = new Point(126, 532),
                    Size = new Size(95, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(17, 34, 60),
                    ForeColor = TextMuted,
                    Cursor = Cursors.Hand,
                    Font = new Font("Segoe UI", 8.5F)
                };
                deselectAllBtn.FlatAppearance.BorderSize = 0;
                deselectAllBtn.Click += (s, e) =>
                {
                    for (int i = 0; i < categoryList.Items.Count; i++)
                    {
                        categoryList.SetItemChecked(i, false);
                        WorkingCategories[i].Enabled = false;
                    }
                };

                Button saveBtn = new Button
                {
                    Text = "Сохранить",
                    Location = new Point(350, 532),
                    Size = new Size(200, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(56, 139, 253),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    DialogResult = DialogResult.OK
                };
                saveBtn.FlatAppearance.BorderSize = 0;

                Controls.Add(header);
                Controls.Add(EnableCheck);
                Controls.Add(catLabel);
                Controls.Add(categoryList);
                Controls.Add(domLabel);
                Controls.Add(domainList);
                Controls.Add(newDomainBox);
                Controls.Add(addBtn);
                Controls.Add(removeBtn);
                Controls.Add(selectAllBtn);
                Controls.Add(deselectAllBtn);
                Controls.Add(saveBtn);

                if (categoryList.Items.Count > 0)
                    categoryList.SelectedIndex = 0;
            }

            public void ApplyDarkHeader(Action<Form, string> applier)
            {
                applier(this, "Atlas — Блокировщик");
            }

            private void RefreshDomains()
            {
                domainList.Items.Clear();
                if (selectedCategory >= 0 && selectedCategory < WorkingCategories.Count)
                {
                    foreach (var d in WorkingCategories[selectedCategory].Domains)
                        domainList.Items.Add(d);
                }
            }
        }

        internal class HistoryItem
        {
            public string Url { get; set; }
            public string Title { get; set; }
            public string Time { get; set; }
        }

        internal class BookmarkItem
        {
            public string Url { get; set; }
            public string Title { get; set; }
        }

        private List<HistoryItem> visitHistory = new List<HistoryItem>();
        private List<BookmarkItem> bookmarks = new List<BookmarkItem>();

        private string HomePageUrl => BuildHomePageUrl();
        private readonly string historyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.dat");
        private readonly string bookmarksFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bookmarks.dat");

        // Режим инкогнито — история и закладки не сохраняются
        private bool incognitoMode = false;

        // Встроенный блокировщик рекламы и трекеров
        private readonly string blockerFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blocker.dat");
        private BlockerSettings blocker = new BlockerSettings();
        private int sessionBlockedCount = 0;

        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 0x0002;

        private Panel titleBar;
        private Button btnWinMin, btnWinMax, btnWinClose;
        private Button btnIncognito;
        private Button btnBlocker;
        private Button btnPicker;
        private Button btnSettings;
        private Button btnExtensions;
        private bool pickerMode = false;
        private string currentPageUrl = "";

        public Form1()
        {
            InitializeComponent();
            LoadAppSettings();
            LoadData();
            // Автоматический перенос закладок и истории из Chrome/Edge при первом запуске
            _ = Task.Run(() => AutoImportFromBrowsers());
            LoadBlockerSettings();
            InitializeBrowserControls();
            InitializeTitleBar();
            ApplyTheme();
            InitializeAsync();
            CheckForUpdatesOnStartup();
        }

        private async void InitializeAsync()
        {
            string singleProcessArg = appSettings.SingleProcessMode ? "--single-process " : "";
            var options = new CoreWebView2EnvironmentOptions(
                additionalBrowserArguments: singleProcessArg + "--disable-component-update --disable-default-apps --disable-features=msEdgeSidebarV2,msEdgeSplitScreen --no-first-run"
            );
            options.AreBrowserExtensionsEnabled = true;

            webEnvironment = await CoreWebView2Environment.CreateAsync(null, null, options);

            // Отдельный профиль для инкогнито — во Temp, удаляется при выходе
            string incArg = appSettings.SingleProcessMode ? "--single-process " : "";
            var incOptions = new CoreWebView2EnvironmentOptions(
                additionalBrowserArguments: incArg + "--disable-component-update --disable-default-apps --disable-features=msEdgeSidebarV2,msEdgeSplitScreen --no-first-run"
            );
            incOptions.AreBrowserExtensionsEnabled = true;
            incognitoProfileDir = Path.Combine(Path.GetTempPath(), "AtlasIncognito_" + Guid.NewGuid().ToString("N"));
            incognitoEnvironment = await CoreWebView2Environment.CreateAsync(null, incognitoProfileDir, incOptions);

            AddNewTab(HomePageUrl);
        }

        // Полная очистка временного инкогнито-профиля (cookies, localStorage, кеш)
        private async Task ResetIncognitoProfileAsync()
        {
            try
            {
                incognitoEnvironment = null;
                if (!string.IsNullOrEmpty(incognitoProfileDir))
                {
                    // WebView2 держит файлы — даём время освободить
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            if (Directory.Exists(incognitoProfileDir))
                                Directory.Delete(incognitoProfileDir, true);
                            break;
                        }
                        catch { await Task.Delay(200); }
                    }
                }

                string incArg = appSettings.SingleProcessMode ? "--single-process " : "";
                var incOptions = new CoreWebView2EnvironmentOptions(
                    additionalBrowserArguments: incArg + "--disable-component-update --disable-default-apps --disable-features=msEdgeSidebarV2,msEdgeSplitScreen --no-first-run"
                );
                incOptions.AreBrowserExtensionsEnabled = true;
                incognitoProfileDir = Path.Combine(Path.GetTempPath(), "AtlasIncognito_" + Guid.NewGuid().ToString("N"));
                incognitoEnvironment = await CoreWebView2Environment.CreateAsync(null, incognitoProfileDir, incOptions);
                Log("Incognito profile reset");
            }
            catch (Exception ex) { Log("Incognito reset error", ex); }
        }

        private string BuildHomePageUrl()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("Atlas.HomePage.html");
            if (stream == null) { Log("HomePage.html resource missing"); return "about:blank"; }
            using var reader = new StreamReader(stream);
            return "data:text/html;charset=utf-8," + Uri.EscapeDataString(reader.ReadToEnd());
        }

        // --- Защищённое локальное хранилище (DPAPI, привязка к текущему пользователю Windows) ---
        private string ReadProtected(string path, string legacyPath)
        {
            if (legacyPath != null) return CryptoService.ReadProtectedWithLegacy(path, legacyPath);
            return CryptoService.ReadProtected(path);
        }

        private void WriteProtected(string path, string json) => CryptoService.WriteProtected(path, json);

        // --- Логирование ошибок в файл ---
        private void Log(string message, Exception ex = null)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                if (ex != null) line += Environment.NewLine + ex;
                File.AppendAllText(logFilePath, line + Environment.NewLine);
            }
            catch { }
        }

        // --- Настройки приложения ---
        private void LoadAppSettings()
        {
            try
            {
                string json = ReadProtected(settingsFilePath, null);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) appSettings = loaded;
                }
            }
            catch (Exception ex) { Log("LoadAppSettings error", ex); }
        }

        private void SaveAppSettings()
        {
            try { WriteProtected(settingsFilePath, JsonSerializer.Serialize(appSettings)); }
            catch (Exception ex) { Log("SaveAppSettings error", ex); }
        }

        // Убирает светлую системную шапку у диалогов — рисует свою в теме Atlas
        private void ApplyDarkTitle(Form form, string title)
        {
            form.FormBorderStyle = FormBorderStyle.None;
            form.Text = title;
            form.ShowIcon = false;

            Panel bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = Color.FromArgb(7, 17, 36),
                Cursor = Cursors.SizeAll
            };
            bar.Paint += (s, e) =>
            {
                var g = e.Graphics;
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, bar.Height),
                    Color.FromArgb(9, 18, 36), Color.FromArgb(5, 11, 24));
                g.FillRectangle(brush, bar.ClientRectangle);
                using var pen = new Pen(BorderCol, 1);
                g.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };

            Label caption = new Label
            {
                Text = title,
                Location = new Point(12, 0),
                Height = 34,
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 9.5F),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            Button close = new Button
            {
                Text = IcoClose,
                Font = new Font(IconFont, 11),
                Width = 34,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            close.FlatAppearance.BorderSize = 0;
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 45, 55);
            close.MouseEnter += (s, e) => close.ForeColor = Color.White;
            close.MouseLeave += (s, e) => close.ForeColor = TextMuted;
            close.Click += (s, e) => { form.DialogResult = DialogResult.Cancel; form.Close(); };
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Left = 0; // будет скорректировано при Resize

            bar.Resize += (s, e) => { close.Left = bar.Width - 34; };
            bar.Controls.Add(caption);
            bar.Controls.Add(close);

            // Перетаскивание диалога
            bar.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    SendMessage(form.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            };

            // Сдвигаем существующие контролы вниз на высоту шапки
            var controls = form.Controls.Cast<Control>().ToList();
            foreach (var c in controls)
                c.Top += 34;
            form.Controls.Add(bar);
            form.Padding = new Padding(1);
        }

        private void ApplyTheme()
        {
            bool light = appSettings.Theme == "light";
            this.BackColor = light ? Color.FromArgb(240, 244, 250) : SpaceDark;
            if (contentPanel != null) contentPanel.BackColor = this.BackColor;
        }

        private void LoadData()
        {
            if (incognitoMode) return; // в инкогнито не загружаем сохранённые данные

            string oldHistory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");
            string oldBookmarks = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bookmarks.json");

            string histJson = ReadProtected(historyFilePath, oldHistory);
            if (!string.IsNullOrEmpty(histJson))
            {
                var loaded = JsonSerializer.Deserialize<List<HistoryItem>>(histJson);
                if (loaded != null) visitHistory = loaded;
            }

            string bmJson = ReadProtected(bookmarksFilePath, oldBookmarks);
            if (!string.IsNullOrEmpty(bmJson))
            {
                var loaded = JsonSerializer.Deserialize<List<BookmarkItem>>(bmJson);
                if (loaded != null) bookmarks = loaded;
            }
        }

        private void SaveHistory()
        {
            if (incognitoMode) return;
            try { WriteProtected(historyFilePath, JsonSerializer.Serialize(visitHistory)); }
            catch (Exception ex) { Log("SaveHistory error", ex); }
        }

        private void SaveBookmarks()
        {
            if (incognitoMode) return;
            try { WriteProtected(bookmarksFilePath, JsonSerializer.Serialize(bookmarks)); }
            catch (Exception ex) { Log("SaveBookmarks error", ex); }
        }

        // --- Настройки блокировщика (шифруются вместе с остальными данными) ---
        // Категории с доменами. Статус из данных: true = «Заблокировано», false = «Доступно».
        private static List<BlockerCategory> BuildDefaultCategories()
        {
            return new List<BlockerCategory>
            {
                new BlockerCategory("Amazon Ads", true,
                    "aax.amazon-adsystem.com", "aan.amazon.com"),
                new BlockerCategory("DoubleClick (Google)", true,
                    "ad.doubleclick.net", "pubads.g.doubleclick.net", "securepubads.g.doubleclick.net"),
                new BlockerCategory("Media.net", true,
                    "static.media.net", "contextual.media.net"),
                new BlockerCategory("Google Ads", true,
                    "pagead2.googlesyndication.com", "adservice.google.com", "googleadservices.com", "tpc.googlesyndication.com"),
                new BlockerCategory("Criteo", true,
                    "static.criteo.net", "bidder.criteo.com"),
                new BlockerCategory("Taboola", true,
                    "cdn.taboola.com", "trc.taboola.com", "images.taboola.com", "nr.taboola.com", "api.taboola.com"),
                new BlockerCategory("Teads (formerly Outbrain)", true,
                    "log.outbrain.com", "widgets.outbrain.com", "odb.outbrain.com"),
                new BlockerCategory("MGID", false,
                    "cdn.mgid.com", "servicer.mgid.com"),
                new BlockerCategory("Microsoft Advertising", true,
                    "bat.bing.com"),
                new BlockerCategory("AppLovin", true,
                    "d.applovin.com", "rt.applovin.com", "ms.applovin.com"),
                new BlockerCategory("Vungle / Liftoff", false,
                    "api.vungle.com"),
                new BlockerCategory("Microsoft Monetize (Xandr)", true,
                    "ib.adnxs.com", "secure.adnxs.com", "acdn.adnxs.com", "prebid.adnxs.com"),
                new BlockerCategory("PubMatic", true,
                    "ads.pubmatic.com", "image6.pubmatic.com", "hbopenbid.pubmatic.com"),
                new BlockerCategory("OpenX", false,
                    "openx.net", "us-ads.openx.net", "rtb.openx.net", "u.openx.net"),
                new BlockerCategory("Magnite (Rubicon / SpotX)", true,
                    "pixel.rubiconproject.com", "fastlane.rubiconproject.com"),
                new BlockerCategory("Index Exchange (Casale Media)", true,
                    "casalemedia.com", "as.casalemedia.com", "cdn.indexexchange.com", "htlb.casalemedia.com"),
                new BlockerCategory("Yahoo Ads", false,
                    "ads.yahoo.com"),
                new BlockerCategory("Unity Ads", true,
                    "auction.unityads.unity3d.com", "webview.unityads.unity3d.com", "config.unityads.unity3d.com", "adserver.unityads.unity3d.com"),
                new BlockerCategory("Yandex", true,
                    "adfox.yandex.ru", "adfstat.yandex.ru"),
                new BlockerCategory("Мобильные рекламные сети", true,
                    "live.chartboost.com", "init.supersonicads.com", "api.fyber.com", "ironsource.mobi", "outcome-ssp.supersonicads.com"),
                new BlockerCategory("YouTube Ads", false,
                    "s.youtube.com"),
                new BlockerCategory("Рекламные биржи и SSP", true,
                    "adsrvr.org", "ad.gt", "contextweb.com", "eb2.3lift.com", "tlx.3lift.com",
                    "apex.go.sonobi.com", "c.gumgum.com", "a.teads.tv", "cdn.teads.tv", "cdn.kargo.com", "sync.kargo.com"),
                new BlockerCategory("AdRoll", true,
                    "d.adroll.com", "s.adroll.com"),
                new BlockerCategory("Верификация рекламы", true,
                    "doubleverify.com", "cdn.doubleverify.com", "pixel.adsafeprotected.com", "static.adsafeprotected.com", "fw.adsafeprotected.com", "insightexpressai.com"),
                new BlockerCategory("Всплывающая реклама", true,
                    "popads.net", "popcash.net", "onclickads.net", "clickadu.com", "trafficjunky.net", "exoclick.com", "juicyads.com"),
                new BlockerCategory("The Trade Desk", true,
                    "direct.adsrvr.org", "match.adsrvr.org"),
                new BlockerCategory("Прочие трекеры", true,
                    "google-analytics.com", "googletagmanager.com", "connect.facebook.net", "facebook.net",
                    "scorecardresearch.com", "quantserve.com", "chartbeat.net", "hotjar.com", "mixpanel.com", "segment.io"),
                new BlockerCategory("Whitelist (разрешённые сайты)", true,
                    "accounts.google.com", "www.google.com", "www.gstatic.com", "www.youtube.com", "youtube.com",
                    "google.com", "gstatic.com", "recaptcha.net", "googleusercontent.com", "googleapis.com"),
                // === Расширенная защита: рекламные сети, трекеры и паттерны ===
                new BlockerCategory("Расширенная защита (доп. сети)", true,
                    // Рекламные сети и биржи
                    "adcolony.com", "adc-deploy.com", "addthis.com", "addtoany.com", "adform.net", "adnxs.com",
                    "adtech.de", "adthrive.com", "advanalytics.org", "adyieldmanager.com", "advertising.com",
                    "advrb.com", "adzwitch.com", "aerserv.com", "amazon-adsystem.com", "amoad.com", "appier.com",
                    "aucdn.net", "avian.com", "bluekai.com", "brightroll.com", "bidswitch.net", "brtbx.net",
                    "bucketly.com", "buysellads.com", "c3tag.com", "carbonads.net", "cedato.com", "chango.com",
                    "clickforce.com", "clicktripz.com", "collective-media.net", "connexity.net", "conversantmedia.com",
                    "crashlytics.com", "criteo.com", "criteo.net", "cxense.com", "data.adintelligence.com",
                    "demandbase.com", "districtm.io", "dotomi.com", "ds-aksb-a.akamaihd.net", "dwtrack.com",
                    "easyswap.org", "emxg.com", "epom.com", "etsy.com/ads", "exponential.com", "eyeota.net",
                    "faithmedia.com", "flite.com", "focalpixel.com", "freewheel.com", "ftracking.net",
                    "gamoshi.com", "gannettmm.com", "gdpr.ad", "geniee.co.jp", "geoplugin.net", "getclicky.com",
                    "gid.gorillas.group", "glanceguide.com", "google-analytics.com", "googleadservices.com",
                    "googlesyndication.com", "grapeshot.co.uk", "gumgum.com", "gwallet.com", "hashicorp.com",
                    "hdadnet.com", "hit.gg", "hpjad.com", "htrib.net", "i-mobile.co.jp", "iabtechlab.com",
                    "ibm.com/ads", "im-apps.net", "impressiondesk.com", "improvedigital.com", "indexexchange.com",
                    "inner-active.mobi", "install.exec", "intentiq.com", "intermarkets.net", "ireo.io",
                    "jetstack.com", "jina.ai", "juicyads.com", "ketchup.cl", "kiosked.com", "kochava.com",
                    "krushmedia.com", "kubient.com", "leadforce1.com", "legolas-media.com", "lijit.com",
                    "linkury.com", "liqwid.net", "localytics.com", "loopme.me", "lunamedia.com", "madisonlogic.com",
                    "madvertise.com", "media.net", "media6degrees.com", "mediabrands.com", "mediamath.com",
                    "mediatonic.com", "merchant-quotient.net", "millennialmedia.com", "minefield.net", "mixpanel.com",
                    "mobfox.com", "mobiclix.com", "mobilead.id", "mobilitytr.com", "mojiva.com", "monetize.io",
                    "mookie1.com", "moovcorp.com", "mopub.com", "myads.com", "mylocaladexchange.com",
                    "nanointeractive.com", "nativo.net", "netseer.com", "newrelic.com", "nexage.com", "nielsen.com",
                    "nosto.com", "nugg.ad", "o2-cdn.com", "omnitagjs.com", "onemata.com", "openx.net", "opera.com/ads",
                    "optimizely.com", "outbrain.com", "ownlocal.com", "ozone.co.uk", "p-adv.net", "pardot.com",
                    "paypal.com/ads", "pchome.com.tw/ads", "peer39.com", "perfor max.com", "pinterest.com/ads",
                    "pixel.quantserve.com", "platform144.com", "playhaven.com", "po.st", "popads.net", "popcash.net",
                    "prebid.org", "predicative.com", "precisionclick.com", "predicta.com", "pubmatic.com", "pulse360.com",
                    "quantcast.com", "quantserve.com", "radiumone.com", "rapleaf.com", "realtimebidder.com",
                    "rebrush.com", "resultspage.net", "revcontent.com", "richaudience.com", "roisystem.in",
                    "rtbhouse.com", "rubiconproject.com", "s3.amazonaws.com/ads", "sapientmedia.com", "saymedia.com",
                    "scientiamobile.com", "scoutpanel.com", "scribd.com/ads", "segment.io", "sekindo.com",
                    "semasio.net", "serverbid.com", "sharethrough.com", "shopify.com/ads", "siftmedia.com",
                    "signal.co", "simplifi.re", "sitepoint.com/ads", "smartadserver.com", "smartyads.com",
                    "socialweet.com", "sonobi.com", "sovrn.com", "speakol.nl", "spotx.tv", "springserve.com",
                    "sspnet.com", "statcounter.com", "steelhouse.com", "stripchat.com/ads", "suntimes.com/ads",
                    "supership.jp", "swoop.com", "synacor.com", "taboola.com", "tad.net", "tags.bluekai.com",
                    "tailtarget.com", "tapdoubles.com", "teads.tv", "telstra.com/ads", "thetradedesk.com",
                    "tidal.tv/ads", "tigtag.com", "tiktok.com/ads", "time.com/ads", "tmt1.com", "toboads.com",
                    "tonefuse.com", "tou点击.com", "tracemyip.com", "tracking-dome.com", "traveladvertising.com",
                    "tribalfusion.com", "triplelift.com", "truecaller.com/ads", "turbobytes.net", "twitter.com/ads",
                    "ucfunnel.com", "undertone.com", "unrulygroup.com", "up-root.com", "urbanlogiq.com",
                    "ushmm.org/ads", "vadano.com", "validClick.com", "various.com/ads", "veinteractive.com",
                    "vendor.org/ads", "videoplaza.tv", "vidyard.com", "vidoomy.com", "viewdeos.com", "viralize.com",
                    "visiblemeasures.com", "viva.com/ads", "vliesteding.com", "vungle.com", "w4a.info",
                    "weborama.fr", "wedpic.com", "widespace.com", "wildtangent.com", "windows.com/ads",
                    "wordstream.com", "wp.com/ads", "xad.com", "xandr.com", "xing.com/ads", "yahoo.com/ads",
                    "yandex.ru/ads", "yandexadexchange.net", "yielde rumors.com", "yieldlab.net", "yieldmanager.com",
                    "yieldmo.com", "yieldmore.net", "zads.com", "zedo.com", "zemanta.com", "zeta.com", "ziffdavis.com/ads",
                    "zooz.com", "zynga.com/ads")
            };
        }

        private static string[] GetAllDefaultDomains()
        {
            var result = new List<string>();
            foreach (var cat in BuildDefaultCategories())
                result.AddRange(cat.Domains);
            return result.Distinct().ToArray();
        }

        private void LoadBlockerSettings()
        {
            try
            {
                string json = ReadProtected(blockerFilePath, null);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonSerializer.Deserialize<BlockerSettings>(json);
                    if (loaded != null)
                    {
                        blocker = loaded;
                        if (blocker.BlockedDomains == null)
                            blocker.BlockedDomains = new List<string>();
                        if (blocker.Categories == null)
                            blocker.Categories = new List<BlockerCategory>();

                        // Миграция с версии 2 (плоский список) на версию 3 (категории)
                        if (blocker.Version < 3)
                        {
                            blocker.Categories = BuildDefaultCategories();
                            blocker.BlockedDomains = new List<string>(GetAllDefaultDomains());
                            blocker.Version = 3;
                            SaveBlockerSettings();
                        }
                        // Миграция на версию 4 — обновляем Whitelist для входа в Google/YouTube
                        if (blocker.Version < 4)
                        {
                            var defaults = BuildDefaultCategories();
                            var defWl = defaults.FirstOrDefault(c => c.Name.StartsWith("Whitelist"));
                            var curWl = blocker.Categories.FirstOrDefault(c => c.Name.StartsWith("Whitelist"));
                            if (defWl != null && curWl != null)
                            {
                                foreach (var d in defWl.Domains)
                                    if (!curWl.Domains.Contains(d)) curWl.Domains.Add(d);
                            }
                            else if (defWl != null && curWl == null)
                                blocker.Categories.Add(defWl);
                            blocker.Version = 4;
                            SaveBlockerSettings();
                        }
                        return;
                    }
                }
            }
            catch (Exception ex) { Log("LoadBlockerSettings error", ex); }

            // Первый запуск — стандартные категории
            blocker = new BlockerSettings
            {
                Enabled = true,
                Categories = BuildDefaultCategories(),
                BlockedDomains = new List<string>(GetAllDefaultDomains())
            };
            SaveBlockerSettings();
        }

        private void SaveBlockerSettings()
        {
            try { WriteProtected(blockerFilePath, JsonSerializer.Serialize(blocker)); }
            catch (Exception ex) { Log("SaveBlockerSettings error", ex); }
        }

        // Проверка, нужно ли блокировать запрос (только включённые категории)
        private bool ShouldBlock(string url)
        {
            if (!blocker.Enabled || string.IsNullOrEmpty(url)) return false;
            // Whitelist: не блокируем запросы к разрешённым доменам (в т.ч.accounts.google.com)
            if (IsWhitelisted(url)) return false;
            if (!string.IsNullOrEmpty(currentPageUrl) && IsWhitelisted(currentPageUrl)) return false;

            foreach (var cat in blocker.Categories)
            {
                if (!cat.Enabled) continue;
                foreach (var domain in cat.Domains)
                {
                    if (!string.IsNullOrWhiteSpace(domain) &&
                        url.IndexOf(domain, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            // Пользовательские правила (не привязанные к категориям)
            foreach (var domain in blocker.BlockedDomains)
            {
                if (!string.IsNullOrWhiteSpace(domain) &&
                    url.IndexOf(domain, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private void InitializeBrowserControls()
        {
            this.Width = 1250;
            this.Height = 850;
            this.Text = "Atlas — Cosmic Browser";
            this.BackColor = SpaceDark;
            this.DoubleBuffered = true;

            // ---------- Панель вкладок ----------
            tabsHeaderPanel = new FlowLayoutPanel()
            {
                Height = 44,
                Dock = DockStyle.Top,
                BackColor = PanelDark,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(8, 4, 8, 0)
            };
            tabsHeaderPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, tabsHeaderPanel.Height),
                    Color.FromArgb(14, 28, 52), Color.FromArgb(8, 17, 34));
                g.FillRectangle(brush, tabsHeaderPanel.ClientRectangle);
                using var pen = new Pen(BorderCol, 1);
                g.DrawLine(pen, 0, tabsHeaderPanel.Height - 1, tabsHeaderPanel.Width, tabsHeaderPanel.Height - 1);
            };

            btnNewTab = CreateIconButton(IcoAdd, 28);
            btnNewTab.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            btnNewTab.Click += (s, e) => AddNewTab(HomePageUrl);
            tabsHeaderPanel.Controls.Add(btnNewTab);

            // ---------- Навигационная панель ----------
            navPanel = CreateGlassPanel();
            navPanel.Height = 90;
            navPanel.Dock = DockStyle.Top;
            navPanel.Padding = new Padding(14, 8, 14, 8);
            navPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, navPanel.Height),
                    Color.FromArgb(10, 20, 40), Color.FromArgb(6, 13, 28));
                g.FillRectangle(brush, navPanel.ClientRectangle);
                using var pen = new Pen(BorderCol, 1);
                g.DrawLine(pen, 0, navPanel.Height - 1, navPanel.Width, navPanel.Height - 1);
            };

            // ---- Верхний ряд: кнопки навигации ----
            int topY = 12;
            int iconSize = 36;
            int x = 14;

            btnRefresh = CreateNavButton(IcoRefresh, iconSize);
            btnRefresh.Left = x; btnRefresh.Top = topY; x += iconSize + 4;
            btnRefresh.Click += (s, e) => GetCurrentWebView()?.Reload();

            btnHome = CreateNavButton(IcoHome, iconSize);
            btnHome.Left = x; btnHome.Top = topY; x += iconSize + 4;
            btnHome.Click += (s, e) => NavigateToUrl(HomePageUrl);

            btnHistory = CreateNavButton(IcoHistory, iconSize);
            btnHistory.Left = x; btnHistory.Top = topY; x += iconSize + 4;
            btnHistory.Click += (s, e) => OpenHistoryTab();

            btnBookmarks = CreateNavButton(IcoStar, iconSize);
            btnBookmarks.Left = x; btnBookmarks.Top = topY; x += iconSize + 4;
            btnBookmarks.Click += (s, e) => OpenBookmarksTab();

            btnDownloads = CreateNavButton(IcoDownload, iconSize);
            btnDownloads.Left = x; btnDownloads.Top = topY; x += iconSize + 4;
            btnDownloads.Click += (s, e) => OpenDownloadManager();

            btnAddBookmark = CreateNavButton(IcoStar, iconSize);
            btnAddBookmark.Left = x; btnAddBookmark.Top = topY; x += iconSize + 4;
            btnAddBookmark.Click += (s, e) => ToggleBookmark();

            // Разделитель
            Panel sep1 = CreateSeparator();
            sep1.Left = x + 6;
            sep1.Top = topY + 6;

            // Адресная строка
            txtUrl = new TextBox()
            {
                Height = 32,
                Left = sep1.Left + 14,
                Top = topY + 2,
                AutoSize = false,
                BackColor = Color.FromArgb(10, 18, 34),
                ForeColor = TextMain,
                Font = new Font("Segoe UI", 11F),
                BorderStyle = BorderStyle.None,
                Padding = new Padding(12, 0, 12, 0),
                Cursor = Cursors.IBeam
            };
            txtUrl.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(txtUrl.Focused ? Accent : BorderCol, 1.5f);
                g.DrawPath(pen, GetRoundRectPath(txtUrl.ClientRectangle, 12));
            };
            txtUrl.GotFocus += (s, e) => txtUrl.Invalidate();
            txtUrl.LostFocus += (s, e) => txtUrl.Invalidate();
            txtUrl.KeyDown += TxtUrl_KeyDown;
            txtUrl.GotFocus += TxtUrl_GotFocus;
            txtUrl.LostFocus += TxtUrl_LostFocus;

            // Кнопка Перейти
            btnGo = new Button()
            {
                Text = "Перейти",
                Height = 32,
                Width = 86,
                Top = topY + 2,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            bool goHover = false;
            bool goPressed = false;
            btnGo.FlatStyle = FlatStyle.Flat;
            btnGo.FlatAppearance.BorderSize = 0;
            btnGo.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btnGo.FlatAppearance.MouseDownBackColor = Color.Transparent;
            btnGo.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = btnGo.ClientRectangle;
                var path = GetRoundRectPath(rect, 10);
                Color top = goPressed ? AccentDim : goHover ? AccentHover : Accent;
                Color bottom = goPressed ? Accent : Color.FromArgb(38, 100, 185);
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, rect.Height), top, bottom))
                {
                    g.FillPath(brush, path);
                }
                using (var glow = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, Math.Max(2, rect.Height / 2)),
                    Color.FromArgb(70, 150, 230), Color.FromArgb(0, 0, 0, 0)))
                {
                    g.FillPath(glow, path);
                }
                using var pen = new Pen(goHover ? Color.FromArgb(150, 200, 255) : Color.FromArgb(90, 150, 220), 1);
                g.DrawPath(pen, path);

                DrawButtonText(g, btnGo, rect);
            };
            btnGo.MouseEnter += (s, e) => { goHover = true; btnGo.Invalidate(); };
            btnGo.MouseLeave += (s, e) => { goHover = false; btnGo.Invalidate(); };
            btnGo.MouseDown += (s, e) => { goPressed = true; btnGo.Invalidate(); };
            btnGo.MouseUp += (s, e) => { goPressed = false; btnGo.Invalidate(); };
            btnGo.Click += (s, e) => NavigateToUrl(txtUrl.Text);

            // ---- Нижний ряд: назад / вперёд (одного размера, поменьше) ----
            btnBack = CreateNavButton(IcoBack, 28);
            btnBack.Left = 14;
            btnBack.Top = 56;
            btnBack.Click += (s, e) => GetCurrentWebView()?.GoBack();

            btnForward = CreateNavButton(IcoForward, 28);
            btnForward.Left = 48;
            btnForward.Top = 56;
            btnForward.Click += (s, e) => GetCurrentWebView()?.GoForward();

            // Режим инкогнито
            btnIncognito = CreateNavButton(IcoIncognito, 28);
            btnIncognito.Left = 90;
            btnIncognito.Top = 56;
            btnIncognito.Click += async (s, e) =>
            {
                bool wasIncognito = incognitoMode;
                incognitoMode = !incognitoMode;
                btnIncognito.ForeColor = incognitoMode ? Accent : TextMuted;

                // При выходе из инкогнито — полностью удаляем временный профиль
                if (wasIncognito && !incognitoMode)
                    await ResetIncognitoProfileAsync();

                MessageBox.Show(
                    incognitoMode
                        ? "Режим инкогнито включён.\nИстория, закладки и cookies не сохраняются.\nОткройте новую вкладку — она будет приватной."
                        : "Режим инкогнито выключен.\nВсе данные инкогнито удалены.",
                    "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            // Блокировщик рекламы и трекеров
            btnBlocker = CreateNavButton(IcoShield, 28);
            btnBlocker.Left = 132;
            btnBlocker.Top = 56;
            btnBlocker.ForeColor = blocker.Enabled ? Accent : TextMuted;
            btnBlocker.Click += (s, e) => OpenBlockerSettings();

            // Пипетка: выбрать элемент рекламы на странице и добавить его домен в блокировщик
            btnPicker = CreateNavButton(IcoPicker, 28);
            btnPicker.Left = 174;
            btnPicker.Top = 56;
            btnPicker.Click += (s, e) => TogglePickerMode();

            // Настройки приложения
            btnSettings = CreateNavButton(IcoSettings, 28);
            btnSettings.Left = 216;
            btnSettings.Top = 56;
            btnSettings.Click += (s, e) => OpenAppSettings();

            // Расширения Chrome
            btnExtensions = CreateNavButton(IcoExtension, 28);
            btnExtensions.Left = 258;
            btnExtensions.Top = 56;
                        btnExtensions.Click += (s, e) =>
            {
                var wv = GetCurrentWebView();
                ExtensionsHost.Open(this, wv, ApplyDarkTitle, () =>
                {
                    foreach (Control c in contentPanel.Controls)
                    {
                        if (c is Panel p && p.Controls.Count > 0 && p.Controls[0] is WebView2 w)
                        {
                            try { w.CoreWebView2?.Reload(); } catch { }
                        }
                    }
                });
            };

            navPanel.Controls.Add(btnRefresh);
            navPanel.Controls.Add(btnHome);
            navPanel.Controls.Add(btnHistory);
            navPanel.Controls.Add(btnBookmarks);
            navPanel.Controls.Add(btnDownloads);
            navPanel.Controls.Add(btnAddBookmark);
            navPanel.Controls.Add(sep1);
            navPanel.Controls.Add(txtUrl);
            navPanel.Controls.Add(btnGo);
            navPanel.Controls.Add(btnBack);
            navPanel.Controls.Add(btnForward);
            navPanel.Controls.Add(btnIncognito);
            navPanel.Controls.Add(btnBlocker);
            navPanel.Controls.Add(btnPicker);
            navPanel.Controls.Add(btnSettings);
            navPanel.Controls.Add(btnExtensions);

            navPanel.Resize += (s, e) =>
            {
                btnGo.Left = navPanel.Width - btnGo.Width - 14;
                txtUrl.Width = btnGo.Left - txtUrl.Left - 10;
                UpdateAddressBarDisplay();
            };

            // ---------- Контент ----------
            contentPanel = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = SpaceDark
            };

            // Порядок добавления важен: сайт не перекрывает панели браузера.
            this.Controls.Add(contentPanel);
            this.Controls.Add(navPanel);
            this.Controls.Add(tabsHeaderPanel);
        }

        // ---------- Кастомная панель заголовка в стиле Atlas ----------
        private void InitializeTitleBar()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowIcon = false;

            titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = Color.Transparent
            };
            titleBar.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, titleBar.Height),
                    Color.FromArgb(9, 18, 36), Color.FromArgb(5, 11, 24));
                g.FillRectangle(brush, titleBar.ClientRectangle);
                using var pen = new Pen(BorderCol, 1);
                g.DrawLine(pen, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1);

                // Иконка приложения 🌌
                try
                {
                    string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App.ico");
                    if (File.Exists(iconPath))
                    {
                        using var appIcon = new Icon(iconPath, 18, 18);
                        g.DrawIcon(appIcon, new Rectangle(10, 7, 18, 18));
                    }
                }
                catch (Exception ex) { Log("Title icon error", ex); }

                // Текст заголовка
                TextRenderer.DrawText(
                    g,
                    "Atlas — Cosmic Browser",
                    new Font("Segoe UI", 9.5F),
                    new Rectangle(36, 0, 300, titleBar.Height),
                    TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };

            btnWinMin = CreateWindowButton(IcoMin, false);
            btnWinMax = CreateWindowButton(IcoMax, false);
            btnWinClose = CreateWindowButton(IcoClose, true);

            btnWinMin.Click += (s, e) => { WindowState = FormWindowState.Minimized; };
            btnWinMax.Click += (s, e) =>
            {
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
                btnWinMax.Text = WindowState == FormWindowState.Maximized ? "\uE73E" : IcoMax;
            };
            btnWinClose.Click += (s, e) => { Close(); };

            btnWinClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnWinMax.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnWinMin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnWinClose.Left = titleBar.Width - 38;
            btnWinMax.Left = titleBar.Width - 76;
            btnWinMin.Left = titleBar.Width - 114;
            titleBar.Resize += (s, e) =>
            {
                btnWinClose.Left = titleBar.Width - 38;
                btnWinMax.Left = titleBar.Width - 76;
                btnWinMin.Left = titleBar.Width - 114;
            };

            titleBar.Controls.Add(btnWinMin);
            titleBar.Controls.Add(btnWinMax);
            titleBar.Controls.Add(btnWinClose);

            // Перетаскивание окна за панель заголовка и пустые области панелей
            Action<MouseEventArgs> drag = e =>
            {
                if (e.Button == MouseButtons.Left)
                    SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            };
            titleBar.MouseDown += (s, e) => drag(e);
            tabsHeaderPanel.MouseDown += (s, e) =>
            {
                if (tabsHeaderPanel.GetChildAtPoint(e.Location) == null) drag(e);
            };
            titleBar.DoubleClick += (s, e) => { btnWinMax.PerformClick(); };
            tabsHeaderPanel.DoubleClick += (s, e) => { btnWinMax.PerformClick(); };

            // Добавляем последним, чтобы панель была поверх остальных Dock-панелей
            this.Controls.Add(titleBar);
        }

        private Button CreateWindowButton(string text, bool isClose)
        {
            Button btn = new Button
            {
                Text = text,
                Width = 38,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                ForeColor = TextMuted,
                Font = new Font(IconFont, 12),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Padding = Padding.Empty
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = isClose
                ? Color.FromArgb(200, 45, 55)
                : Color.FromArgb(30, 50, 82);
            btn.FlatAppearance.MouseDownBackColor = isClose
                ? Color.FromArgb(160, 30, 40)
                : Color.FromArgb(45, 80, 130);
            btn.MouseEnter += (s, e) => btn.ForeColor = Color.White;
            btn.MouseLeave += (s, e) => btn.ForeColor = TextMuted;
            return btn;
        }

        // ---------- Горячие клавиши ----------
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl+T — новая вкладка
            if (keyData == (Keys.Control | Keys.T))
            {
                AddNewTab(HomePageUrl);
                return true;
            }
            // Ctrl+W — закрыть текущую вкладку
            if (keyData == (Keys.Control | Keys.W))
            {
                CloseCurrentTab();
                return true;
            }
            // Ctrl+Shift+T — восстановить закрытую вкладку
            if (keyData == (Keys.Control | Keys.Shift | Keys.T))
            {
                if (recentlyClosed.Count > 0)
                {
                    string url = recentlyClosed.Pop();
                    AddNewTab(url);
                    Log("Вкладка восстановлена: " + url);
                }
                else
                {
                    MessageBox.Show("Нет недавно закрытых вкладок.", "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return true;
            }
            // Ctrl+L — фокус на адресную строку
            if (keyData == (Keys.Control | Keys.L))
            {
                txtUrl.Focus();
                txtUrl.SelectAll();
                return true;
            }
            // Ctrl+R / F5 — обновить
            if (keyData == (Keys.Control | Keys.R) || keyData == Keys.F5)
            {
                GetCurrentWebView()?.Reload();
                return true;
            }
            // Alt+← — назад
            if (keyData == (Keys.Alt | Keys.Left))
            {
                GetCurrentWebView()?.GoBack();
                return true;
            }
            // Alt+→ — вперёд
            if (keyData == (Keys.Alt | Keys.Right))
            {
                GetCurrentWebView()?.GoForward();
                return true;
            }
            // Ctrl+F — поиск по странице
            if (keyData == (Keys.Control | Keys.F))
            {
                ToggleFindPanel();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Позволяет менять размер окна за края даже без системной рамки
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            // Удаляем временный инкогнито-профиль при выходе
            try
            {
                incognitoEnvironment = null;
                if (!string.IsNullOrEmpty(incognitoProfileDir) && Directory.Exists(incognitoProfileDir))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        try { Directory.Delete(incognitoProfileDir, true); break; }
                        catch { await Task.Delay(200); }
                    }
                }
            }
            catch (Exception ex) { Log("Incognito cleanup on exit error", ex); }
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            if (m.Msg == WM_NCHITTEST)
            {
                Point screen = new Point(m.LParam.ToInt32() & 0xFFFF, (m.LParam.ToInt32() >> 16) & 0xFFFF);
                Point p = PointToClient(screen);

                // Перетаскивание за панель заголовка (и за пустые места панели вкладок)
                if (p.Y >= 0 && p.Y < 34 && WindowState != FormWindowState.Maximized)
                {
                    m.Result = (IntPtr)0x0002; // HTCAPTION
                    return;
                }
                if (p.Y >= 34 && p.Y < 78 && WindowState != FormWindowState.Maximized)
                {
                    // область вкладок — разрешаем перетаскивание только если не попали в саму вкладку
                    Control hit = GetChildAtPoint(p);
                    if (hit == null || hit == tabsHeaderPanel)
                    {
                        m.Result = (IntPtr)0x0002;
                        return;
                    }
                }

                base.WndProc(ref m);
                if ((int)m.Result == 0x0001 && WindowState == FormWindowState.Normal) // HTCLIENT
                {
                    const int d = 7;
                    bool left = p.X <= d, right = p.X >= ClientSize.Width - d;
                    bool top = p.Y <= d, bottom = p.Y >= ClientSize.Height - d;

                    int ht = 0;
                    if (top && left) ht = 13;        // HTTOPLEFT
                    else if (top && right) ht = 14;  // HTTOPRIGHT
                    else if (bottom && left) ht = 16; // HTBOTTOMLEFT
                    else if (bottom && right) ht = 17;// HTBOTTOMRIGHT
                    else if (left) ht = 10;          // HTLEFT
                    else if (right) ht = 11;         // HTRIGHT
                    else if (top) ht = 12;           // HTTOP
                    else if (bottom) ht = 15;        // HTBOTTOM

                    if (ht != 0) m.Result = (IntPtr)ht;
                }
                return;
            }
            base.WndProc(ref m);
        }

        // рисует текст кнопки поверх собственного фона (Button.OnPaint рисует текст раньше Paint)
        private void DrawButtonText(Graphics g, Button btn, Rectangle rect)
        {
            TextRenderer.DrawText(
                g, btn.Text, btn.Font, rect, btn.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // единый стиль отрисовки кнопок: мягкий градиент + обводка + видимый текст
        private void StyleButton(Button btn, int radius)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.BackColor = Color.Transparent;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btn.FlatAppearance.MouseDownBackColor = Color.Transparent;
            btn.Cursor = Cursors.Hand;

            bool hovered = false;
            bool pressed = false;

            btn.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = btn.ClientRectangle;
                var path = GetRoundRectPath(rect, radius);

                // фон: сдержанный градиент в зависимости от состояния
                Color top = pressed ? Color.FromArgb(28, 64, 108)
                          : hovered ? Color.FromArgb(30, 56, 92)
                          : Color.FromArgb(20, 38, 66);
                Color bottom = pressed ? Color.FromArgb(20, 46, 84)
                             : hovered ? Color.FromArgb(18, 36, 64)
                             : Color.FromArgb(13, 26, 46);
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, rect.Height), top, bottom))
                {
                    g.FillPath(brush, path);
                }

                // тонкое внутреннее свечение сверху
                using (var glow = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, Math.Max(2, rect.Height / 2)),
                    hovered ? Color.FromArgb(55, 95, 150) : Color.FromArgb(28, 50, 82),
                    Color.FromArgb(0, 0, 0, 0)))
                {
                    g.FillPath(glow, path);
                }

                // аккуратная обводка
                using var pen = new Pen(hovered || pressed ? BorderHover : BorderCol, 1);
                g.DrawPath(pen, path);

                // текст/иконка — рисуем сами, чтобы не закрасить его градиентом
                DrawButtonText(g, btn, rect);
            };

            btn.MouseEnter += (s, e) => { hovered = true; btn.Invalidate(); };
            btn.MouseLeave += (s, e) => { hovered = false; btn.Invalidate(); };
            btn.MouseDown += (s, e) => { pressed = true; btn.Invalidate(); };
            btn.MouseUp += (s, e) => { pressed = false; btn.Invalidate(); };
        }

        private Button CreateIconButton(string text, int size = 34)
        {
            Button btn = new Button()
            {
                Text = text,
                Width = size,
                Height = size,
                ForeColor = TextMuted,
                Font = new Font(IconFont, 14),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            StyleButton(btn, 8);
            btn.MouseEnter += (s, e) => btn.ForeColor = TextMain;
            btn.MouseLeave += (s, e) => btn.ForeColor = TextMuted;
            btn.MouseDown += (s, e) => btn.ForeColor = Color.White;
            btn.MouseUp += (s, e) => btn.ForeColor = TextMain;
            return btn;
        }

        private Button CreateNavButton(string text, int size = 40)
        {
            Button btn = new Button()
            {
                Text = text,
                Width = size,
                Height = size,
                ForeColor = TextMuted,
                Font = new Font(IconFont, size <= 30 ? 13 : 16),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            StyleButton(btn, Math.Max(6, size / 4));
            btn.MouseEnter += (s, e) => btn.ForeColor = TextMain;
            btn.MouseLeave += (s, e) => btn.ForeColor = TextMuted;
            btn.MouseDown += (s, e) => btn.ForeColor = Color.White;
            btn.MouseUp += (s, e) => btn.ForeColor = TextMain;
            return btn;
        }

        private Panel CreateGlassPanel()
        {
            Panel p = new Panel { BackColor = Color.Transparent };
            p.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, p.Height),
                    Color.FromArgb(10, 20, 40), Color.FromArgb(6, 13, 28));
                g.FillRectangle(brush, p.ClientRectangle);
                using var pen = new Pen(BorderCol, 1);
                g.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1);
            };
            return p;
        }

        private Panel CreateSeparator()
        {
            Panel sep = new Panel
            {
                Width = 1,
                Height = 24,
                BackColor = BorderCol,
                Margin = new Padding(0, 6, 0, 6)
            };
            return sep;
        }

        private System.Drawing.Drawing2D.GraphicsPath GetRoundRectPath(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rect.Left, rect.Top, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.Right - radius * 2, rect.Top, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void TxtUrl_GotFocus(object sender, EventArgs e)
        {
            var webView = GetCurrentWebView();
            if (webView != null && webView.Source != null)
            {
                string url = webView.Source.ToString();
                if (!url.StartsWith("data:text/html"))
                {
                    txtUrl.Text = url;
                    txtUrl.SelectAll();
                }
            }
        }

        private void TxtUrl_LostFocus(object sender, EventArgs e)
        {
            UpdateAddressBarDisplay();
        }

        private void UpdateAddressBarDisplay()
        {
            var webView = GetCurrentWebView();
            if (webView != null && webView.Source != null)
            {
                string url = webView.Source.ToString();
                string display;
                if (url.StartsWith("data:text/html"))
                {
                    display = url.Contains("Закладки") ? "atlas://bookmarks" : "atlas://history";
                }
                else
                {
                    display = url;
                }
                txtUrl.Text = UrlHelper.Elide(display, txtUrl);
            }
        }

        // Безопасное экранирование для вставки данных в HTML
        private static string H(string value) => WebUtility.HtmlEncode(value ?? "");

        // Одноразовый токен для команды очистки истории — защита от подделки сообщений сайтами
        private readonly string historyClearToken = Guid.NewGuid().ToString("N");

        private void ToggleBookmark()
        {
            if (incognitoMode)
            {
                MessageBox.Show("В режиме инкогнито закладки не сохраняются.", "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var webView = GetCurrentWebView();
            if (webView != null && webView.Source != null)
            {
                string url = webView.Source.ToString();
                if (!url.StartsWith("data:text/html"))
                {
                    var existing = bookmarks.Find(b => b.Url == url);
                    if (existing == null)
                    {
                        string title = activeBrowserContainer?.Tag?.ToString() ?? url;
                        bookmarks.Add(new BookmarkItem { Url = url, Title = title });
                    }
                    else
                    {
                        bookmarks.Remove(existing);
                    }
                    SaveBookmarks();
                    UpdateBookmarkIcon();
                }
            }
        }

        private void UpdateBookmarkIcon()
        {
            var webView = GetCurrentWebView();
            if (webView != null && webView.Source != null)
            {
                string url = webView.Source.ToString();
                if (url.StartsWith("data:text/html"))
                {
                    btnAddBookmark.Text = IcoStar;
                    btnAddBookmark.ForeColor = Color.FromArgb(170, 170, 180);
                    return;
                }

                if (bookmarks.Exists(b => b.Url == url))
                {
                    btnAddBookmark.Text = IcoStarFill;
                    btnAddBookmark.ForeColor = Color.FromArgb(234, 179, 8);
                }
                else
                {
                    btnAddBookmark.Text = IcoStar;
                    btnAddBookmark.ForeColor = Color.FromArgb(170, 170, 180);
                }
            }
            else
            {
                btnAddBookmark.Text = IcoStar;
                btnAddBookmark.ForeColor = Color.FromArgb(170, 170, 180);
            }
        }

        private void OpenBookmarksTab()
        {
            UpdateTabCount(bookmarks.Count);
            AddNewTab(PageTemplates.BuildBookmarksPage(bookmarks));
        }

        private void OpenHistoryTab()
        {
            UpdateTabCount(visitHistory.Count);
            AddNewTab(PageTemplates.BuildHistoryPage(visitHistory, historyClearToken));
        }

        private void UpdateTabCount(int count)
        {
            for (int i = 0; i < tabsHeaderPanel.Controls.Count; i++)
            {
                if (tabsHeaderPanel.Controls[i] is Label label)
                {
                    label.Text = count.ToString();
                    break;
                }
            }
        }


        private void OpenDownloadManager()
        {
            if (downloadManagerForm == null || downloadManagerForm.IsDisposed)
            {
                downloadManagerForm = new DownloadManagerForm();
                downloadManagerForm.FormClosed += (s, e) => downloadManagerForm = null;
                downloadManagerForm.OpenFolderBtn.Click += (s, e) => OpenDownloadsFolder();
                downloadManagerForm.ClearBtn.Click += (s, e) => ClearDownloads();
                downloadManagerForm.ApplyDarkHeader(ApplyDarkTitle);
            }
            RefreshDownloadManager();
            if (!downloadManagerForm.Visible) downloadManagerForm.Show(this);
            else { downloadManagerForm.WindowState = FormWindowState.Normal; downloadManagerForm.BringToFront(); }
        }

        private void OpenBlockerSettings()
        {
            using var form = new BlockerSettingsForm(blocker, sessionBlockedCount);
            form.ApplyDarkHeader(ApplyDarkTitle);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                blocker.Enabled = form.EnableCheck.Checked;
                blocker.Categories = form.WorkingCategories;

                // Пересобираем плоский список для обратной совместимости
                blocker.BlockedDomains = new List<string>();
                foreach (var cat in blocker.Categories)
                    foreach (var d in cat.Domains)
                        if (!blocker.BlockedDomains.Contains(d))
                            blocker.BlockedDomains.Add(d);

                SaveBlockerSettings();
                btnBlocker.ForeColor = blocker.Enabled ? Accent : TextMuted;
            }
        }

        // ---------- Пипетка: выбрать рекламный элемент на странице ----------
        private void TogglePickerMode()
        {
            SetPickerMode(!pickerMode);
        }

        private void SetPickerMode(bool enabled)
        {
            pickerMode = enabled;
            btnPicker.ForeColor = enabled ? Accent : TextMuted;
            btnPicker.BackColor = enabled ? AccentDim : Surface;

            var webView = GetCurrentWebView();
            if (webView == null) return;

            if (enabled)
            {
                MessageBox.Show(
                    "Пипетка включена.\nКликните по рекламному элементу на странице — его домен будет добавлен в блокировщик.\nНажмите кнопку пипетки ещё раз для отмены.",
                    "Atlas — Пипетка", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // внедряем/удаляем JS-перехватчик кликов
            string script = enabled ? BuildPickerScript() : "window.__atlasPickerOff && window.__atlasPickerOff();";
            try { webView.ExecuteScriptAsync(script); }
            catch (Exception ex) { Log("Picker script error", ex); }
        }

        private string BuildPickerScript()
        {
            return @"
(function(){
  if (window.__atlasPickerOn) return;
  window.__atlasPickerOn = true;
  function send(msg){ try{ window.chrome.webview.postMessage(msg); }catch(e){} }
  function extractHost(url){
    try { var u = new URL(url, location.href); return u.hostname.replace(/^www\./,''); }
    catch(e){ return ''; }
  }
  function onClick(e){
    if (!window.__atlasPickerOn) return;
    e.preventDefault();
    e.stopPropagation();
    var el = e.target;
    // ищем ближайший элемент со ссылкой или картинкой
    var node = el;
    var found = '';
    var depth = 0;
    while (node && depth < 12) {
      if (node.href) { found = extractHost(node.href); if (found) break; }
      if (node.src) { found = extractHost(node.src); if (found) break; }
      if (node.getAttribute && node.getAttribute('data-src')) { found = extractHost(node.getAttribute('data-src')); if (found) break; }
      node = node.parentElement; depth++;
    }
    // ищем IFRAME (часто реклама внутри фрейма)
    if (!found) {
      var frames = document.getElementsByTagName('iframe');
      for (var i=0;i<frames.length;i++){
        var r = frames[i].getBoundingClientRect();
        if (r.left <= e.clientX && r.right >= e.clientX && r.top <= e.clientY && r.bottom >= e.clientY){
          found = extractHost(frames[i].src); if (found) break;
        }
      }
    }
    if (!found) found = extractHost(location.href);
    // строим CSS-селектор для косметического скрытия элемента
    function cssPath(el){
      if(!el || el.tagName==='BODY') return 'body';
      var parts=[];
      while(el && el.tagName!=='BODY' && parts.length<5){
        var s=el.tagName.toLowerCase();
        if(el.id){ s+='#'+el.id; parts.unshift(s); break; }
        if(el.className && typeof el.className==='string'){
          var c=el.className.trim().split(/\s+/)[0];
          if(c) s+='.'+c;
        }
        parts.unshift(s); el=el.parentElement;
      }
      return parts.join(' > ');
    }
    var selector = cssPath(e.target);
    window.__atlasPickerOff();
    if (found) send('picker:' + found + '|' + selector); else send('picker:cancel');
  }
  function onKey(e){ if (e.key === 'Escape'){ window.__atlasPickerOff(); send('picker:cancel'); } }
  window.__atlasPickerOff = function(){
    window.__atlasPickerOn = false;
    document.removeEventListener('click', onClick, true);
    document.removeEventListener('keydown', onKey, true);
    var h = document.getElementById('__atlasPickerHighlight');
    if (h) h.remove();
    document.body.style.cursor = '';
  };
  document.addEventListener('click', onClick, true);
  document.addEventListener('keydown', onKey, true);
  document.body.style.cursor = 'crosshair';
  // подсветка при наведении
  var hl = document.createElement('div');
  hl.id = '__atlasPickerHighlight';
  hl.style.cssText = 'position:fixed;pointer-events:none;border:2px solid #388bfd;background:rgba(56,139,253,.18);z-index:2147483647;display:none;';
  document.body.appendChild(hl);
  document.addEventListener('mousemove', function move(e){
    if (!window.__atlasPickerOn){ document.removeEventListener('mousemove', move, true); return; }
    var el = document.elementFromPoint(e.clientX, e.clientY);
    if (!el) return;
    var r = el.getBoundingClientRect();
    hl.style.display='block';
    hl.style.left=r.left+'px'; hl.style.top=r.top+'px';
    hl.style.width=r.width+'px'; hl.style.height=r.height+'px';
  }, true);
})();";
        }

        private void AddBlockedDomainFromPicker(string domain)
        {
            domain = domain.Trim().ToLowerInvariant().Replace("www.", "");
            if (string.IsNullOrEmpty(domain)) return;

            // Ищем категорию «Пользовательские правила» или создаём её
            var custom = blocker.Categories.FirstOrDefault(c => c.Name == "Пользовательские правила (пипетка)");
            if (custom == null)
            {
                custom = new BlockerCategory("Пользовательские правила (пипетка)", true);
                blocker.Categories.Add(custom);
            }

            bool exists = custom.Domains.Any(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase));
            if (exists)
            {
                MessageBox.Show("Домен уже в блокировщике:\n" + domain, "Atlas — Пипетка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            custom.Domains.Add(domain);
            if (!blocker.BlockedDomains.Contains(domain))
                blocker.BlockedDomains.Add(domain);
            SaveBlockerSettings();

            MessageBox.Show("Добавлено в блокировщик:\n" + domain, "Atlas — Пипетка", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateDownloadEntry(DownloadEntry entry)
        {
            if (entry.Operation != null)
            {
                entry.Received = (long)entry.Operation.BytesReceived;
                entry.Total = (long)entry.Operation.TotalBytesToReceive;
                switch (entry.Operation.State)
                {
                    case CoreWebView2DownloadState.Completed: entry.Status = "Завершено"; break;
                    case CoreWebView2DownloadState.Interrupted: entry.Status = "Прервано"; break;
                    default: entry.Status = "Загрузка"; break;
                }
            }

            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(new Action(() => RefreshDownloadManager()));
                else RefreshDownloadManager();
            }
            catch (Exception ex)
            {
                string errorMessage = "";
                if (ex.Message == null || ex.Message == "")
                    errorMessage = "Произошла неизвестная ошибка в системе обновлений загрузки.";
                else
                {
                    if (ex.Message.Contains("NoRender") || ex.Message.Contains("render"))
                        errorMessage = "Не удалось загрузить браузер. Проверьте установку Microsoft Edge и перезапустите приложение.";
                    else if (ex.Message.Contains("failed") || ex.Message.Contains("Failed"))
                        errorMessage = "Ошибка загрузки браузера.\nПожалуйста, проверьте системы безопасности Windows.";
                    else
                        errorMessage = $"Произошла ошибка: {ex.Message}";
                }

                MessageBox.Show(errorMessage, "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshDownloadManager()
        {
            if (downloadManagerForm == null || downloadManagerForm.IsDisposed) return;

            FlowLayoutPanel cards = downloadManagerForm.CardsPanel;
            cards.SuspendLayout();
            cards.Controls.Clear();

            int active = 0;
            foreach (DownloadEntry entry in downloads)
            {
                if (entry.Status == "Загрузка") active++;
                cards.Controls.Add(CreateDownloadCard(entry));
            }

            downloadManagerForm.CountLabel.Text = downloads.Count + " файлов  •  " + active + " активных";
            downloadManagerForm.EmptyLabel.Visible = downloads.Count == 0;
            cards.Visible = downloads.Count > 0;
            cards.ResumeLayout();
        }

        private Control CreateDownloadCard(DownloadEntry entry)
        {
            Panel card = new Panel
            {
                Width = Math.Max(320, downloadManagerForm.CardsPanel.ClientSize.Width - 30),
                Height = 148,
                Margin = new Padding(4, 4, 4, 14),
                BackColor = Surface,
                Padding = new Padding(16)
            };
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(BorderCol, 1);
                g.DrawPath(pen, GetRoundRectPath(card.ClientRectangle, 12));
            };

            Label fileIcon = new Label { Text = "↓", Location = new Point(16, 18), Size = new Size(44, 44), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(20, 44, 80), ForeColor = Accent, Font = new Font("Segoe UI", 20F, FontStyle.Bold) };
            fileIcon.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(BorderCol, 1);
                g.DrawPath(pen, GetRoundRectPath(fileIcon.ClientRectangle, 10));
            };
            Label fileName = new Label { Text = entry.FileName, Location = new Point(72, 16), Size = new Size(Math.Max(200, card.Width - 330), 26), AutoEllipsis = true, ForeColor = TextMain, Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold) };
            Label filePath = new Label { Text = entry.FilePath, Location = new Point(73, 46), Size = new Size(Math.Max(200, card.Width - 330), 20), AutoEllipsis = true, ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5F) };

            int percent = entry.Total > 0 ? (int)Math.Min(100, entry.Received * 100.0 / entry.Total) : (entry.Status == "Завершено" ? 100 : 0);
            string progressText = entry.Status == "Завершено" ? "Загрузка завершена" : entry.Status == "Прервано" ? "Загрузка прервана" : (entry.Total > 0 ? FormatBytes(entry.Received) + " из " + FormatBytes(entry.Total) : FormatBytes(entry.Received));
            Color statusColor = entry.Status == "Завершено" ? Color.FromArgb(91, 213, 157) : entry.Status == "Прервано" ? Color.FromArgb(255, 110, 110) : Accent;
            Label status = new Label { Text = entry.Status, Location = new Point(73, 76), AutoSize = true, ForeColor = statusColor, Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold) };
            Label progress = new Label { Text = progressText, Location = new Point(73, 100), AutoSize = true, ForeColor = TextMuted, Font = new Font("Segoe UI", 8F) };

            Panel track = new Panel { Location = new Point(73, 125), Size = new Size(Math.Max(130, card.Width - 340), 5), BackColor = Color.FromArgb(24, 40, 66) };
            Panel fill = new Panel { Location = Point.Empty, Size = new Size((int)(track.Width * percent / 100.0), 5), BackColor = entry.Status == "Прервано" ? Color.FromArgb(255, 110, 110) : Accent };
            track.Controls.Add(fill);

            Button openButton = CreateDownloadActionButton("Открыть", 0);
            Button folderButton = CreateDownloadActionButton("Папка", 1);
            Button removeButton = CreateDownloadActionButton("Убрать", 2);
            Button pauseButton = CreateDownloadActionButton(IcoPause, 1);
            int right = card.Width - 16;
            removeButton.Location = new Point(right - removeButton.Width, 94);
            folderButton.Location = new Point(right - removeButton.Width - folderButton.Width - 7, 94);
            pauseButton.Location = new Point(right - removeButton.Width - folderButton.Width - pauseButton.Width - 14, 94);
            openButton.Location = new Point(right - removeButton.Width - folderButton.Width - pauseButton.Width - openButton.Width - 21, 94);
            pauseButton.Click += (s, e) => ToggleDownloadPause(entry);

            openButton.Click += (s, e) =>
            {
                try
                {
                    if (File.Exists(entry.FilePath)) Process.Start(new ProcessStartInfo(entry.FilePath) { UseShellExecute = true });
                    else MessageBox.Show("Файл ещё не найден или был перемещён.", "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show("Не удалось открыть файл: " + ex.Message, "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            folderButton.Click += (s, e) =>
            {
                try
                {
                    string directory = Path.GetDirectoryName(entry.FilePath);
                    if (File.Exists(entry.FilePath)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + entry.FilePath + "\"") { UseShellExecute = true });
                    else if (Directory.Exists(directory)) Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
                    else MessageBox.Show("Папка не найдена.", "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show("Не удалось открыть папку: " + ex.Message, "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            removeButton.Click += (s, e) => { downloads.Remove(entry); RefreshDownloadManager(); };

            card.Controls.Add(fileIcon);
            card.Controls.Add(fileName);
            card.Controls.Add(filePath);
            card.Controls.Add(status);
            card.Controls.Add(progress);
            card.Controls.Add(track);
            card.Controls.Add(openButton);
            card.Controls.Add(folderButton);
            card.Controls.Add(pauseButton);
            card.Controls.Add(removeButton);
            return card;
        }

        private Button CreateDownloadActionButton(string text, int kind)
        {
            bool primary = kind == 0;
            Button button = new Button
            {
                Text = text,
                Size = new Size(primary ? 82 : 70, 30),
                ForeColor = primary ? Color.White : TextMain,
                Font = text == IcoPause ? new Font(IconFont, 12) : new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = Padding.Empty
            };
            bool hovered = false;
            bool pressed = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.Transparent;
            button.FlatAppearance.MouseDownBackColor = Color.Transparent;
            button.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = button.ClientRectangle;
                var path = GetRoundRectPath(rect, 8);
                Color top, bottom, border;
                if (primary)
                {
                    top = pressed ? AccentDim : hovered ? AccentHover : Accent;
                    bottom = pressed ? Accent : Color.FromArgb(38, 100, 185);
                    border = Color.FromArgb(90, 150, 220);
                }
                else
                {
                    top = pressed ? AccentDim : hovered ? SurfaceAlt : Surface;
                    bottom = pressed ? Color.FromArgb(22, 52, 92) : hovered ? Color.FromArgb(18, 38, 68) : Color.FromArgb(9, 18, 34);
                    border = hovered ? BorderHover : BorderCol;
                }
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, rect.Height), top, bottom))
                {
                    g.FillPath(brush, path);
                }
                using (var glow = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, Math.Max(2, rect.Height / 2)),
                    primary ? Color.FromArgb(70, 150, 230) : Color.FromArgb(40, 70, 110),
                    Color.FromArgb(0, 0, 0, 0)))
                {
                    g.FillPath(glow, path);
                }
                using var pen = new Pen(border, 1);
                g.DrawPath(pen, path);

                DrawButtonText(g, button, rect);
            };
            button.MouseEnter += (s, e) => { hovered = true; button.ForeColor = Color.White; button.Invalidate(); };
            button.MouseLeave += (s, e) => { hovered = false; button.ForeColor = primary ? Color.White : TextMain; button.Invalidate(); };
            button.MouseDown += (s, e) => { pressed = true; button.Invalidate(); };
            button.MouseUp += (s, e) => { pressed = false; button.Invalidate(); };
            return button;
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " Б";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " КБ";
            if (bytes < 1024L * 1024L * 1024L) return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " МБ";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00") + " ГБ";
        }

        // ---------- Поиск по странице (Ctrl+F) ----------
        private void ToggleFindPanel()
        {
            if (findPanel == null)
            {
                findPanel = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 44,
                    BackColor = Color.FromArgb(9, 18, 36),
                    Padding = new Padding(12, 8, 12, 8),
                    Visible = false
                };
                findBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(15, 29, 52),
                    ForeColor = TextMain,
                    Font = new Font("Segoe UI", 11F),
                    BorderStyle = BorderStyle.FixedSingle,
                    PlaceholderText = "Поиск на странице..."
                };
                findBox.TextChanged += (s, e) =>
                {
                    var wv = GetCurrentWebView();
                    if (wv == null) return;
                    string q = findBox.Text.Replace("'", "\\'");
                    if (string.IsNullOrEmpty(findBox.Text))
                    {
                        wv.ExecuteScriptAsync("window.__atlasClearFind && window.__atlasClearFind();");
                        return;
                    }
                    string script = $@"
(function(){{
  if (window.__atlasClearFind) window.__atlasClearFind();
  var sel = window.getSelection(); sel.removeAllRanges();
  var query = '{q}'.toLowerCase();
  var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null, false);
  var node; var count = 0;
  while (node = walker.nextNode()) {{
    var idx = node.nodeValue.toLowerCase().indexOf(query);
    if (idx >= 0) {{
      var r = document.createRange();
      r.setStart(node, idx); r.setEnd(node, idx + query.length);
      sel.addRange(r); count++;
    }}
  }}
  window.__atlasClearFind = function(){{ window.getSelection().removeAllRanges(); }};
  return count;
}})();";
                    try { wv.ExecuteScriptAsync(script); } catch (Exception ex) { Log("Find script error", ex); }
                };
                findBox.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape)
                    {
                        SetFindPanel(false);
                        e.Handled = true;
                    }
                    else if (e.KeyCode == Keys.Enter)
                    {
                        var wv = GetCurrentWebView();
                        if (wv != null && !string.IsNullOrEmpty(findBox.Text))
                        {
                            string q = findBox.Text.Replace("'", "\\'");
                            wv.ExecuteScriptAsync($@"(function(){{var m=document.getSelection();if(m.rangeCount>0){{m.getRangeAt(0).startContainer.parentElement.scrollIntoView({{block:'center'}});}}}})();");
                        }
                        e.Handled = true;
                    }
                };
                findPanel.Controls.Add(findBox);
                this.Controls.Add(findPanel);
                findPanel.BringToFront();
            }
            SetFindPanel(!findPanel.Visible);
        }

        private void SetFindPanel(bool visible)
        {
            if (findPanel == null) return;
            findPanel.Visible = visible;
            if (visible)
            {
                findBox.Focus();
                findBox.SelectAll();
            }
            else
            {
                var wv = GetCurrentWebView();
                try { wv?.ExecuteScriptAsync("window.__atlasClearFind && window.__atlasClearFind();"); } catch (Exception ex) { Log("Clear find error", ex); }
            }
        }

        private void CloseCurrentTab()
        {
            if (activeBrowserContainer == null || activeTabContainer == null) return;
            WebView2 webView = activeBrowserContainer.Controls.Count > 0
                ? activeBrowserContainer.Controls[0] as WebView2 : null;
            if (webView == null) return;
            CloseTab(activeTabContainer, activeBrowserContainer, webView);
        }

        private void CloseTab(Panel tabContainer, Panel browserContainer, WebView2 webView)
        {
            // Запоминаем адрес вкладки для восстановления (кроме домашней страницы)
            try
            {
                if (webView.CoreWebView2?.Source != null)
                {
                    string url = webView.CoreWebView2.Source;
                    if (!url.StartsWith("data:text/html") && !url.StartsWith("about:"))
                        recentlyClosed.Push(url);
                }
            }
            catch { }

            int tabCount = 0;
            foreach (Control ctrl in tabsHeaderPanel.Controls)
            {
                if (ctrl is Panel) tabCount++;
            }

            if (tabCount <= 1)
            {
                AddNewTab(HomePageUrl);
            }

            if (activeBrowserContainer == browserContainer)
            {
                int idx = tabsHeaderPanel.Controls.GetChildIndex(tabContainer);
                for (int i = idx - 1; i >= 0; i--)
                {
                    if (tabsHeaderPanel.Controls[i] is Panel p)
                    {
                        foreach (Control sub in p.Controls)
                        {
                            if (sub is Button b && b.Text != IcoClose) { b.PerformClick(); break; }
                        }
                        break;
                    }
                }
            }

            tabsHeaderPanel.Controls.Remove(tabContainer);
            contentPanel.Controls.Remove(browserContainer);
            webView.Dispose();
            tabContainer.Dispose();
            browserContainer.Dispose();
        }

        // Закрыть все вкладки, кроме указанной
        private void CloseOtherTabs(Panel keepTab)
        {
            foreach (var ctrl in tabsHeaderPanel.Controls.Cast<Control>().ToList())
            {
                if (ctrl is Panel p && p != keepTab)
                    CloseTabByContainer(p);
            }
        }

        // Закрыть вкладки справа от указанной
        private void CloseTabsToRight(Panel fromTab)
        {
            int idx = tabsHeaderPanel.Controls.GetChildIndex(fromTab);
            foreach (var ctrl in tabsHeaderPanel.Controls.Cast<Control>().Skip(idx + 1).ToList())
            {
                if (ctrl is Panel p)
                    CloseTabByContainer(p);
            }
        }

        private void CloseTabByContainer(Panel tabPanel)
        {
            if (tabPanel.Tag is Panel bc && bc.Controls.Count > 0 && bc.Controls[0] is WebView2 wv)
                CloseTab(tabPanel, bc, wv);
        }

        private void SwitchTab(Panel tabContainer, Panel browserContainer, WebView2 webView)
        {
            activeTabContainer = tabContainer;
            foreach (Control ctrl in contentPanel.Controls)
            {
                if (ctrl is Panel p) p.Visible = false;
            }

            foreach (Control ctrl in tabsHeaderPanel.Controls)
            {
                if (ctrl is Panel p)
                {
                    p.Invalidate();
                    foreach (Control subCtrl in p.Controls)
                    {
                        if (subCtrl is Button btn && btn.Text != IcoClose)
                        {
                            btn.ForeColor = TextMuted;
                            btn.BackColor = Color.Transparent;
                        }
                    }
                }
            }

            browserContainer.Visible = true;
            tabContainer.Invalidate();
            foreach (Control subCtrl in tabContainer.Controls)
            {
                if (subCtrl is Button btn && btn.Text != IcoClose)
                {
                    btn.ForeColor = TextMain;
                    btn.BackColor = Color.Transparent;
                }
            }

            activeBrowserContainer = browserContainer;

            if (webView.CoreWebView2 != null && webView.Source != null)
            {
                UpdateAddressBarDisplay();
                UpdateBookmarkIcon();
            }
            else
            {
                txtUrl.Text = string.Empty;
                UpdateBookmarkIcon();
            }
        }

        private CoreWebView2 GetCurrentWebView()
        {
            if (activeBrowserContainer != null && activeBrowserContainer.Controls.Count > 0)
            {
                if (activeBrowserContainer.Controls[0] is WebView2 webView)
                {
                    return webView.CoreWebView2;
                }
            }
            return null;
        }

        // УМНАЯ АДРЕСНАЯ СТРОКА (ПОИСК ИЛИ ПРЯМОЙ URL)
        private void NavigateToUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            input = input.Trim();

            if (input == "atlas://history")
            {
                OpenHistoryTab();
                return;
            }
            if (input == "atlas://bookmarks")
            {
                OpenBookmarksTab();
                return;
            }

            string targetUrl;

            // Проверяем, является ли ввод полноценным URL или доменом (содержит точку, нет пробелов, либо начинается с протокола)
            if (input.StartsWith("http://") || input.StartsWith("https://"))
            {
                targetUrl = input;
            }
            else if (input.Contains(".") && !input.Contains(" "))
            {
                targetUrl = "https://" + input;
            }
            else
            {
                // Поисковый запрос — используем выбранную поисковую систему
                targetUrl = UrlHelper.BuildSearchUrl(appSettings.SearchEngine, input);
            }

            var currentWebView = GetCurrentWebView();
            currentWebView?.Navigate(targetUrl);
        }

        // Получает подсказки от Google Suggest и отправляет их на домашнюю страницу
        private async Task FetchSuggestionsAsync(WebView2 webView, string query)
        {
            try
            {
                string url = "https://suggestqueries.google.com/complete/search?client=chrome&hl=ru&q=" + Uri.EscapeDataString(query);
                string json = await Http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var list = new List<string>();
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 1)
                {
                    var arr = doc.RootElement[1];
                    foreach (var item in arr.EnumerateArray())
                    {
                        string s = item.GetString();
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                }

                // Передаём подсказки в JS страницы
                string jsArray = "[" + string.Join(",", list.Select(s => "\"" +
                    s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"")) + "]";
                await webView.ExecuteScriptAsync("window.__atlasShowSuggestions && window.__atlasShowSuggestions(" + jsArray + ");");
            }
            catch (Exception ex)
            {
                Log("FetchSuggestions error", ex);
            }
        }

        private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                NavigateToUrl(txtUrl.Text);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        // ==================== ВЗАИМОДЕЙСТВИЕ С ВКЛАДКАМИ ====================
        // ==================== ПОЛНОЭКРАННОЕ ВИДЕО ====================
        private void SetVideoFullScreen(bool enter)
        {
            if (enter == videoFullScreen) return;
            videoFullScreen = enter;

            if (enter)
            {
                prevWindowState = WindowState;
                prevBorderStyle = FormBorderStyle;
                prevMaximizeBox = MaximizeBox;
                prevMinimizeBox = MinimizeBox;

                // Скрываем панели браузера — остаётся только видео
                titleBar.Visible = false;
                tabsHeaderPanel.Visible = false;
                navPanel.Visible = false;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                titleBar.Visible = true;
                tabsHeaderPanel.Visible = true;
                navPanel.Visible = true;
                WindowState = prevWindowState;
                FormBorderStyle = prevBorderStyle == FormBorderStyle.None ? FormBorderStyle.Sizable : prevBorderStyle;
            }
        }

        // Панель навигации хранится в поле (нужна для fullscreen)
        private Panel navPanel;
        private void TogglePinTab(Panel tab, Button label)
        {
            bool pinned = pinnedTabs.ContainsKey(tab) && pinnedTabs[tab];
            pinnedTabs[tab] = !pinned;
            tab.Width = !pinned ? 44 : 180;
            label.Width = !pinned ? 16 : 140;
            label.Text = !pinned ? "📌" : label.Text.StartsWith("📌") ? label.Text.Substring(4) : label.Text;
            tab.Invalidate();
        }

        // ==================== КОНТЕКСТНОЕ МЕНЮ WEBVIEW2 ====================
        // Последний URL, на котором был произведён правый клик (заполняется JS-перехватчиком)
        private string lastContextUrl = "";

        private void AttachContextMenu(WebView2 webView)
        {
            try { webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false; }
            catch (Exception ex) { Log("Context menu disable error", ex); }

            var menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(10, 22, 42);
            menu.ForeColor = TextMain;

            var openNew = new ToolStripMenuItem("Открыть в новой вкладке");
            openNew.Click += (s, e) =>
            {
                string url = !string.IsNullOrEmpty(lastContextUrl) ? lastContextUrl : webView.Source?.ToString();
                if (!string.IsNullOrEmpty(url)) AddNewTab(url);
            };

            var copyUrl = new ToolStripMenuItem("Копировать адрес");
            copyUrl.Click += (s, e) =>
            {
                string url = !string.IsNullOrEmpty(lastContextUrl) ? lastContextUrl : webView.Source?.ToString();
                if (!string.IsNullOrEmpty(url))
                    try { Clipboard.SetText(url); Log("Адрес скопирован"); } catch (Exception ex) { Log("Clipboard error", ex); }
            };

            var addBm = new ToolStripMenuItem("Добавить в закладки");
            addBm.Click += (s, e) =>
            {
                string url = !string.IsNullOrEmpty(lastContextUrl) ? lastContextUrl : webView.Source?.ToString();
                if (string.IsNullOrEmpty(url)) return;
                if (bookmarks.Any(b => b.Url == url)) { Log("Уже в закладках"); return; }
                bookmarks.Add(new BookmarkItem { Url = url, Title = webView.CoreWebView2.DocumentTitle ?? url });
                SaveBookmarks();
                Log("Добавлено в закладки");
            };

            var blockEl = new ToolStripMenuItem("Заблокировать элемент (пипетка)");
            blockEl.Click += (s, e) => SetPickerMode(true);

            menu.Items.AddRange(new ToolStripItem[] { openNew, copyUrl, addBm, new ToolStripSeparator(), blockEl });

            // Перехватываем правый клик, чтобы узнать ссылку под курсором
            webView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                string js = @"document.addEventListener('contextmenu', function(e){
  var n = e.target;
  while(n && !n.href){ n = n.parentElement; }
  window.__atlasCtxUrl = n && n.href ? n.href : '';
}, true);";
                try { webView.CoreWebView2.ExecuteScriptAsync(js); } catch (Exception ex) { Log("Context menu JS error", ex); }
            };
            webView.MouseDown += async (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                try
                {
                    string res = await webView.ExecuteScriptAsync("window.__atlasCtxUrl || ''");
                    lastContextUrl = string.IsNullOrEmpty(res) || res == "undefined" ? "" : res.Trim('"');
                }
                catch { lastContextUrl = ""; }
                menu.Show(Cursor.Position);
            };
        }

        // ==================== ЗАГРУЗКИ: ПАУЗА/ОЧИСТКА ====================
        private void ClearDownloads()
        {
            foreach (var d in downloads)
            {
                try { d.Operation?.Cancel(); } catch (Exception ex) { Log("Download cancel error", ex); }
            }
            downloads.Clear();
            RefreshDownloadManager();
        }

        private void ToggleDownloadPause(DownloadEntry entry)
        {
            if (entry.Operation == null) return;
            try
            {
                if (entry.Status == "Загрузка")
                {
                    entry.Operation.Pause();
                    entry.Status = "Пауза";
                }
                else if (entry.Status == "Пауза")
                {
                    entry.Operation.Resume();
                    entry.Status = "Загрузка";
                }
                RefreshDownloadManager();
            }
            catch (Exception ex) { Log("ToggleDownloadPause error", ex); }
        }

        private void OpenDownloadsFolder()
        {
            try
            {
                string folder = string.IsNullOrEmpty(appSettings.DownloadFolder)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                    : appSettings.DownloadFolder;
                if (Directory.Exists(folder))
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex) { Log("OpenDownloadsFolder error", ex); }
        }

        // ==================== WHITELIST ====================
        private bool IsWhitelisted(string url)
        {
            try
            {
                var host = new Uri(url).Host.ToLowerInvariant().Replace("www.", "");
                var wl = blocker.Categories.FirstOrDefault(c => c.Name.StartsWith("Whitelist"));
                if (wl == null) return false;
                return wl.Domains.Any(d => host.Contains(d.ToLowerInvariant()));
            }
            catch { return false; }
        }

        // ==================== КОСМЕТИЧЕСКАЯ ФИЛЬТРАЦИЯ ====================
        private void ApplyCosmeticFilter(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector)) return;
            var wv = GetCurrentWebView();
            if (wv == null) return;
            string safe = selector.Replace("'", "\\'");
            try
            {
                wv.ExecuteScriptAsync($@"(function(){{
  var els = document.querySelectorAll('{safe}');
  for (var i=0;i<els.length;i++) els[i].style.display='none';
  window.__atlasCosmetic = window.__atlasCosmetic || [];
  window.__atlasCosmetic.push('{safe}');
}})();");
                // сохраняем селектор в блокировщик
                var cat = blocker.Categories.FirstOrDefault(c => c.Name == "Косметические правила");
                if (cat == null)
                {
                    cat = new BlockerCategory("Косметические правила", true);
                    blocker.Categories.Add(cat);
                }
                if (!cat.Domains.Contains(selector))
                {
                    cat.Domains.Add(selector);
                    SaveBlockerSettings();
                }
                MessageBox.Show("Элемент скрыт и правило сохранено:\n" + selector, "Atlas — Фильтр", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Log("ApplyCosmeticFilter error", ex); }
        }

        // ==================== АВТООБНОВЛЕНИЕ (GitHub Releases API) ====================
        private async void CheckForUpdatesOnStartup()
        {
            if (!appSettings.AutoUpdate) return;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (appSettings.LastUpdateCheck == today) return;

            appSettings.LastUpdateCheck = today;
            SaveAppSettings();
            await CheckForUpdatesAsync(false);
        }

        private async Task CheckForUpdatesAsync(bool showResultEvenIfNoUpdate)
        {
            string repo = UpdateRepoFixed;
            if (string.IsNullOrWhiteSpace(repo))
            {
                if (showResultEvenIfNoUpdate)
                    MessageBox.Show("Укажите репозиторий обновлений в настройках (например: owner/repo).",
                        "Atlas — Обновления", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                // Не экранируем слеш в owner/repo — иначе GitHub API вернёт 404
                string url = $"https://api.github.com/repos/{repo.Trim('/')}/releases/latest";
                var response = await Http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Log($"Update check failed: HTTP {(int)response.StatusCode} for {repo}");
                    if (showResultEvenIfNoUpdate)
                        MessageBox.Show($"Не удалось проверить обновления.\nHTTP {(int)response.StatusCode}\nПроверьте репозиторий: {repo}",
                            "Atlas — Обновления", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                string releaseUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
                string latest = tag.TrimStart('v', 'V');

                bool newer = VersionHelper.IsNewer(latest, AppVersion);
                Log($"Update check: current={AppVersion}, latest={latest}, newer={newer}");

                if (newer)
                {
                    string body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                    if (body.Length > 900) body = body.Substring(0, 900) + "…";

                    var result = MessageBox.Show(
                        $"Доступна новая версия Atlas: {tag}\nТекущая: {AppVersion}\n\n{body}\n\nСкачать и установить обновление?",
                        "Atlas — Доступно обновление", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (result == DialogResult.Yes)
                        await DownloadAndInstallUpdate(root, tag);
                }
                else if (showResultEvenIfNoUpdate)
                {
                    MessageBox.Show($"У вас актуальная версия ({AppVersion}).",
                        "Atlas — Обновления", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Log("Update check error", ex);
                if (showResultEvenIfNoUpdate)
                    MessageBox.Show("Ошибка при проверке обновлений:\n" + ex.Message,
                        "Atlas — Обновления", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Скачивает подходящий .exe из релиза и запускает установку
        private async Task DownloadAndInstallUpdate(JsonElement root, string tag)
        {
            try
            {
                string downloadUrl = null;
                string fileName = null;
                if (root.TryGetProperty("assets", out var assets))
                {
                    // Сначала ищем .exe, затем .zip (архив со сборкой Atlas)
                    foreach (var ext in new[] { ".exe", ".zip" })
                    {
                        foreach (var a in assets.EnumerateArray())
                        {
                            string n = a.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                            if (n.EndsWith(ext, StringComparison.OrdinalIgnoreCase) &&
                                !n.EndsWith(".blockmap", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = a.TryGetProperty("browser_download_url", out var up) ? up.GetString() : null;
                                fileName = n;
                                break;
                            }
                        }
                        if (downloadUrl != null) break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    MessageBox.Show("В релизе не найден архив со сборкой.\nОткройте страницу загрузки вручную.",
                        "Atlas — Обновления", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string temp = Path.Combine(Path.GetTempPath(),
                    "atlas_" + Guid.NewGuid().ToString("N") + "_" + fileName);

                // Чистим старые временные файлы обновления, чтобы не засорять Temp
                try
                {
                    foreach (var old in Directory.GetFiles(Path.GetTempPath(), "atlas_*_" + fileName))
                    {
                        try { File.Delete(old); } catch { }
                    }
                }
                catch { }

                var proceed = MessageBox.Show(
                    $"Найден установщик: {fileName}\nНачать загрузку? (это может занять несколько минут)",
                    "Atlas — Обновления", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (proceed != DialogResult.Yes) return;

                using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0;
                using var fs = new FileStream(temp, FileMode.Create, FileAccess.Write);
                using var stream = await response.Content.ReadAsStreamAsync();
                byte[] buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, read);
                    readTotal += read;
                    if (total > 0)
                    {
                        int pct = (int)(readTotal * 100 / total);
                        if (pct % 10 == 0) Log($"Загрузка обновления: {pct}%");
                    }
                }

                Log($"Update downloaded: {temp}");

                // Распаковываем архив (если это ZIP) и находим папку с новыми файлами
                string sourceDir;
                if (temp.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string extractDir = Path.Combine(Path.GetTempPath(), "AtlasUpdate_" + tag);
                    if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                    System.IO.Compression.ZipFile.ExtractToDirectory(temp, extractDir);
                    string exe = Directory.GetFiles(extractDir, "Atlas.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (string.IsNullOrEmpty(exe))
                    {
                        MessageBox.Show("В архиве не найден Atlas.exe.", "Atlas — Обновления", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    sourceDir = Path.GetDirectoryName(exe);
                }
                else
                {
                    // Одиночный .exe — кладём его в отдельную папку
                    sourceDir = Path.Combine(Path.GetTempPath(), "AtlasUpdate_" + tag);
                    if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, true);
                    Directory.CreateDirectory(sourceDir);
                    File.Copy(temp, Path.Combine(sourceDir, "Atlas.exe"), true);
                }

                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

                var run = MessageBox.Show(
                    "Обновление загружено.\n\nТекущее окно закроется, файлы будут заменены в папке браузера, и Atlas перезапустится.\nПродолжить?",
                    "Atlas — Обновления", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (run != DialogResult.Yes) return;

                // Создаём BAT-скрипт: ждёт закрытия Atlas, копирует файлы поверх и перезапускает
                string batPath = Path.Combine(Path.GetTempPath(), "atlas_update_" + Guid.NewGuid().ToString("N") + ".bat");
                string script =
                    "@echo off\r\n" +
                    ":wait\r\n" +
                    "tasklist /FI \"IMAGENAME eq Atlas.exe\" 2>NUL | find /I \"Atlas.exe\" >NUL\r\n" +
                    "if \"%ERRORLEVEL%\"==\"0\" ( timeout /t 1 /nobreak >NUL & goto wait )\r\n" +
                    "timeout /t 1 /nobreak >NUL\r\n" +
                    $"xcopy /E /Y /Q /H /R \"{sourceDir}\\*\" \"{appDir}\" >NUL\r\n" +
                    $"start \"\" \"{appDir}\\Atlas.exe\"\r\n" +
                    $"del \"{temp}\" >NUL 2>&1\r\n" +
                    $"rmdir /S /Q \"{sourceDir}\" >NUL 2>&1\r\n" +
                    "del \"%~f0\" >NUL 2>&1\r\n";
                File.WriteAllText(batPath, script);
                Log($"Update script: {batPath}; source={sourceDir}; target={appDir}");

                Process.Start(new ProcessStartInfo("cmd.exe", "/C \"" + batPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                Application.Exit();
            }
            catch (Exception ex)
            {
                Log("DownloadAndInstallUpdate error", ex);
                MessageBox.Show("Не удалось скачать обновление:\n" + ex.Message,
                    "Atlas — Обновления", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Автоматический перенос закладок и истории из Chrome/Edge при первом запуске.
        // Работает только если в Atlas ещё нет своих данных, импортирует молча (без окон).
        private void AutoImportFromBrowsers()
        {
            try
            {
                if (incognitoMode) return;
                if (bookmarks.Count > 0 || visitHistory.Count > 0) return;

                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var candidates = new[]
                {
                    // Chrome
                    (
                        browser: "Chrome",
                        bookmarks: Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Bookmarks"),
                        history: Path.Combine(localAppData, @"Google\Chrome\User Data\Default\History")
                    ),
                    // Edge
                    (
                        browser: "Edge",
                        bookmarks: Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Bookmarks"),
                        history: Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\History")
                    ),
                };

                foreach (var c in candidates)
                {
                    try
                    {
                        if (File.Exists(c.bookmarks) && bookmarks.Count == 0)
                        {
                            foreach (var b in Importer.FromChromeJson(c.bookmarks))
                                if (bookmarks.All(x => x.Url != b.Url)) bookmarks.Add(b);
                            SaveBookmarks();
                            Log($"Автоимпорт закладок из {c.browser}: {bookmarks.Count}");
                        }
                        if (File.Exists(c.history) && visitHistory.Count == 0)
                        {
                            foreach (var h in Importer.FromChromeHistorySqlite(c.history))
                                if (visitHistory.All(x => x.Url != h.Url)) visitHistory.Add(h);
                            SaveHistory();
                            Log($"Автоимпорт истории из {c.browser}: {visitHistory.Count}");
                        }
                    }
                    catch (Exception ex) { Log($"Автоимпорт из {c.browser} ошибка", ex); }
                }
            }
            catch (Exception ex) { Log("AutoImportFromBrowsers ошибка", ex); }
        }

        // Импорт данных из другого браузера
        private void ImportBrowserData(string type, string path)
        {
            try
            {
                int addedBm = 0, addedHist = 0;
                if (type == "bookmarks_html")
                {
                    foreach (var b in Importer.FromHtml(path))
                        if (bookmarks.All(x => x.Url != b.Url)) { bookmarks.Add(b); addedBm++; }
                }
                else if (type == "bookmarks_json")
                {
                    foreach (var b in Importer.FromChromeJson(path))
                        if (bookmarks.All(x => x.Url != b.Url)) { bookmarks.Add(b); addedBm++; }
                }
                else if (type == "history_sqlite")
                {
                    foreach (var h in Importer.FromChromeHistorySqlite(path))
                        if (visitHistory.All(x => x.Url != h.Url)) { visitHistory.Add(h); addedHist++; }
                }

                SaveBookmarks();
                SaveHistory();
                Log($"Import {type}: +{addedBm} bookmarks, +{addedHist} history");
                MessageBox.Show(
                    $"Импорт завершён.\nДобавлено закладок: {addedBm}\nДобавлено записей истории: {addedHist}",
                    "Atlas — Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("ImportBrowserData error", ex);
                MessageBox.Show("Ошибка импорта:\n" + ex.Message, "Atlas — Импорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ==================== НАСТРОЙКИ ПРИЛОЖЕНИЯ ====================
        // Тёмный ComboBox с собственной отрисовкой
        private void OpenAppSettings()
        {
            using var form = new SettingsForm(appSettings, ApplyDarkTitle,
                () => { SaveAppSettings(); ApplyTheme(); },
                () => { SaveAppSettings(); _ = CheckForUpdatesAsync(true); },
                ImportBrowserData);
            form.ShowDialog(this);
        }
    }
}

