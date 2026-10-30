using System;
using System.Collections.Generic;
using System.IO;

namespace RP2040AutoFlash
{
    // BOOT ボタンを押しながら刺したときに出てくる書き込み用ドライブ（RPI-RP2 / RP2350）
    class BootDrive
    {
        // ボードがなくても動きを試せるよう、この環境変数に書いたフォルダ（; 区切り）もドライブとして扱う
        const string ExtraRootsVariable = "RP2040AUTOFLASH_EXTRA_ROOTS";

        public string Root { get; private set; }
        public Chip Chip { get; private set; }

        public string Name
        {
            get { return Root.TrimEnd('\\') + "（" + Chip + "）"; }
        }

        public bool StillPresent
        {
            get { return File.Exists(Path.Combine(Root, "INFO_UF2.TXT")); }
        }

        public static List<BootDrive> Scan(bool includeRP2350)
        {
            var roots = new List<string>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Removable && drive.IsReady) roots.Add(drive.Name);
                }
                catch (IOException) { }
            }
            string extra = Environment.GetEnvironmentVariable(ExtraRootsVariable);
            if (!string.IsNullOrEmpty(extra)) roots.AddRange(extra.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

            var found = new List<BootDrive>();
            foreach (string root in roots)
            {
                var bootDrive = Probe(root);
                if (bootDrive != null && (bootDrive.Chip == Chip.RP2040 || includeRP2350)) found.Add(bootDrive);
            }
            return found;
        }

        // INFO_UF2.TXT の Board-ID で RP2040 / RP2350 の書き込みモードかを見分ける
        public static BootDrive Probe(string root)
        {
            try
            {
                string info = Path.Combine(root, "INFO_UF2.TXT");
                if (!File.Exists(info)) return null;
                foreach (string line in File.ReadAllLines(info))
                {
                    if (!line.StartsWith("Board-ID:", StringComparison.OrdinalIgnoreCase)) continue;
                    string id = line.Substring("Board-ID:".Length).Trim();
                    if (id.StartsWith("RPI-RP2", StringComparison.OrdinalIgnoreCase)) return new BootDrive { Root = root, Chip = Chip.RP2040 };
                    if (id.StartsWith("RP2350", StringComparison.OrdinalIgnoreCase)) return new BootDrive { Root = root, Chip = Chip.RP2350 };
                }
            }
            catch (IOException) { }                  // 読んでいる途中で抜かれた、まだ準備中など
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }            // 環境変数に書かれたパスがおかしい
            return null;
        }
    }
}
