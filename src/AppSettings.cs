using System;
using System.IO;
using System.Text;

namespace RP2040AutoFlash
{
    enum FlashMode { Auto, Manual }

    // 設定は exe と同じフォルダの RP2040AutoFlash.ini に「キー=値」で保存する
    class AppSettings
    {
        public string Uf2Path = "";
        public FlashMode Mode = FlashMode.Auto;
        public bool IncludeRP2350;
        public bool Notify = true;
        public bool Sound = true;

        static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RP2040AutoFlash.ini"); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            string[] lines;
            try
            {
                if (!File.Exists(FilePath)) return settings;
                lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            }
            catch (IOException) { return settings; }
            catch (UnauthorizedAccessException) { return settings; }

            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string value = line.Substring(eq + 1).Trim();
                switch (line.Substring(0, eq).Trim())
                {
                    case "Uf2Path": settings.Uf2Path = value; break;
                    case "Mode": settings.Mode = value == "Manual" ? FlashMode.Manual : FlashMode.Auto; break;
                    case "IncludeRP2350": settings.IncludeRP2350 = value == "1"; break;
                    case "Notify": settings.Notify = value != "0"; break;
                    case "Sound": settings.Sound = value != "0"; break;
                }
            }
            return settings;
        }

        public void Save()
        {
            File.WriteAllLines(FilePath, new[]
            {
                "Uf2Path=" + Uf2Path,
                "Mode=" + Mode,
                "IncludeRP2350=" + (IncludeRP2350 ? "1" : "0"),
                "Notify=" + (Notify ? "1" : "0"),
                "Sound=" + (Sound ? "1" : "0"),
            }, Encoding.UTF8);
        }
    }
}
