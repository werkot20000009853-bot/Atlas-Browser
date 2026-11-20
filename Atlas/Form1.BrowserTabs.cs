using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Atlas
{
    public partial class Form1
    {
        private async void AddNewTab(string url)
        {
            Panel browserContainer = new Panel() { Dock = DockStyle.Fill, Visible = false };
            WebView2 webView = new WebView2() { Dock = DockStyle.Fill };
            browserContainer.Controls.Add(webView);
            contentPanel.Controls.Add(browserContainer);

            Panel tabContainer = new Panel()
            {
                Width = 180,
                Height = 36,
                Margin = new Padding(4, 4, 4, 0),
                BackColor = Surface,
                Cursor = Cursors.Hand
            };
            tabContainer.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                bool active = activeBrowserContainer == browserContainer;
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, tabContainer.Height),
                    active ? Color.FromArgb(20, 44, 80) : Surface,
                    active ? Color.FromArgb(14, 28, 52) : Color.FromArgb(8, 16, 32));
                g.FillPath(brush, GetRoundRectPath(tabContainer.ClientRectangle, 10));
                using var pen = new Pen(active ? Accent : BorderCol, 1.2f);
                g.DrawPath(pen, GetRoundRectPath(tabContainer.ClientRectangle, 10));
            };

            Button tabButton = new Button()
            {
                Text = "  Загрузка...",
                Width = 140,
                Height = 36,
                Top = 0,
                Left = 0,
                FlatStyle = FlatStyle.Flat,
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };
            tabButton.FlatAppearance.BorderSize = 0;
            tabButton.Cursor = Cursors.Hand;

            Button closeButton = new Button()
            {
                Text = IcoClose,
                Width = 28,
                Height = 28,
                Top = 4,
                Left = 143,
                FlatStyle = FlatStyle.Flat,
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(IconFont, 11),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.MouseEnter += (s, e) => { closeButton.ForeColor = Color.FromArgb(255, 110, 110); closeButton.BackColor = Color.FromArgb(40, 10, 10); };
            closeButton.MouseLeave += (s, e) => { closeButton.ForeColor = TextMuted; closeButton.BackColor = Color.Transparent; };

            tabContainer.Controls.Add(tabButton);
            tabContainer.Controls.Add(closeButton);

            tabContainer.Tag = browserContainer;
            int newTabIdx = tabsHeaderPanel.Controls.Count - 1;
            tabsHeaderPanel.Controls.Add(tabContainer);
            tabsHeaderPanel.Controls.SetChildIndex(tabContainer, newTabIdx);

            Action activateAction = () => SwitchTab(tabContainer, browserContainer, webView);
            tabButton.Click += (s, e) => activateAction();
            tabContainer.Click += (s, e) => activateAction();

            closeButton.Click += (s, e) => {
                CloseTab(tabContainer, browserContainer, webView);
            };

            // Закрепление вкладки по правому клику
            var tabMenu = new ContextMenuStrip();
            tabMenu.BackColor = Color.FromArgb(10, 22, 42);
            tabMenu.ForeColor = TextMain;
            var pinItem = new ToolStripMenuItem("Закрепить вкладку");
            pinItem.Click += (s, e) => { TogglePinTab(tabContainer, tabButton); };
            var closeItem = new ToolStripMenuItem("Закрыть вкладку");
            closeItem.Click += (s, e) => { CloseTab(tabContainer, browserContainer, webView); };
            var closeOthers = new ToolStripMenuItem("Закрыть другие вкладки");
            closeOthers.Click += (s, e) => { CloseOtherTabs(tabContainer); };
            var closeRight = new ToolStripMenuItem("Закрыть вкладки справа");
            closeRight.Click += (s, e) => { CloseTabsToRight(tabContainer); };
            tabMenu.Items.AddRange(new ToolStripItem[] { pinItem, closeItem, new ToolStripSeparator(), closeOthers, closeRight });
            tabContainer.ContextMenuStrip = tabMenu;

            activateAction();

            var activeEnv = incognitoMode ? incognitoEnvironment : webEnvironment;
            if (activeEnv != null)
            {
                await webView.EnsureCoreWebView2Async(activeEnv);

                AttachContextMenu(webView);

                // Встроенный блокировщик: перехватываем все сетевые запросы
                webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.WebResourceRequested += (sender, args) =>
                {
                    try
                    {
                        if (ShouldBlock(args.Request.Uri))
                        {
                            sessionBlockedCount++;
                            blocker.BlockedCount++;
                            // Возвращаем пустой ответ — запрос отменён
                            var response = activeEnv.CreateWebResourceResponse(
                                new MemoryStream(), 204, "No Content", "");
                            args.Response = response;
                        }
                    }
                    catch (Exception ex) { Log("WebResourceRequested error", ex); }
                };

                // Средний клик / target="_blank" — открываем вкладку в том же окне
                webView.CoreWebView2.NewWindowRequested += (sender, args) =>
                {
                    // Полноэкранный запрос YouTube — не перехватываем, WebView2 сам войдёт в fullscreen
                    // (обрабатывается через ContainsFullScreenElementChanged)
                    if (args.IsUserInitiated && string.IsNullOrEmpty(args.Uri)) return;

                    args.Handled = true; // запрещаем WebView2 открывать новое окно
                    string target = args.Uri;
                    if (string.IsNullOrWhiteSpace(target)) return;

                    // Выполняем в потоке UI, т.к. событие может прийти из фонового потока WebView2
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => AddNewTab(target)));
                    else
                        AddNewTab(target);
                };

                // Полноэкранный режим видео (YouTube и др.)
                webView.CoreWebView2.ContainsFullScreenElementChanged += (s, e) =>
                {
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => SetVideoFullScreen(webView.CoreWebView2.ContainsFullScreenElement)));
                    else
                        SetVideoFullScreen(webView.CoreWebView2.ContainsFullScreenElement);
                };

                webView.CoreWebView2.WebMessageReceived += (s, e) =>
                {
                    string msg = e.TryGetWebMessageAsString();
                    if (string.IsNullOrEmpty(msg)) return;

                    // Подсказки поиска с домашней страницы (data:text/html)
                    if (msg.StartsWith("suggest:"))
                    {
                        string query = msg.Substring("suggest:".Length);
                        if (!string.IsNullOrWhiteSpace(query))
                            _ = FetchSuggestionsAsync(webView, query);
                        return;
                    }
                    if (msg.StartsWith("suggest-pick:"))
                    {
                        // Пользователь выбрал подсказку — запоминаем в истории поиска
                        string picked = msg.Substring("suggest-pick:".Length);
                        Log("Search picked: " + picked);
                        return;
                    }

                    // Команда очистки истории — только от нашей страницы «Журнал»
                    bool fromHistoryPage = webView.CoreWebView2.Source != null &&
                                           webView.CoreWebView2.Source.StartsWith("data:text/html") &&
                                           webView.CoreWebView2.Source.Contains("Журнал");
                    if (msg == "clear:" + historyClearToken && fromHistoryPage)
                    {
                        visitHistory.Clear();
                        SaveHistory();
                        MessageBox.Show("История успешно очищена!", "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        webView.CoreWebView2.Reload();
                        return;
                    }

                    // Пипетка: язык ищет домен кликнутого элемента и присылает его
                    if (msg.StartsWith("picker:"))
                    {
                        string payload = msg.Substring("picker:".Length).Trim();
                        string domain = payload;
                        string selector = "";
                        int sep = payload.IndexOf('|');
                        if (sep > 0)
                        {
                            domain = payload.Substring(0, sep).Trim();
                            selector = payload.Substring(sep + 1).Trim();
                        }
                        if (!string.IsNullOrEmpty(domain))
                        {
                            if (InvokeRequired)
                                BeginInvoke(new Action(() => AddBlockedDomainFromPicker(domain)));
                            else
                                AddBlockedDomainFromPicker(domain);
                        }
                        if (!string.IsNullOrEmpty(selector) && selector != "body")
                        {
                            if (InvokeRequired)
                                BeginInvoke(new Action(() => ApplyCosmeticFilter(selector)));
                            else
                                ApplyCosmeticFilter(selector);
                        }
                        // Снимаем режим пипетки после выбора
                        if (InvokeRequired)
                            BeginInvoke(new Action(() => SetPickerMode(false)));
                        else
                            SetPickerMode(false);
                        return;
                    }

                    // Пипетка: пользователь кликнул по пустому месту — отменяем
                    if (msg == "picker:cancel")
                    {
                        if (InvokeRequired)
                            BeginInvoke(new Action(() => SetPickerMode(false)));
                        else
                            SetPickerMode(false);
                    }
                };

                webView.CoreWebView2.DownloadStarting += (sender, e) =>
                {
                    try
                    {
                        string downloadsFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                        Directory.CreateDirectory(downloadsFolder);

                        // Получаем имя файла из URL: эти свойства доступны в разных версиях WebView2.
                        string suggestedName = "download";
                        try
                        {
                            Uri downloadUri = new Uri(e.DownloadOperation.Uri);
                            string urlFileName = Path.GetFileName(downloadUri.LocalPath);
                            if (!string.IsNullOrWhiteSpace(urlFileName))
                                suggestedName = Uri.UnescapeDataString(urlFileName);
                        }
                        catch (Exception ex) { Log("Download filename parse error", ex); }

                        foreach (char invalid in Path.GetInvalidFileNameChars())
                            suggestedName = suggestedName.Replace(invalid, '_');

                        string nameOnly = Path.GetFileNameWithoutExtension(suggestedName);
                        string extension = Path.GetExtension(suggestedName);
                        if (string.IsNullOrWhiteSpace(nameOnly)) nameOnly = "download";

                        string destination = Path.Combine(downloadsFolder, suggestedName);
                        int suffix = 1;
                        while (File.Exists(destination))
                        {
                            destination = Path.Combine(downloadsFolder,
                                nameOnly + " (" + suffix++ + ")" + extension);
                        }

                        e.ResultFilePath = destination;
                        DownloadEntry entry = new DownloadEntry
                        {
                            FileName = Path.GetFileName(destination),
                            FilePath = destination,
                            Url = e.DownloadOperation.Uri,
                            Status = "Загрузка",
                            Operation = e.DownloadOperation
                        };
                        downloads.Add(entry);
                        e.DownloadOperation.StateChanged += (s2, e2) => UpdateDownloadEntry(entry);
                        e.DownloadOperation.BytesReceivedChanged += (s2, e2) => UpdateDownloadEntry(entry);
                        UpdateDownloadEntry(entry);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Не удалось начать загрузку: " + ex.Message, "Atlas", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                webView.CoreWebView2.Navigate(url);

                if (url.StartsWith("data:text/html"))
                {
                    if (url.Contains("Закладки")) tabButton.Text = " Закладки";
                    else tabButton.Text = " Журнал";
                }

                webView.CoreWebView2.DocumentTitleChanged += (s, e) =>
                {
                    if (!url.StartsWith("data:text/html"))
                    {
                        string title = webView.CoreWebView2.DocumentTitle;
                        if (!string.IsNullOrEmpty(title))
                        {
                            tabButton.Text = " " + UrlHelper.Elide(title.Length > 20 ? title.Substring(0, 17) + "…" : title, tabButton);
                            browserContainer.Tag = title;

                            if (webView.Source != null && !incognitoMode)
                            {
                                string currentUrl = webView.Source.ToString();
                                var existing = visitHistory.FindLast(h => h.Url == currentUrl);
                                if (existing != null)
                                {
                                    existing.Title = title;
                                    SaveHistory();
                                }
                            }
                        }
                    }
                };

                webView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    if (activeBrowserContainer == browserContainer && webView.Source != null)
                    {
                        string currentUrl = webView.Source.ToString();
                        currentPageUrl = currentUrl;
                        UpdateBookmarkIcon();

                        if (!currentUrl.StartsWith("data:text/html"))
                        {
                            tabButton.Text = webView.CoreWebView2.DocumentTitle;
                        }

                        if (!currentUrl.StartsWith("data:text/html"))
                        {
                            UpdateAddressBarDisplay();
                            string timeNow = DateTime.Now.ToString("HH:mm");

                            if (!incognitoMode &&
                                (visitHistory.Count == 0 || visitHistory[visitHistory.Count - 1].Url != currentUrl))
                            {
                                visitHistory.Add(new HistoryItem { Url = currentUrl, Title = webView.CoreWebView2.DocumentTitle, Time = timeNow });
                                SaveHistory();
                            }
                        }
                        else
                        {
                            txtUrl.Text = UrlHelper.Elide(currentUrl.Contains("Закладки") ? "atlas://bookmarks" : "atlas://history", txtUrl);
                        }
                    }
                };
            }
        }

    }
}
