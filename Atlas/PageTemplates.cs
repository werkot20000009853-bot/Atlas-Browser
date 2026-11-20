using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Atlas
{
    /// <summary>Генерация HTML для внутренних страниц браузера (Закладки, Журнал).</summary>
    internal static class PageTemplates
    {
        private static string H(string value) => WebUtility.HtmlEncode(value ?? "");

        private static string ToDataUrl(string html) =>
            "data:text/html;charset=utf-8," + Uri.EscapeDataString(html);

        public static string BuildBookmarksPage(IEnumerable<Atlas.Form1.BookmarkItem> bookmarks)
        {
            var html = new StringBuilder();
            html.Append("<html><head><meta charset='utf-8'><title>Закладки</title>");
            html.Append("<style>");
            html.Append("body { background-color: #202024; color: #f0f0f5; font-family: 'Segoe UI', Arial, sans-serif; padding: 30px 50px; margin: 0; }");
            html.Append(".header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 25px; }");
            html.Append("h1 { font-size: 26px; font-weight: 600; margin: 0; }");
            html.Append(".item { display: flex; align-items: center; justify-content: space-between; padding: 12px 16px; border-radius: 8px; margin-bottom: 8px; background: #2a2a30; border: 1px solid #3f3f46; }");
            html.Append(".item:hover { background: #32323a; }");
            html.Append(".info { display: flex; flex-direction: column; overflow: hidden; padding-right: 20px; }");
            html.Append(".title { color: #38bdf8; font-size: 15px; font-weight: 500; text-decoration: none; margin-bottom: 4px; }");
            html.Append(".title:hover { text-decoration: underline; }");
            html.Append(".url { color: #9ca3af; font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }");
            html.Append(".empty { color: #888; font-style: italic; margin-top: 20px; }");
            html.Append("</style></head><body>");
            html.Append("<div class='header'><h1>⭐ Закладки</h1></div>");

            int count = 0;
            foreach (var b in bookmarks)
            {
                if (count++ == 0) html.Append("<div>");
                html.Append("<div class='item'><div class='info'>");
                html.Append($"<a href='{H(b.Url)}' class='title'>{H(b.Title)}</a>");
                html.Append($"<span class='url'>{H(b.Url)}</span>");
                html.Append("</div></div>");
            }
            if (count > 0) html.Append("</div>");
            else html.Append("<p class='empty'>У вас пока нет закладок. Нажмите на значок звездочки (☆) на панели навигации, чтобы добавить сайт.</p>");

            html.Append("</body></html>");
            return ToDataUrl(html.ToString());
        }

        public static string BuildHistoryPage(IEnumerable<Atlas.Form1.HistoryItem> history, string clearToken)
        {
            var html = new StringBuilder();
            html.Append("<html><head><meta charset='utf-8'><title>Журнал</title>");
            html.Append("<style>");
            html.Append("body { background-color: #202024; color: #f0f0f5; font-family: 'Segoe UI', Arial, sans-serif; padding: 30px 50px; margin: 0; }");
            html.Append(".header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 25px; }");
            html.Append("h1 { font-size: 26px; font-weight: 600; margin: 0; }");
            html.Append(".clear-btn { background: #dc2626; color: white; border: none; padding: 8px 16px; border-radius: 6px; font-size: 14px; cursor: pointer; font-weight: 600; }");
            html.Append(".clear-btn:hover { background: #b91c1c; }");
            html.Append(".search-box { width: 100%; max-width: 600px; padding: 10px 15px; background: #2a2a30; border: 1px solid #3f3f46; border-radius: 6px; color: #fff; font-size: 14px; outline: none; margin-bottom: 20px; }");
            html.Append(".search-box:focus { border-color: #007acc; }");
            html.Append(".item { display: flex; align-items: center; justify-content: space-between; padding: 10px 14px; border-radius: 6px; margin-bottom: 6px; background: transparent; }");
            html.Append(".item:hover { background: #2a2a32; }");
            html.Append(".info { display: flex; flex-direction: column; overflow: hidden; padding-right: 20px; }");
            html.Append(".title { color: #e4e4e7; font-size: 14px; text-decoration: none; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; margin-bottom: 3px; }");
            html.Append(".title:hover { color: #38bdf8; text-decoration: underline; }");
            html.Append(".url { color: #9ca3af; font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }");
            html.Append(".time { color: #9ca3af; font-size: 13px; white-space: nowrap; }");
            html.Append(".empty { color: #888; font-style: italic; margin-top: 20px; }");
            html.Append("</style>");
            html.Append("<script>");
            html.Append("function filterHistory() {");
            html.Append("  let input = document.getElementById('searchInput').value.toLowerCase();");
            html.Append("  let items = document.getElementsByClassName('item');");
            html.Append("  for (let i = 0; i < items.length; i++) {");
            html.Append("    let text = items[i].innerText.toLowerCase();");
            html.Append("    items[i].style.display = text.includes(input) ? 'flex' : 'none';");
            html.Append("  }");
            html.Append("}");
            html.Append("function clearHistory() {");
            html.Append("  window.chrome.webview.postMessage('clear:" + clearToken + "');");
            html.Append("}");
            html.Append("</script>");
            html.Append("</head><body>");
            html.Append("<div class='header'><h1>🕒 Журнал</h1><button class='clear-btn' onclick='clearHistory()'>Очистить историю</button></div>");
            html.Append("<input type='text' id='searchInput' class='search-box' placeholder='Поиск в журнале...' onkeyup='filterHistory()'>");

            int count = 0;
            var list = new List<Atlas.Form1.HistoryItem>(history);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var h = list[i];
                if (count++ == 0) html.Append("<div id='historyList'>");
                string displayTitle = string.IsNullOrEmpty(h.Title) ? h.Url : h.Title;
                html.Append("<div class='item'><div class='info'>");
                html.Append($"<a href='{H(h.Url)}' class='title'>{H(displayTitle)}</a>");
                html.Append($"<span class='url'>{H(h.Url)}</span>");
                html.Append("</div>");
                html.Append($"<span class='time'>{H(h.Time)}</span>");
                html.Append("</div>");
            }
            if (count > 0) html.Append("</div>");
            else html.Append("<p class='empty'>Журнал пуст.</p>");

            html.Append("</body></html>");
            return ToDataUrl(html.ToString());
        }
    }
}
