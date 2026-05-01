using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SPD_Checker.Logic
{
    public static class SpdFixer
    {
        // ── JEDEC SPD Byte Offsets ────────────────────────────────────────────
        private const int DRAM_TYPE_OFFSET   =   2;
        private const int MODULE_TYPE_OFFSET =   3;
        private const int DIE_DENSITY_OFFSET =   4;
        private const int IO_WIDTH_OFFSET    =   6;
        private const int BANK_OFFSET        =   7;
        private const int VDD_OFFSET         =  16;
        private const int TCK_AVG_MIN_OFFSET =  20;
        private const int TAA_MIN_OFFSET     =  30;
        private const int TRCD_MIN_OFFSET    =  32;
        private const int TRP_MIN_OFFSET     =  34;
        private const int RANK_OFFSET        = 234;
        private const int PART_NUMBER_OFFSET = 521;
        private const int PART_NUMBER_LENGTH =  30;
        private const int MODULE_MFR_OFFSET  = 512;
        private const int DRAM_MFR_OFFSET    = 552;

        // ── XMP Byte Offsets ──────────────────────────────────────────────────
        private const int XMP_GLOBAL_BASE    = 640;
        private const int XMP_P1_BASE        = 704;
        private const int XMP_P2_BASE        = 768;
        private const int XMP_P1_NAME_OFFSET = 654;
        private const int XMP_P2_NAME_OFFSET = 670;

        // ── Lookup Tables (SpdChecker와 동일 값 유지) ─────────────────────────
        private static readonly Dictionary<char, byte> DIMM_TYPE_MAP = new Dictionary<char, byte>
        {
            { 'S', 0x03 }, { 'D', 0x02 }, { 'G', 0x02 },
        };

        private static readonly Dictionary<char, byte> DIE_DENSITY_MAP = new Dictionary<char, byte>
        {
            { '4', 0x01 }, { '8', 0x02 }, { 'A', 0x04 }, { 'H', 0x05 }, { 'B', 0x06 },
        };

        private static readonly Dictionary<char, byte> IO_WIDTH_MAP = new Dictionary<char, byte>
        {
            { '4', 0x00 }, { '8', 0x20 }, { '6', 0x40 },
        };

        private static readonly Dictionary<char, byte> BANK_MAP = new Dictionary<char, byte>
        {
            { '4', 0x42 }, { '5', 0x62 }, { '6', 0x62 }, { '7', 0x62 },
        };

        private static readonly Dictionary<char, byte> RANK_MAP = new Dictionary<char, byte>
        {
            { '1', 0x00 }, { '2', 0x08 },
        };

        // RAmos Module Mfr JEDEC ID
        private const byte RAMOS_MFR_B1 = 0x07;
        private const byte RAMOS_MFR_B2 = 0x25;

        // DRAM Mfr 코드 (파일명 '-' 이후 첫 글자) → JEDEC ID
        private static readonly Dictionary<char, (byte b1, byte b2)> DRAM_MFR_MAP =
            new Dictionary<char, (byte, byte)>
            {
                { 'G', (0x07, 0x25) }, // RAmos
                { 'S', (0x07, 0x25) }, // RAmos
                { 'H', (0x80, 0xAD) }, // SK Hynix
                { 'N', (0x83, 0x0B) }, // Nanya
                { 'C', (0x8A, 0x91) }, // CXMT
                { 'M', (0x80, 0x2C) }, // Micron
            };

        // Speed 코드 → XMP VDD/VDDQ Byte
        private static readonly Dictionary<string, byte> SPEED_TO_VDD = new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            { "QK", 0x22 }, { "WM", 0x22 },  // 1.1V
            { "CM", 0x27 }, { "CQ", 0x27 },  // 1.35V
            { "CR", 0x28 }, { "CS", 0x28 },  // 1.4V
        };

        // XMP Profile 2 기준 Speed 코드 (파트 Speed → P2 Speed)
        private static readonly Dictionary<string, string> XMP_P2_SPEED =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "CQ", "CM" }, { "CR", "CQ" }, { "CS", "CR" },
            };

        // ── 직렬화: byte[] → .sp5 CSV Hex 텍스트 (16바이트/줄) ───────────────
        public static string SerializeToSp5(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 3 + 100);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(i % 16 == 0 ? "\r\n" : ",");
                sb.Append(data[i].ToString("X2"));
            }
            sb.Append("\r\n");
            return sb.ToString();
        }

        // JEDEC CRC 재계산: Bytes 0~509 → 510~511
        private static void RecalcJedecCrc(byte[] data)
        {
            if (data.Length < 512) return;
            ushort crc = SpdChecker.ComputeCrc16(data, 0, 510);
            data[510] = (byte)(crc & 0xFF);
            data[511] = (byte)(crc >> 8);
        }

        // XMP 섹션 CRC 재계산: base~base+61 (62bytes) → base+62~63
        private static void RecalcXmpSectionCrc(byte[] data, int baseOffset)
        {
            if (data.Length < baseOffset + 64) return;
            ushort crc = SpdChecker.ComputeCrc16(data, baseOffset, 62);
            data[baseOffset + 62] = (byte)(crc & 0xFF);
            data[baseOffset + 63] = (byte)(crc >> 8);
        }

        // ── 저장 ─────────────────────────────────────────────────────────────
        public static string SaveAsFixed(string originalPath, byte[] data)
        {
            string dir     = Path.GetDirectoryName(originalPath) ?? "";
            string name    = Path.GetFileNameWithoutExtension(originalPath);
            string ext     = Path.GetExtension(originalPath);
            string newPath = Path.Combine(dir, name + "_FIXED" + ext);
            File.WriteAllText(newPath, SerializeToSp5(data), Encoding.ASCII);
            return newPath;
        }

        public static void SaveOverwrite(string originalPath, byte[] data)
        {
            File.WriteAllText(originalPath, SerializeToSp5(data), Encoding.ASCII);
        }

        // ── 유틸 ─────────────────────────────────────────────────────────────
        private static void WriteLE16(byte[] data, int offset, int value)
        {
            data[offset]     = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static int ReadLE16(byte[] data, int offset)
            => data[offset] | (data[offset + 1] << 8);

        // ── Fix 메인 ─────────────────────────────────────────────────────────
        public static byte[] ApplyFixes(byte[] original, string filePath)
        {
            byte[] data      = (byte[])original.Clone();
            string nameNoExt = SpdChecker.StripSuffix(
                                   Path.GetFileNameWithoutExtension(filePath));
            var f = SpdChecker.ParsePartFields(nameNoExt);

            // P1: 고정값
            FixDramType(data);
            FixVddNominal(data);

            if (f.Valid)
            {
                // P2: 파일명 파생
                FixPartNumber(data, nameNoExt);
                FixModuleType(data, f);
                FixDieDensity(data, f);
                FixIoWidth(data, f);
                FixBankGroups(data, f);
                FixRank(data, f);

                // P3: Speed 파생
                if (f.SpeedCode != null)
                    FixJedecTimings(data, f);

                // P4: Mfr ID
                FixModuleMfrId(data);
                FixDramMfrId(data, f);
            }

            // JEDEC CRC — 항상 마지막
            RecalcJedecCrc(data);

            // P5: XMP (6000 이상 + 파일 크기 832 이상)
            if (f.Valid && f.SpeedCode != null &&
                SpdChecker.XMP_SPEED_CODES.Contains(f.SpeedCode) &&
                data.Length >= 832)
            {
                FixXmp(data, f);
            }

            return data;
        }

        // ── P1: 고정값 ────────────────────────────────────────────────────────
        private static void FixDramType(byte[] data)   => data[DRAM_TYPE_OFFSET] = 0x12;
        private static void FixVddNominal(byte[] data) => data[VDD_OFFSET]       = 0x00;

        // ── P2: 파일명 파생 ───────────────────────────────────────────────────
        private static void FixPartNumber(byte[] data, string partNo)
        {
            for (int i = 0; i < PART_NUMBER_LENGTH; i++) data[PART_NUMBER_OFFSET + i] = 0x20;
            byte[] ascii = Encoding.ASCII.GetBytes(partNo);
            Array.Copy(ascii, 0, data, PART_NUMBER_OFFSET, Math.Min(ascii.Length, PART_NUMBER_LENGTH));
        }

        private static void FixModuleType(byte[] data, SpdChecker.PartFields f)
        {
            if (!DIMM_TYPE_MAP.TryGetValue(f.DimmType, out byte val)) return;
            data[MODULE_TYPE_OFFSET] = (byte)((data[MODULE_TYPE_OFFSET] & 0xF0) | (val & 0x0F));
        }

        private static void FixDieDensity(byte[] data, SpdChecker.PartFields f)
        {
            if (!DIE_DENSITY_MAP.TryGetValue(f.DieDensityCode, out byte val)) return;
            // bits[4:0] 만 교체, bits[7:5] (Dies/Package) 보존
            data[DIE_DENSITY_OFFSET] = (byte)((data[DIE_DENSITY_OFFSET] & 0xE0) | (val & 0x1F));
        }

        private static void FixIoWidth(byte[] data, SpdChecker.PartFields f)
        {
            if (!IO_WIDTH_MAP.TryGetValue(f.CompositionCode, out byte val)) return;
            // bits[7:5] 만 교체, bits[4:0] 보존
            data[IO_WIDTH_OFFSET] = (byte)((data[IO_WIDTH_OFFSET] & 0x1F) | (val & 0xE0));
        }

        private static void FixBankGroups(byte[] data, SpdChecker.PartFields f)
        {
            if (!BANK_MAP.TryGetValue(f.BankCode, out byte val)) return;
            data[BANK_OFFSET] = val;
        }

        private static void FixRank(byte[] data, SpdChecker.PartFields f)
        {
            if (!RANK_MAP.TryGetValue(f.RankCode, out byte val)) return;
            // bits[5:3] 만 교체, 나머지 보존
            data[RANK_OFFSET] = (byte)((data[RANK_OFFSET] & 0xC7) | (val & 0x38));
        }

        // ── P3: JEDEC 타이밍 ──────────────────────────────────────────────────
        private static void FixJedecTimings(byte[] data, SpdChecker.PartFields f)
        {
            // 6000 이상 XMP 파트의 JEDEC SPD 타이밍은 WM(5600) 기준
            string jedecCode = SpdChecker.XMP_SPEED_CODES.Contains(f.SpeedCode) ? "WM" : f.SpeedCode;
            if (!SpdChecker.SPEED_MAP.TryGetValue(jedecCode, out SpdChecker.SpeedSpec spec)) return;

            WriteLE16(data, TCK_AVG_MIN_OFFSET, spec.TckAvgMin);

            int cl = spec.CL % 2 == 1 ? spec.CL + 1 : spec.CL;  // 홀수 CL → +1 보정
            WriteLE16(data, TAA_MIN_OFFSET,  cl           * spec.TckPs);
            WriteLE16(data, TRCD_MIN_OFFSET, spec.TrcdNck * spec.TckPs);
            WriteLE16(data, TRP_MIN_OFFSET,  spec.TrpNck  * spec.TckPs);
        }

        // ── P4: Mfr ID ────────────────────────────────────────────────────────
        private static void FixModuleMfrId(byte[] data)
        {
            if (data.Length < MODULE_MFR_OFFSET + 2) return;
            data[MODULE_MFR_OFFSET]     = RAMOS_MFR_B1;
            data[MODULE_MFR_OFFSET + 1] = RAMOS_MFR_B2;
        }

        private static void FixDramMfrId(byte[] data, SpdChecker.PartFields f)
        {
            if (data.Length < DRAM_MFR_OFFSET + 2) return;
            if (f.DramMfrCode == '\0') return;
            if (!DRAM_MFR_MAP.TryGetValue(f.DramMfrCode, out var mfr)) return;
            data[DRAM_MFR_OFFSET]     = mfr.b1;
            data[DRAM_MFR_OFFSET + 1] = mfr.b2;
        }

        // ── P5: XMP ───────────────────────────────────────────────────────────
        private static void FixXmp(byte[] data, SpdChecker.PartFields f)
        {
            data[XMP_GLOBAL_BASE + 0] = 0x0C;
            data[XMP_GLOBAL_BASE + 1] = 0x4A;
            data[XMP_GLOBAL_BASE + 2] = 0x30;

            data[XMP_GLOBAL_BASE + 3] = f.SpeedCode == "CM" ? (byte)0x01 : (byte)0x03;

            // Profile 1
            FixXmpProfile(data, f.SpeedCode, XMP_P1_BASE, XMP_P1_NAME_OFFSET);
            RecalcXmpSectionCrc(data, XMP_P1_BASE);

            // Profile 2 (CM 제외)
            if (XMP_P2_SPEED.TryGetValue(f.SpeedCode, out string p2Code))
            {
                FixXmpProfile(data, p2Code, XMP_P2_BASE, XMP_P2_NAME_OFFSET);
                RecalcXmpSectionCrc(data, XMP_P2_BASE);
            }

            // Global CRC: Bytes 640~701 → 702~703
            RecalcXmpSectionCrc(data, XMP_GLOBAL_BASE);
        }

        private static void FixXmpProfile(byte[] data, string speedCode,
                                           int baseOffset, int nameOffset)
        {
            if (!SpdChecker.SPEED_MAP.TryGetValue(speedCode, out SpdChecker.SpeedSpec spec)) return;
            if (!SPEED_TO_VDD.TryGetValue(speedCode, out byte vByte)) return;

            // VPP (BASE+0): 1.8V 고정
            data[baseOffset + 0] = 0x30;
            // VDD (BASE+1), VDDQ (BASE+2)
            data[baseOffset + 1] = vByte;
            data[baseOffset + 2] = vByte;

            // tCKAVGmin (BASE+5~6, LE)
            WriteLE16(data, baseOffset + 5, spec.TckAvgMin);

            int cl = spec.CL % 2 == 1 ? spec.CL + 1 : spec.CL;

            // tAAmin (BASE+13~14), tRCDmin (BASE+15~16), tRPmin (BASE+17~18)
            WriteLE16(data, baseOffset + 13, cl           * spec.TckPs);
            WriteLE16(data, baseOffset + 15, spec.TrcdNck * spec.TckPs);
            WriteLE16(data, baseOffset + 17, spec.TrpNck  * spec.TckPs);

            // Name String: "RM-[DataRate]-[CL]-[tRCD]-[tRAS]"
            // tRAS는 Fix 대상이 아니므로 기존 tRASmin 바이트에서 nCK 역산
            int tRasPs  = ReadLE16(data, baseOffset + 19);
            int tRasNck = spec.TckPs > 0 ? tRasPs / spec.TckPs : 0;
            int dataRate = 2_000_000 / spec.TckPs;
            string nameStr = $"RM-{dataRate}-{cl}-{spec.TrcdNck}-{tRasNck}";

            byte[] nameBytes = new byte[16];
            for (int i = 0; i < 16; i++) nameBytes[i] = 0x20;
            byte[] ascii   = Encoding.ASCII.GetBytes(nameStr);
            int    copyLen = Math.Min(ascii.Length, 16);
            Array.Copy(ascii, nameBytes, copyLen);
            Array.Copy(nameBytes, 0, data, nameOffset, 16);
        }
    }
}
