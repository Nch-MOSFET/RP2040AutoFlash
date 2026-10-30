using System;
using System.Collections.Generic;
using System.IO;

namespace RP2040AutoFlash
{
    enum Chip { RP2040, RP2350 }

    // UF2 ファイルの中身を確かめ、どのチップ向けかを読み取る
    class Uf2Image
    {
        const int BlockSize = 512;
        const uint MagicStart0 = 0x0A324655;
        const uint MagicStart1 = 0x9E5D5157;
        const uint MagicEnd = 0x0AB16F30;
        const uint FlagNotMainFlash = 0x00000001;
        const uint FlagFamilyIdPresent = 0x00002000;

        public const uint FamilyRP2040 = 0xE48BFF56;
        public const uint FamilyAbsolute = 0xE48BFF57;
        public const uint FamilyData = 0xE48BFF58;
        public const uint FamilyRP2350ArmS = 0xE48BFF59;
        public const uint FamilyRP2350RiscV = 0xE48BFF5A;
        public const uint FamilyRP2350ArmNs = 0xE48BFF5B;

        readonly HashSet<uint> families = new HashSet<uint>();

        public byte[] Data { get; private set; }
        public int BlockCount { get; private set; }
        public long PayloadBytes { get; private set; }

        public static Uf2Image Load(string path)
        {
            return Parse(File.ReadAllBytes(path));
        }

        public static Uf2Image Parse(byte[] data)
        {
            if (data.Length == 0 || data.Length % BlockSize != 0)
                throw new InvalidDataException("UF2 ファイルではありません（大きさが 512 バイト単位ではありません）");

            var image = new Uf2Image { Data = data, BlockCount = data.Length / BlockSize };
            for (int off = 0; off < data.Length; off += BlockSize)
            {
                if (BitConverter.ToUInt32(data, off) != MagicStart0
                    || BitConverter.ToUInt32(data, off + 4) != MagicStart1
                    || BitConverter.ToUInt32(data, off + 508) != MagicEnd)
                    throw new InvalidDataException(string.Format("UF2 ファイルではありません（{0} 番目のブロックが壊れています）", off / BlockSize + 1));

                uint flags = BitConverter.ToUInt32(data, off + 8);
                if ((flags & FlagNotMainFlash) != 0) continue;
                image.PayloadBytes += BitConverter.ToUInt32(data, off + 16);
                if ((flags & FlagFamilyIdPresent) != 0) image.families.Add(BitConverter.ToUInt32(data, off + 28));
            }
            return image;
        }

        // ブートローダーは自分向けでない family ID のブロックを無視するので、書いても起動しない
        public bool SupportsChip(Chip chip)
        {
            if (families.Count == 0) return true;
            if (chip == Chip.RP2040) return families.Contains(FamilyRP2040);
            return families.Contains(FamilyRP2350ArmS) || families.Contains(FamilyRP2350RiscV)
                || families.Contains(FamilyRP2350ArmNs) || families.Contains(FamilyAbsolute) || families.Contains(FamilyData);
        }

        // 画面に出す「〇〇 用」の部分
        public string FamilyText
        {
            get
            {
                if (families.Count == 0) return "チップ指定なし";
                var names = new List<string>();
                if (families.Contains(FamilyRP2040)) names.Add("RP2040");
                if (families.Contains(FamilyRP2350ArmS) || families.Contains(FamilyRP2350ArmNs)) names.Add("RP2350（ARM）");
                if (families.Contains(FamilyRP2350RiscV)) names.Add("RP2350（RISC-V）");
                if (names.Count == 0 && (families.Contains(FamilyAbsolute) || families.Contains(FamilyData))) names.Add("RP2350");
                if (names.Count == 0)
                    foreach (uint family in families) names.Add(string.Format("不明なチップ（0x{0:X8}）", family));
                return string.Join("・", names);
            }
        }
    }
}
