using System.Diagnostics;
using System.IO;
using System.Threading;

namespace RP2040AutoFlash
{
    enum FlashOutcome { Rebooted, NotRebooted }

    static class Flasher
    {
        // UF2 をドライブへ書き、ボードが再起動して（ドライブが消えて）くれるかを見届ける
        public static FlashOutcome Flash(Uf2Image image, string fileName, BootDrive drive, int rebootWaitMs = 10000)
        {
            string dest = Path.Combine(drive.Root, fileName);
            try
            {
                using (var stream = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    stream.Write(image.Data, 0, image.Data.Length);
            }
            catch (IOException)
            {
                // 最後のブロックを受け取るとボードはすぐ再起動するので、閉じる処理が失敗することがある。
                // そのままドライブが消えたなら書き込めている
                if (WaitForRemoval(drive, 3000)) return FlashOutcome.Rebooted;
                throw;
            }
            return WaitForRemoval(drive, rebootWaitMs) ? FlashOutcome.Rebooted : FlashOutcome.NotRebooted;
        }

        static bool WaitForRemoval(BootDrive drive, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (drive.StillPresent)
            {
                if (watch.ElapsedMilliseconds >= timeoutMs) return false;
                Thread.Sleep(200);
            }
            return true;
        }
    }
}
