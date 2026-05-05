using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SPD_Checker.Logic
{
    // ── 공유 타입 ────────────────────────────────────────────────────────────
    internal struct PartFields
    {
        public string Sourcing;          // "RM" / "TM" / "CM" / "BM"  (Auto-Gen 템플릿명용)
        public char   DramTypeCode;      // 'R' (DDR5) / '4' (DDR4)    (Auto-Gen 템플릿명용)
        public char   DimmType;          // 'S' / 'D' / 'G' / 'C'
        public string DensityCode;       // "1G" / "2G" / "4G" / "8G" / "AG" / "BG" / "CG"
        public char   BankCode;          // '4' / '5' / '6' / '7'
        public char   CompositionCode;   // '4' / '8' / '6'  (X4 / X8 / X16)
        public char   DieDensityCode;    // '4' / '8' / 'A' / 'H' / 'B'
        public char   RankCode;          // '0' / '1' / '2'
        public char   DramMfrCode;       // '-' 이후 첫 글자 (G/S/H/N/C/M)
        public string SpeedCode;         // "QK" / "WM" / "CM" / "CP" / "CQ" / "CR" / "CS"  (null = 미검출)
        public bool   Valid;
        public string Error;
    }

    internal struct SpeedSpec
    {
        public string Name;
        public int    TckPs;
        public int    TckAvgMin;
        public int    CL;
        public int    TrcdNck;
        public int    TrpNck;
    }

    // ── 파싱 / CRC / 명명 규칙 (Check / Editor / Auto-Gen 공유) ──────────────
    internal static class SpdParser
    {
        // ── Byte Offsets (JESD400-5C / XMP 3.0) ──────────────────────────────
        public const int PART_NUMBER_OFFSET   = 521;   // 0x209
        public const int PART_NUMBER_LENGTH   = 30;    // Bytes 521~550
        public const int MODULE_MFR_OFFSET    = 512;   // 0x200
        public const int DRAM_MFR_OFFSET      = 552;   // 0x228
        public const int CRC_OFFSET           = 510;   // 0x1FE

        public const int DRAM_TYPE_OFFSET     =   2;
        public const int MODULE_TYPE_OFFSET   =   3;
        public const int DIE_DENSITY_OFFSET   =   4;
        public const int IO_WIDTH_OFFSET      =   6;
        public const int BANK_OFFSET          =   7;
        public const int VDD_OFFSET           =  16;
        public const int TCK_AVG_MIN_OFFSET   =  20;
        public const int TAA_MIN_OFFSET       =  30;
        public const int TRCD_MIN_OFFSET      =  32;
        public const int TRP_MIN_OFFSET       =  34;
        public const int RANK_OFFSET          = 234;

        public const int XMP_ID_OFFSET        = 640;   // 0x280
        public const int XMP_GLOBAL_BASE      = 640;
        public const int XMP_P1_BASE          = 704;   // 0x2C0
        public const int XMP_P2_BASE          = 768;   // 0x300
        public const int XMP_P1_NAME_OFFSET   = 654;
        public const int XMP_P2_NAME_OFFSET   = 670;

        public const int SPD_FULL_SIZE        = 1024;

        // ── Speed Map (Speed 코드 → 기대 spec) ───────────────────────────────
        internal static readonly Dictionary<string, SpeedSpec> SPEED_MAP =
            new Dictionary<string, SpeedSpec>(StringComparer.Ordinal)
            {
                { "QK", new SpeedSpec { Name="DDR5-4800", TckPs=416, TckAvgMin=0x01A0, CL=40, TrcdNck=39, TrpNck=39 } },
                { "WM", new SpeedSpec { Name="DDR5-5600", TckPs=357, TckAvgMin=0x0165, CL=46, TrcdNck=45, TrpNck=45 } },
                { "CM", new SpeedSpec { Name="DDR5-6000", TckPs=333, TckAvgMin=0x014D, CL=34, TrcdNck=44, TrpNck=44 } },
                { "CP", new SpeedSpec { Name="DDR5-6400", TckPs=312, TckAvgMin=0x0138, CL=52, TrcdNck=52, TrpNck=52 } },
                { "CQ", new SpeedSpec { Name="DDR5-6400", TckPs=312, TckAvgMin=0x0138, CL=36, TrcdNck=44, TrpNck=44 } },
                { "CR", new SpeedSpec { Name="DDR5-6800", TckPs=294, TckAvgMin=0x0126, CL=36, TrcdNck=44, TrpNck=44 } },
                { "CS", new SpeedSpec { Name="DDR5-7200", TckPs=277, TckAvgMin=0x0115, CL=38, TrcdNck=46, TrpNck=46 } },
            };

        // 6000 이상 — XMP 활성 코드
        internal static readonly HashSet<string> XMP_SPEED_CODES =
            new HashSet<string>(StringComparer.Ordinal) { "CM", "CQ", "CR", "CS" };

        // ── Byte → 의미 단위 변환 (Density 계산 등 표시·검증 공유) ───────────
        internal static readonly Dictionary<byte, int> DIE_DENSITY_GB_MAP =
            new Dictionary<byte, int>
            {
                { 0x01, 4 }, { 0x02, 8 }, { 0x04, 16 }, { 0x05, 24 }, { 0x06, 32 },
            };

        internal static readonly Dictionary<byte, int> DIES_PER_PKG_MAP =
            new Dictionary<byte, int>
            {
                { 0, 1 }, { 1, 2 }, { 2, 2 }, { 3, 4 }, { 4, 8 }, { 5, 16 },
            };

        internal static readonly Dictionary<byte, int> IO_WIDTH_BITS_MAP =
            new Dictionary<byte, int>
            {
                { 0, 4 }, { 1, 8 }, { 2, 16 },
            };

        // ── 파일 → byte[] (.sp5 CSV hex / .bin 모두 지원) ───────────────────
        public static byte[] ParseFile(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase))
                return File.ReadAllBytes(filePath);

            // .sp5 (또는 그 외): CSV hex 텍스트로 파싱
            string text = File.ReadAllText(filePath, Encoding.ASCII);
            string[] tokens = text.Split(
                new[] { ',', '\r', '\n', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            var bytes = new List<byte>(SPD_FULL_SIZE);
            foreach (string token in tokens)
            {
                string t = token.Trim();
                if (t.Length == 0) continue;
                bytes.Add(Convert.ToByte(t, 16));
            }
            return bytes.ToArray();
        }

        // ── 파일명 접미사 제거 ("0Y", "-TN") ────────────────────────────────
        public static string StripSuffix(string nameNoExt)
        {
            if (nameNoExt.EndsWith("-TN", StringComparison.OrdinalIgnoreCase))
                nameNoExt = nameNoExt.Substring(0, nameNoExt.Length - 3);
            if (nameNoExt.EndsWith("0Y", StringComparison.OrdinalIgnoreCase))
                nameNoExt = nameNoExt.Substring(0, nameNoExt.Length - 2);
            return nameNoExt;
        }

        // ── Part Number → PartFields ────────────────────────────────────────
        // 입력: "RMRDAG58A1P-GPWRRWM7" 형태 (StripSuffix 이후 권장)
        public static PartFields ParsePartFields(string partNoFromName)
        {
            var f = new PartFields();
            if (string.IsNullOrEmpty(partNoFromName))
            {
                f.Error = "Part Number 비어 있음";
                return f;
            }

            int dashIdx = partNoFromName.IndexOf('-');
            string body = dashIdx >= 0
                ? partNoFromName.Substring(0, dashIdx)
                : partNoFromName;

            // prefix(2) + core 분리
            if (body.Length < 2)
            {
                f.Error = "본체 너무 짧음";
                return f;
            }
            f.Sourcing = body.Substring(0, 2).ToUpper();
            string core = body.Substring(2);

            // core: [0]=DRAMType [1]=DimmType [2~3]=Density [4]=Bank [5]=Comp [6]=DieDensity [7]=Rank
            if (core.Length < 8)
            {
                f.Error = $"파트 본체 길이 부족 ({core.Length} < 8)";
                return f;
            }

            f.DramTypeCode    = char.ToUpper(core[0]);
            f.DimmType        = char.ToUpper(core[1]);
            f.DensityCode     = core.Substring(2, 2).ToUpper();
            f.BankCode        = char.ToUpper(core[4]);
            f.CompositionCode = char.ToUpper(core[5]);
            f.DieDensityCode  = char.ToUpper(core[6]);
            f.RankCode        = core[7];

            // suffix: '-' 이후 → DRAM Mfr 첫 글자 + Speed 코드 탐색
            if (dashIdx >= 0 && dashIdx + 1 < partNoFromName.Length)
            {
                string suffix = partNoFromName.Substring(dashIdx + 1);
                if (suffix.Length > 0)
                    f.DramMfrCode = char.ToUpper(suffix[0]);

                foreach (string code in SPEED_MAP.Keys)
                {
                    if (suffix.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        f.SpeedCode = code;
                        break;
                    }
                }
            }

            f.Valid = true;
            return f;
        }

        // ── 골든 샘플 템플릿 파일명 조립 ────────────────────────────────────
        // 형식: TPL_{Src}_{DRAM}{DIMM}{Density}{Bank}{IO}{Die}{Rank}_{Speed}.sp5
        // 예: RMRDAG58A1P-GPWRRWM7-TN → TPL_RM_RDAG58A1_WM.sp5
        public static string BuildTemplateFileName(PartFields f)
        {
            if (!f.Valid)
                return null;

            string speed = f.SpeedCode ?? "??";
            return $"TPL_{f.Sourcing}_{f.DramTypeCode}{f.DimmType}{f.DensityCode}" +
                   $"{f.BankCode}{f.CompositionCode}{f.DieDensityCode}{f.RankCode}_" +
                   $"{speed}.sp5";
        }

        // ── CRC-16 (poly=0x1021, init=0x0000) ───────────────────────────────
        // JEDEC SPD / XMP 공통
        public static ushort ComputeCrc16(byte[] data, int offset, int length)
        {
            ushort crc = 0x0000;
            for (int i = offset; i < offset + length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                    crc = (crc & 0x8000) != 0
                        ? (ushort)((crc << 1) ^ 0x1021)
                        : (ushort)(crc << 1);
            }
            return crc;
        }

        // ── XMP 3.0 활성 여부 (Byte 640=0x0C, 641=0x4A, 642=0x30) ───────────
        public static bool IsXmpEnabled(byte[] data)
        {
            if (data == null || data.Length < XMP_ID_OFFSET + 3) return false;
            return data[XMP_ID_OFFSET]     == 0x0C
                && data[XMP_ID_OFFSET + 1] == 0x4A
                && data[XMP_ID_OFFSET + 2] == 0x30;
        }
    }
}
