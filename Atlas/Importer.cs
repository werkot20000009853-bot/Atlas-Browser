using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Atlas
{
    /// <summary>Импорт закладок и истории из других браузеров.</summary>
    internal static class Importer
    {
        public static List<Form1.BookmarkItem> FromHtml(string path)
        {
            var result = new List<Form1.BookmarkItem>();
            string html = File.ReadAllText(path, Encoding.UTF8);
            // Формат Netscape bookmark file: <DT><A HREF="..." ADD_DATE="...">Заголовок</A>
            var regex = new Regex(@"<A\s+[^>]*HREF\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>(?<title>.*?)</A>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in regex.Matches(html))
            {
                string url = WebUtility.HtmlDecode(m.Groups["url"].Value).Trim();
                string title = WebUtility.HtmlDecode(
                    Regex.Replace(m.Groups["title"].Value, "<[^>]+>", "")).Trim();
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (!url.StartsWith("http://") && !url.StartsWith("https://")) continue;
                result.Add(new Form1.BookmarkItem { Url = url, Title = string.IsNullOrEmpty(title) ? url : title });
            }
            return result;
        }

        public static List<Form1.BookmarkItem> FromChromeJson(string path)
        {
            var result = new List<Form1.BookmarkItem>();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("roots", out var roots)) return result;

            foreach (var root in roots.EnumerateObject())
                WalkBookmarkNode(root.Value, result);
            return result;
        }

        private static void WalkBookmarkNode(JsonElement node, List<Form1.BookmarkItem> result)
        {
            if (node.ValueKind != JsonValueKind.Object) return;

            if (node.TryGetProperty("type", out var type) && type.GetString() == "url" &&
                node.TryGetProperty("url", out var urlEl))
            {
                string url = urlEl.GetString() ?? "";
                string title = node.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(url))
                    result.Add(new Form1.BookmarkItem { Url = url, Title = string.IsNullOrEmpty(title) ? url : title });
            }

            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (var child in children.EnumerateArray())
                    WalkBookmarkNode(child, result);
        }

        public static List<Form1.HistoryItem> FromChromeHistorySqlite(string path)
        {
            var result = new List<Form1.HistoryItem>();
            try
            {
                string temp = Path.Combine(Path.GetTempPath(), "atlas_hist_" + Guid.NewGuid().ToString("N") + ".db");
                File.Copy(path, temp, true);
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={temp}");
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT url, title, last_visit_time FROM urls ORDER BY last_visit_time DESC LIMIT 5000";
                using var reader = cmd.ExecuteReader();
                // Время Chrome/Edge — микросекунды с 1601-01-01 UTC
                var epoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                while (reader.Read())
                {
                    string url = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    string title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    long chromeTime = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    if (!url.StartsWith("http://") && !url.StartsWith("https://")) continue;
                    string time = epoch.AddTicks(chromeTime * 10).ToLocalTime().ToString("HH:mm");
                    result.Add(new Form1.HistoryItem { Url = url, Title = title, Time = time });
                }
                try { File.Delete(temp); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SQLite import error: " + ex.Message);
            }
            return result;
        }
    }
}
