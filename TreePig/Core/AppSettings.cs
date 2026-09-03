using System;
using System.IO;
using System.Text.Json;

namespace TreePig.Core
{
    // lives in %AppData%\TreePig\settings.json
    class AppSettings
    {
        public string LastPath { get; set; } = "";
        public bool ShowBars { get; set; } = true;
        public bool CollectOwner { get; set; } = false;
        public string Unit { get; set; } = "Auto";
        public bool ScanLastOnStart { get; set; } = false;
        public string BarColor { get; set; } = "192,80,77";
        public int SortColumn { get; set; } = 1;
        public bool SortAscending { get; set; } = false;
        public int[] ColumnWidths { get; set; }
        public bool[] ColumnVisible { get; set; }
        public int[] WindowRect { get; set; }
        public bool WindowMaximized { get; set; }

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TreePig");
                return Path.Combine(dir, "settings.json");
            }
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
