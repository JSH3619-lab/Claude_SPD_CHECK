using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SPD_Checker.Logic;

namespace SPD_Checker.Models
{
    /// <summary>
    /// SPD 파일의 byte[] + 파싱된 표시 정보 보관.
    /// Editor / Auto-Gen 모드에서 사용 (Check 모드는 SpdChecker가 직접 사용).
    /// </summary>
    internal class SpdInfo
    {
        // ── Lookup Tables (표시 전용) ─────────────────────────────────────────
        // JEP106 ID → 제조사 이름 (Module Mfr / DRAM Mfr 양쪽에서 사용)
        public static string LookupMfrName(byte b1, byte b2) =>
            MFR_NAMES.TryGetValue((b1, b2), out string n) ? n : null;

        private static readonly Dictionary<(byte, byte), string> MFR_NAMES =
            new Dictionary<(byte, byte), string>
            {
                { (0x07, 0x25), "RAmos"    },
                { (0x04, 0xCB), "ADATA"    },
                { (0x80, 0xCE), "Samsung"  },
                { (0x80, 0xAD), "SK Hynix" },
                { (0x80, 0x2C), "Micron"   },
                { (0x83, 0x0B), "Nanya"    },
                { (0x8A, 0x91), "CXMT"     },
                { (0x02, 0xB5), "Spectek"  },
            };

        private static readonly Dictionary<byte, string> MODULE_TYPE_NAMES =
            new Dictionary<byte, string>
            {
                { 0x01, "RDIMM"    },
                { 0x02, "UDIMM"    },
                { 0x03, "SODIMM"   },
            };

        // ── 원본 데이터 ───────────────────────────────────────────────────────
        public byte[] Data       { get; private set; }
        public string FileName   { get; private set; }
        public string SourcePath { get; private set; }

        // ── 파싱된 정보 (RefreshDerived로 갱신) ────────────────────────────────
        public string     PartNumberAscii { get; private set; }
        public PartFields Fields          { get; private set; }
        public bool       XmpEnabled      { get; private set; }

        public string TemplateFileName =>
            Fields.Valid ? SpdParser.BuildTemplateFileName(Fields) : null;

        public string SpeedName =>
            Fields.Valid && Fields.SpeedCode != null
            && SpdParser.SPEED_MAP.TryGetValue(Fields.SpeedCode, out var s)
                ? s.Name : null;

        // ── 표시 헬퍼 ─────────────────────────────────────────────────────────
        public int?   DensityGb       { get; private set; }
        public string ModuleMfrName   { get; private set; }
        public string DramMfrName     { get; private set; }
        public string ModuleTypeName  { get; private set; }
        public string RankName        { get; private set; }

        // ── Factory ───────────────────────────────────────────────────────────
        public static SpdInfo FromBytes(byte[] data, string fileName = null, string sourcePath = null)
        {
            var info = new SpdInfo
            {
                Data       = data ?? Array.Empty<byte>(),
                FileName   = fileName,
                SourcePath = sourcePath
            };
            info.RefreshDerived();
            return info;
        }

        public static SpdInfo FromFile(string filePath)
        {
            byte[] data = SpdParser.ParseFile(filePath);
            return FromBytes(data, Path.GetFileName(filePath), filePath);
        }

        // ── 재계산 (byte[] 수정 시 호출) ──────────────────────────────────────
        public void RefreshDerived()
        {
            // Part Number ASCII (Bytes 521~550)
            int needed = SpdParser.PART_NUMBER_OFFSET + SpdParser.PART_NUMBER_LENGTH;
            if (Data.Length >= needed)
            {
                PartNumberAscii = Encoding.ASCII.GetString(
                    Data, SpdParser.PART_NUMBER_OFFSET, SpdParser.PART_NUMBER_LENGTH)
                    .TrimEnd('\x20', '\x00');
                Fields = SpdParser.ParsePartFields(SpdParser.StripSuffix(PartNumberAscii));
            }
            else
            {
                PartNumberAscii = "";
                Fields = default;
            }

            XmpEnabled      = SpdParser.IsXmpEnabled(Data);
            ModuleMfrName   = LookupModuleMfr();
            DramMfrName     = LookupDramMfr();
            ModuleTypeName  = LookupModuleTypeName();
            RankName        = LookupRankName();
            DensityGb       = ComputeDensityGb();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private string LookupModuleMfr()
        {
            if (Data.Length < SpdParser.MODULE_MFR_OFFSET + 2) return null;
            byte b1 = Data[SpdParser.MODULE_MFR_OFFSET];
            byte b2 = Data[SpdParser.MODULE_MFR_OFFSET + 1];
            return MFR_NAMES.TryGetValue((b1, b2), out string name)
                ? $"{name} (0x{b1:X2}/0x{b2:X2})"
                : $"0x{b1:X2}/0x{b2:X2}";
        }

        private string LookupDramMfr()
        {
            if (Data.Length < SpdParser.DRAM_MFR_OFFSET + 2) return null;
            byte b1 = Data[SpdParser.DRAM_MFR_OFFSET];
            byte b2 = Data[SpdParser.DRAM_MFR_OFFSET + 1];
            return MFR_NAMES.TryGetValue((b1, b2), out string name)
                ? $"{name} (0x{b1:X2}/0x{b2:X2})"
                : $"0x{b1:X2}/0x{b2:X2}";
        }

        private string LookupModuleTypeName()
        {
            if (Data.Length < SpdParser.MODULE_TYPE_OFFSET + 1) return null;
            byte v = (byte)(Data[SpdParser.MODULE_TYPE_OFFSET] & 0x0F);
            return MODULE_TYPE_NAMES.TryGetValue(v, out string name) ? name : $"0x{v:X2}";
        }

        private string LookupRankName()
        {
            if (Data.Length < SpdParser.RANK_OFFSET + 1) return null;
            byte rankBits = (byte)((Data[SpdParser.RANK_OFFSET] >> 3) & 0x07);
            return $"{rankBits + 1} Rank";
        }

        private int? ComputeDensityGb()
        {
            if (Data.Length < SpdParser.RANK_OFFSET + 1) return null;
            byte b4   = Data[SpdParser.DIE_DENSITY_OFFSET];
            byte b6   = Data[SpdParser.IO_WIDTH_OFFSET];
            byte b234 = Data[SpdParser.RANK_OFFSET];

            byte densityCode = (byte)(b4 & 0x1F);
            byte diesPkgCode = (byte)((b4 >> 5) & 0x07);
            byte ioCode      = (byte)((b6 >> 5) & 0x07);
            byte rankBits    = (byte)((b234 >> 3) & 0x07);

            if (!SpdParser.DIE_DENSITY_GB_MAP.TryGetValue(densityCode, out int dieGb)) return null;
            if (!SpdParser.DIES_PER_PKG_MAP.TryGetValue(diesPkgCode,   out int dies))  return null;
            if (!SpdParser.IO_WIDTH_BITS_MAP.TryGetValue(ioCode,       out int ioBits)) return null;

            int rankCount = rankBits + 1;
            return (dieGb * dies * rankCount * 64) / (ioBits * 8);
        }
    }
}
