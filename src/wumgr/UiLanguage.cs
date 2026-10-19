using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace wumgr
{
    public enum UiLanguage { ZhCN, EnUS }

    /// <summary>Translate display text only; never mutate driver IDs, versions or internal statuses.</summary>
    public static class UiLanguageService
    {
        public static UiLanguage Current { get; private set; } = UiLanguage.ZhCN;
        static readonly Dictionary<string, string> En = UiStrings.English;
        static readonly Dictionary<string, string> Zh = En.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Key);
        // Longest source phrase wins, with a single replacement pass. Short device/vendor words are not replaced inside raw data.
        static readonly Regex DisplayPattern = new Regex(String.Join("|", En.Keys.Where(k => k.Length >= 4 || k == "失败：" || k == "已选 ").OrderByDescending(k => k.Length).Select(Regex.Escape)), RegexOptions.Compiled);
        public static string Get(string text)
        {
            if (text == null) return text;
            string value;
            return Current == UiLanguage.EnUS ? (En.TryGetValue(text, out value) ? value : text) : (Zh.TryGetValue(text, out value) ? value : text);
        }
        public static string T(string zh, string en) { return Current == UiLanguage.EnUS ? en : zh; }
        public static string F(string zh, string en, params object[] args) { return String.Format(Current == UiLanguage.EnUS ? en : zh, args); }
        public static string Display(string text)
        {
            if (String.IsNullOrEmpty(text) || Current == UiLanguage.ZhCN) return text;
            string exact;
            if (En.TryGetValue(text, out exact)) return exact;
            return DisplayPattern.Replace(text, m => En[m.Value]);
        }
        public static string LanguageName(UiLanguage language) { return language == UiLanguage.EnUS ? "English" : "简体中文"; }
        public static string ConfigPath
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("WUMGR_CONFIG_DIR");
                if (String.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WuMgr");
                return Path.Combine(root, "language.txt");
            }
        }
        public static UiLanguage Load(UiLanguage fallback = UiLanguage.ZhCN)
        {
            try
            {
                string value = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath).Trim() : "";
                if (value.Equals("en-US", StringComparison.OrdinalIgnoreCase) || value.Equals("en", StringComparison.OrdinalIgnoreCase)) return UiLanguage.EnUS;
                if (value.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)) return UiLanguage.ZhCN;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return fallback;
        }
        public static void Set(UiLanguage language, bool persist)
        {
            Current = language;
            CultureInfo culture = CultureInfo.GetCultureInfo(language == UiLanguage.EnUS ? "en-US" : "zh-CN");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            if (!persist) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath, language == UiLanguage.EnUS ? "en-US" : "zh-CN", Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public static void Apply(Control root)
        {
            // TextBox contents belong to the user (search and paths), not to localization.
            if (!(root is TextBoxBase) && !(root is LocalizedStatusLabel) && !String.IsNullOrEmpty(root.Text)) root.Text = Get(root.Text);
            if (!String.IsNullOrEmpty(root.AccessibleName)) root.AccessibleName = Get(root.AccessibleName);
            foreach (Control child in root.Controls) Apply(child);
        }
    }

    internal sealed class LocalizedStatusLabel : Label
    {
        string source = String.Empty;
        public override string Text
        {
            get { return base.Text; }
            set { source = value ?? String.Empty; base.Text = UiLanguageService.Display(source); }
        }
        internal void Relocalize() { base.Text = UiLanguageService.Display(source); }
    }
}
