using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Atlas
{
    /// <summary>Окно управления расширениями Chrome.</summary>
    internal class ExtensionsForm : Form
    {
        private readonly CoreWebView2Profile profile;
        private readonly ListBox list;
        private readonly Label status;
        private List<CoreWebView2BrowserExtension> extensions = new List<CoreWebView2BrowserExtension>();

        public Action OnExtensionsChanged = () => { };

        public ExtensionsForm(CoreWebView2Profile profile, Action<Form, string> darkHeader)
        {
            this.profile = profile;
            Text = "Atlas — Расширения";
            Width = 560; Height = 460;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(3, 7, 17);
            ForeColor = Color.FromArgb(226, 236, 252);
            Font = new Font("Segoe UI", 9.5F);
            ShowIcon = false;

            Label info = new Label
            {
                Text = "Установленные расширения Chrome (распакованные из папки):",
                Location = new Point(24, 56), AutoSize = true, ForeColor = Color.FromArgb(139, 164, 196)
            };
            list = new ListBox
            {
                Location = new Point(24, 82), Size = new Size(494, 200),
                BackColor = Color.FromArgb(10, 22, 42), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Button addBtn = MakeButton("Установить из папки", 24, 300, 180, Color.FromArgb(28, 70, 128));
            addBtn.Click += async (s, e) => await InstallExtension();
            Button toggleBtn = MakeButton("Вкл / Выкл", 214, 300, 130, Color.FromArgb(10, 22, 42));
            toggleBtn.Click += async (s, e) => await ToggleExtension();
            Button removeBtn = MakeButton("Удалить", 354, 300, 120, Color.FromArgb(70, 30, 35));
            removeBtn.Click += async (s, e) => await RemoveExtension();
            Button refreshBtn = MakeButton("Обновить список", 24, 342, 180, Color.FromArgb(10, 22, 42));
            refreshBtn.Click += async (s, e) => await RefreshExtensions();

            status = new Label
            {
                Location = new Point(24, 390), Size = new Size(494, 40),
                ForeColor = Color.FromArgb(56, 139, 253), Font = new Font("Segoe UI", 9F)
            };

            Controls.Add(info);
            Controls.Add(list);
            Controls.Add(addBtn);
            Controls.Add(toggleBtn);
            Controls.Add(removeBtn);
            Controls.Add(refreshBtn);
            Controls.Add(status);
            darkHeader(this, "Atlas — Расширения");
            _ = RefreshExtensions();
        }

        private static Button MakeButton(string text, int left, int top, int width, Color back)
        {
            var b = new Button
            {
                Text = text, Location = new Point(left, top), Size = new Size(width, 32),
                FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private CoreWebView2BrowserExtension Selected =>
            list.SelectedIndex >= 0 && list.SelectedIndex < extensions.Count ? extensions[list.SelectedIndex] : null;

        private async Task RefreshExtensions()
        {
            try
            {
                extensions = (await profile.GetBrowserExtensionsAsync()).ToList();
                list.Items.Clear();
                foreach (var ext in extensions)
                    list.Items.Add($"{ext.Name}  [{(ext.IsEnabled ? "вкл" : "выкл")}]  — {ext.Id}");
                status.Text = $"Найдено расширений: {extensions.Count}";
            }
            catch (Exception ex) { status.Text = "Ошибка: " + ex.Message; }
        }

        private async Task InstallExtension()
        {
            using var fbd = new FolderBrowserDialog
            {
                Description = "Выберите папку с распакованным расширением (там есть manifest.json)"
            };
            if (fbd.ShowDialog() != DialogResult.OK) return;
            try
            {
                status.Text = "Установка...";
                await profile.AddBrowserExtensionAsync(fbd.SelectedPath);
                await RefreshExtensions();
                status.Text = "Расширение установлено. Перезапустите вкладки, если не активировалось.";
                OnExtensionsChanged?.Invoke();
            }
            catch (Exception ex) { status.Text = "Ошибка установки: " + ex.Message; }
        }

        private async Task ToggleExtension()
        {
            var ext = Selected;
            if (ext == null) { status.Text = "Выберите расширение."; return; }
            try
            {
                await ext.EnableAsync(!ext.IsEnabled);
                await RefreshExtensions();
                OnExtensionsChanged?.Invoke();
            }
            catch (Exception ex) { status.Text = "Ошибка: " + ex.Message; }
        }

        private async Task RemoveExtension()
        {
            var ext = Selected;
            if (ext == null) { status.Text = "Выберите расширение."; return; }
            try
            {
                await ext.RemoveAsync();
                await RefreshExtensions();
                OnExtensionsChanged?.Invoke();
            }
            catch (Exception ex) { status.Text = "Ошибка: " + ex.Message; }
        }
    }
}
