using System;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Atlas
{
    /// <summary>Открытие окна управления расширениями Chrome.</summary>
    internal static class ExtensionsHost
    {
        public static void Open(Form owner, CoreWebView2 core, Action<Form, string> darkHeader, Action reloadTabs)
        {
            if (core == null)
            {
                MessageBox.Show("Браузер ещё инициализируется.", "Atlas — Расширения",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var form = new ExtensionsForm(core.Profile, darkHeader);
            form.OnExtensionsChanged = reloadTabs;
            form.ShowDialog(owner);
        }
    }
}
