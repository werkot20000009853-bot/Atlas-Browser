using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace Atlas
{
    /// <summary>Шифрование через DPAPI (привязка к текущему пользователю Windows).</summary>
    internal static class CryptoService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AtlasBrowser-DataProtection-v1");

        public static string ReadProtected(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch { return null; }
        }

        public static void WriteProtected(string path, string json)
        {
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
        }

        /// <summary>Миграция со старого незашифрованного .json.</summary>
        public static string ReadProtectedWithLegacy(string path, string legacyPath)
        {
            string json = ReadProtected(path);
            if (json != null) return json;
            try
            {
                if (File.Exists(legacyPath))
                {
                    json = File.ReadAllText(legacyPath);
                    WriteProtected(path, json);
                    File.Delete(legacyPath);
                    return json;
                }
            }
            catch { }
            return null;
        }
    }

    /// <summary>Сравнение версий для автообновления.</summary>
    internal static class VersionHelper
    {
        public static bool IsNewer(string latest, string current)
        {
            latest = (latest ?? "").TrimStart('v', 'V');
            current = (current ?? "").TrimStart('v', 'V');
            if (Version.TryParse(latest, out var lv) && Version.TryParse(current, out var cv))
                return lv > cv;

            var l = (latest ?? "").Split('.');
            var c = (current ?? "").Split('.');
            for (int i = 0; i < Math.Max(l.Length, c.Length); i++)
            {
                int ln = i < l.Length && int.TryParse(l[i], out var a) ? a : 0;
                int cn = i < c.Length && int.TryParse(c[i], out var b) ? b : 0;
                if (ln > cn) return true;
                if (ln < cn) return false;
            }
            return false;
        }
    }

    /// <summary>Утилиты URL и текста.</summary>
    internal static class UrlHelper
    {
        public static string BuildSearchUrl(string engine, string query)
        {
            string q = Uri.EscapeDataString(query ?? "");
            switch ((engine ?? "").ToLowerInvariant())
            {
                case "duckduckgo": return "https://duckduckgo.com/?q=" + q;
                case "bing": return "https://www.bing.com/search?q=" + q;
                case "yandex": return "https://yandex.ru/search/?text=" + q;
                default: return "https://www.google.com/search?q=" + q;
            }
        }

        /// <summary>Обрезает текст с многоточием по ширине контрола.</summary>
        public static string Elide(string text, Control control)
        {
            if (string.IsNullOrEmpty(text)) return text;
            using var font = new Font(control.Font.FontFamily, control.Font.Size, control.Font.Style, control.Font.Unit);
            int availableWidth = control.ClientSize.Width - 6;
            if (TextRenderer.MeasureText(text, font).Width <= availableWidth) return text;

            string ellipsis = "…";
            int maxWidth = (int)(availableWidth - TextRenderer.MeasureText(ellipsis, font).Width);
            for (int len = text.Length; len > 0; len--)
            {
                string candidate = text.Substring(0, len) + ellipsis;
                if (TextRenderer.MeasureText(candidate, font).Width <= maxWidth) return candidate;
            }
            return ellipsis;
        }
    }
}
