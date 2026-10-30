using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;

namespace RP2040AutoFlash
{
    // ビルド時に exe のアイコン（大きさ違いの PNG を束ねた .ico）を作る
    static class MakeIcon
    {
        static void Main(string[] args)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var pngs = new List<byte[]>();
            foreach (int size in sizes)
            {
                using (var bitmap = IconFactory.Draw(IconFactory.AutoColor, size))
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    pngs.Add(stream.ToArray());
                }
            }

            using (var writer = new BinaryWriter(File.Create(args[0])))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte side = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    writer.Write(side);
                    writer.Write(side);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(pngs[i].Length);
                    writer.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var png in pngs) writer.Write(png);
            }
        }
    }
}
