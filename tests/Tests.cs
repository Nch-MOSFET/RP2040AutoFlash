using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace RP2040AutoFlash
{
    // ボードなしで、UF2 の読み取り・ドライブの見分け・書き込みを確かめる
    // 使い方: Tests.exe <作業用の空フォルダ>
    static class Tests
    {
        static int failures;

        static int Main(string[] args)
        {
            string work = args[0];
            Directory.CreateDirectory(work);

            ParsesImages();
            RejectsBrokenImages();
            ProbesBootDrives(work);
            FlashSeesReboot(work);
            FlashReportsNoReboot(work);

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        static void ParsesImages()
        {
            var rp2040 = Uf2Image.Parse(MakeUf2(4, Uf2Image.FamilyRP2040));
            Check(rp2040.BlockCount == 4 && rp2040.PayloadBytes == 1024, "RP2040 UF2: ブロック数と大きさ");
            Check(rp2040.SupportsChip(Chip.RP2040) && !rp2040.SupportsChip(Chip.RP2350), "RP2040 UF2: RP2040 だけに書ける");
            Check(rp2040.FamilyText == "RP2040", "RP2040 UF2: 表示名 " + rp2040.FamilyText);

            var rp2350 = Uf2Image.Parse(MakeUf2(2, Uf2Image.FamilyRP2350ArmS));
            Check(!rp2350.SupportsChip(Chip.RP2040) && rp2350.SupportsChip(Chip.RP2350), "RP2350 UF2: RP2350 だけに書ける");

            var plain = Uf2Image.Parse(MakeUf2(2, null));
            Check(plain.SupportsChip(Chip.RP2040) && plain.SupportsChip(Chip.RP2350), "family ID なし: どちらにも書ける");
        }

        static void RejectsBrokenImages()
        {
            var broken = MakeUf2(2, Uf2Image.FamilyRP2040);
            broken[512 + 508] ^= 0xFF;
            Check(Throws(() => Uf2Image.Parse(broken)), "壊れたブロックを弾く");
            Check(Throws(() => Uf2Image.Parse(new byte[100])), "512 バイト単位でないものを弾く");
        }

        static void ProbesBootDrives(string work)
        {
            string rp2040 = MakeBoard(work, "probe2040", "RPI-RP2");
            string rp2350 = MakeBoard(work, "probe2350", "RP2350");
            string other = MakeBoard(work, "probeOther", "SAMD21G18A-Feather-v0");
            string empty = Path.Combine(work, "probeEmpty");
            Directory.CreateDirectory(empty);

            var a = BootDrive.Probe(rp2040);
            var b = BootDrive.Probe(rp2350);
            Check(a != null && a.Chip == Chip.RP2040, "RPI-RP2 を RP2040 と見分ける");
            Check(b != null && b.Chip == Chip.RP2350, "RP2350 を RP2350 と見分ける");
            Check(BootDrive.Probe(other) == null && BootDrive.Probe(empty) == null, "ほかの UF2 ブートローダーや空のドライブは無視する");

            Environment.SetEnvironmentVariable("RP2040AUTOFLASH_EXTRA_ROOTS", rp2040 + ";" + rp2350 + ";" + other);
            var without = BootDrive.Scan(false).Where(d => d.Root.StartsWith(work)).ToList();
            var with = BootDrive.Scan(true).Where(d => d.Root.StartsWith(work)).ToList();
            Environment.SetEnvironmentVariable("RP2040AUTOFLASH_EXTRA_ROOTS", null);
            Check(without.Count == 1 && without[0].Chip == Chip.RP2040, "RP2350 を対象にしないときは RP2040 だけ");
            Check(with.Count == 2, "RP2350 も対象にすると 2 台");
        }

        static void FlashSeesReboot(string work)
        {
            string root = MakeBoard(work, "flashReboot", "RPI-RP2");
            var data = MakeUf2(64, Uf2Image.FamilyRP2040);
            string dest = Path.Combine(root, "blink.uf2");

            // 書き終わったら INFO_UF2.TXT を消して、ボードの再起動（ドライブが消える）をまねる
            var rebooter = new Thread(() =>
            {
                while (!File.Exists(dest) || new FileInfo(dest).Length < data.Length) Thread.Sleep(20);
                File.Move(Path.Combine(root, "INFO_UF2.TXT"), Path.Combine(root, "INFO_UF2.old"));
            });
            rebooter.Start();

            var outcome = Flasher.Flash(Uf2Image.Parse(data), "blink.uf2", BootDrive.Probe(root), 5000);
            rebooter.Join();
            Check(outcome == FlashOutcome.Rebooted, "再起動したら Rebooted");
            Check(File.ReadAllBytes(dest).SequenceEqual(data), "書いた中身が UF2 と同じ");
        }

        static void FlashReportsNoReboot(string work)
        {
            string root = MakeBoard(work, "flashStay", "RPI-RP2");
            var outcome = Flasher.Flash(Uf2Image.Parse(MakeUf2(2, Uf2Image.FamilyRP2040)), "blink.uf2", BootDrive.Probe(root), 600);
            Check(outcome == FlashOutcome.NotRebooted, "再起動しなければ NotRebooted");
        }

        static string MakeBoard(string work, string name, string boardId)
        {
            string root = Path.Combine(work, name);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "INFO_UF2.TXT"), "UF2 Bootloader v3.0\r\nModel: Raspberry Pi RP2\r\nBoard-ID: " + boardId + "\r\n");
            return root;
        }

        static byte[] MakeUf2(int blocks, uint? family)
        {
            var data = new byte[blocks * 512];
            for (int i = 0; i < blocks; i++)
            {
                int off = i * 512;
                Put(data, off, 0x0A324655);
                Put(data, off + 4, 0x9E5D5157);
                Put(data, off + 8, family.HasValue ? 0x2000u : 0u);
                Put(data, off + 12, 0x10000000u + (uint)i * 256);
                Put(data, off + 16, 256);
                Put(data, off + 20, (uint)i);
                Put(data, off + 24, (uint)blocks);
                Put(data, off + 28, family ?? 0);
                Put(data, off + 508, 0x0AB16F30);
            }
            return data;
        }

        static void Put(byte[] data, int offset, uint value)
        {
            BitConverter.GetBytes(value).CopyTo(data, offset);
        }

        static bool Throws(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
        }

        static void Check(bool ok, string name)
        {
            Console.WriteLine((ok ? "ok   " : "FAIL ") + name);
            if (!ok) failures++;
        }
    }
}
