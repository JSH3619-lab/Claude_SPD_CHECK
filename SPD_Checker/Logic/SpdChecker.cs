using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SPD_Checker.Models;

namespace SPD_Checker.Logic
{
    public static class SpdChecker
    {
        // ── JESD400-5C Byte Offsets ──────────────────────────────────────────
        private const int PART_NUMBER_OFFSET = 521;   // 0x209
        private const int PART_NUMBER_LENGTH = 30;    // Bytes 521~550

        // ── 표준 파일 접두사 ─────────────────────────────────────────────────
        private static readonly string[] STANDARD_PREFIXES = { "RM", "TM", "CM", "BM" };

        // ── Module Mfr 식별 맵 (Bytes 512~513 → 제조사명) ────────────────────
        // "RAmos" 만 검사 진행, 나머지는 Skip
        private static readonly Dictionary<(byte, byte), string> MODULE_MFR_IDENTIFY =
            new Dictionary<(byte, byte), string>
            {
                { (0x07, 0x25), "RAmos"    },
                { (0x04, 0xCB), "ADATA"    },
                { (0x80, 0xCE), "Samsung"  },
                { (0x80, 0xAD), "SK Hynix" },
                { (0x80, 0x2C), "Micron"   },
            };

        // ── Public Entry Point ───────────────────────────────────────────────
        public static List<CheckResult> CheckFile(string filePath)
        {
            var    results   = new List<CheckResult>();
            string fileName  = Path.GetFileName(filePath);
            string nameNoExt = Path.GetFileNameWithoutExtension(filePath);

            // ── 확장자 확인 (.sp5 전용) ──────────────────────────────────────
            string ext = Path.GetExtension(filePath);
            if (!string.Equals(ext, ".sp5", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "File Type",
                    Expected  = ".sp5",
                    Actual    = string.IsNullOrEmpty(ext) ? "(확장자 없음)" : ext,
                    Pass      = false,
                    Status    = CheckStatus.Skip,
                    Note      = "SPD 파일(.sp5)이 아님 — 검사 생략"
                });
                return results;
            }

            // ── 파일 파싱 (CSV Hex 텍스트 → byte[]) ─────────────────────────
            byte[] data;
            try
            {
                data = ParseSpdText(filePath);
            }
            catch (Exception ex)
            {
                results.Add(new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "File Parse",
                    Expected  = "-",
                    Actual    = "ERROR: " + ex.Message,
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "파일 파싱 실패"
                });
                return results;
            }

            // ── 최소 크기 확인 (Module Mfr ID 읽기에 514 bytes 필요) ─────────
            if (data.Length < 514)
            {
                results.Add(new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "File Size",
                    Expected  = ">= 514 bytes",
                    Actual    = $"{data.Length} bytes",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "SPD 크기 부족"
                });
                return results;
            }

            // ── Module Mfr ID 라우팅 (Bytes 512~513) ─────────────────────────
            byte mfrB1 = data[MODULE_MFR_OFFSET];
            byte mfrB2 = data[MODULE_MFR_OFFSET + 1];

            if (MODULE_MFR_IDENTIFY.TryGetValue((mfrB1, mfrB2), out string mfrName))
            {
                if (mfrName != "RAmos")
                {
                    results.Add(new CheckResult
                    {
                        FileName  = fileName,
                        CheckItem = "Module Mfr ID",
                        Expected  = "-",
                        Actual    = $"0x{mfrB1:X2} / 0x{mfrB2:X2}  ({mfrName})",
                        Pass      = false,
                        Status    = CheckStatus.Skip,
                        Note      = $"{mfrName} 모듈 — 검사 생략"
                    });
                    return results;
                }
                // mfrName == "RAmos" → 계속 진행
            }
            else
            {
                // 미등록 제조사
                results.Add(new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "Module Mfr ID",
                    Expected  = "-",
                    Actual    = $"0x{mfrB1:X2} / 0x{mfrB2:X2}",
                    Pass      = false,
                    Status    = CheckStatus.Skip,
                    Note      = $"0x{mfrB1:X2}/0x{mfrB2:X2} 미등록 제조사 — 검사 생략"
                });
                return results;
            }

            // ── RAmos 모듈: 파일명 prefix 확인 (RM/TM/CM/BM) ────────────────
            bool isStandard = STANDARD_PREFIXES.Any(p =>
                nameNoExt.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            if (!isStandard)
            {
                results.Add(new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "Part Number",
                    Expected  = "RM / TM / CM / BM prefix",
                    Actual    = nameNoExt,
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "RAmos 모듈이지만 파일명 비표준 — Part Err"
                });
                return results;
            }

            // ── 전체 크기 확인 (DRAM Mfr ID 553까지 필요) ───────────────────
            int minRequired = DRAM_MFR_OFFSET + 2;   // 554 = 552 + 2
            if (data.Length < minRequired)
            {
                results.Add(new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "File Size",
                    Expected  = $">= {minRequired} bytes",
                    Actual    = $"{data.Length} bytes",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "SPD 크기 부족"
                });
                return results;
            }

            // ── Phase 1: Part Number ─────────────────────────────────────────
            string partNumberFromName = StripSuffix(nameNoExt);
            results.Add(CheckPartNumber(fileName, partNumberFromName, data));

            // ── Phase 2: Manufacturer ID ─────────────────────────────────────
            results.Add(CheckModuleMfr(fileName, data));
            results.Add(CheckDramMfr(fileName, partNumberFromName, data));

            // ── Phase 3: Part Content Validation ─────────────────────────────
            PartFields fields = ParsePartFields(partNumberFromName);
            if (!fields.Valid)
            {
                results.Add(new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "Part Parse (Phase 3)",
                    Expected  = "-",
                    Actual    = fields.Error,
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "파트 넘버 파싱 실패 — Phase 3 검사 생략"
                });
            }
            else
            {
                results.Add(CheckDramType(fileName, data));
                results.Add(CheckModuleType(fileName, fields, data));
                if (fields.SpeedCode != null && XMP_SPEED_CODES.Contains(fields.SpeedCode))
                    results.Add(CheckXmpDimmType(fileName, fields));
                results.Add(CheckDieDensity(fileName, fields, data));
                results.Add(CheckIoWidth(fileName, fields, data));
                results.Add(CheckBankGroups(fileName, fields, data));
                results.Add(CheckVdd(fileName, data));
                results.AddRange(CheckSpeed(fileName, fields, data));
                results.Add(CheckRank(fileName, fields, data));
                results.Add(CheckModuleDensity(fileName, fields, data));
            }

            // ── Phase 4: CRC ─────────────────────────────────────────────────
            results.AddRange(CheckCrc(fileName, data));

            // ── XMP 3.0 검증 (6000 이상 속도 코드 파트만) ────────────────────
            if (fields.Valid && fields.SpeedCode != null && XMP_SPEED_CODES.Contains(fields.SpeedCode))
                results.AddRange(CheckXmp(fileName, fields, data));

            return results;
        }

        // ── CSV Hex 텍스트 파싱 ──────────────────────────────────────────────
        // 형식: "30,12,02,..." CRLF 구분, 16바이트/줄, 64줄 = 1024 논리 바이트
        internal static byte[] ParseSpdText(string filePath)
        {
            string text = File.ReadAllText(filePath, Encoding.ASCII);

            var tokens = text
                .Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var bytes = new List<byte>(1024);
            foreach (string token in tokens)
            {
                string t = token.Trim();
                if (t.Length == 0) continue;
                bytes.Add(Convert.ToByte(t, 16));
            }
            return bytes.ToArray();
        }

        // ── 파일명 접미사 제거 ───────────────────────────────────────────────
        // "0Y", "-TN" 등 SPD에 포함되지 않는 접미사 제거
        internal static string StripSuffix(string nameNoExt)
        {
            if (nameNoExt.EndsWith("-TN", StringComparison.OrdinalIgnoreCase))
                nameNoExt = nameNoExt.Substring(0, nameNoExt.Length - 3);
            if (nameNoExt.EndsWith("0Y", StringComparison.OrdinalIgnoreCase))
                nameNoExt = nameNoExt.Substring(0, nameNoExt.Length - 2);
            return nameNoExt;
        }

        // ── Phase 1: Part Number ─────────────────────────────────────────────
        // JESD400-5C §20.5  Bytes 521~550 (0x209~0x226)
        private static CheckResult CheckPartNumber(
            string fileName, string expectedPartNumber, byte[] data)
        {
            byte[] pnBytes = new byte[PART_NUMBER_LENGTH];
            Array.Copy(data, PART_NUMBER_OFFSET, pnBytes, 0, PART_NUMBER_LENGTH);

            string actualTrim = Encoding.ASCII.GetString(pnBytes).TrimEnd('\x20', '\x00');
            string actualNorm = StripSuffix(actualTrim);
            bool   pass       = string.Equals(expectedPartNumber, actualNorm, StringComparison.Ordinal);
            string hexDump    = BitConverter.ToString(pnBytes).Replace("-", " ");

            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "Part Number",
                Expected  = expectedPartNumber,
                Actual    = actualTrim,
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 521~550 | {hexDump}"
            };
        }

        // ── Phase 2: Manufacturer ID ─────────────────────────────────────────
        // Module Mfr : Byte 512~513 (고정: RAmos 0x07/0x25)
        // DRAM Mfr   : Byte 552~553 (파일명 첫 '-' 이후 첫 글자로 결정)

        private const int MODULE_MFR_OFFSET = 512;   // 0x200
        private const int DRAM_MFR_OFFSET   = 552;   // 0x228

        // Module Mfr 고정값 (RAmos Technology)
        private static readonly byte MODULE_MFR_B1 = 0x07;
        private static readonly byte MODULE_MFR_B2 = 0x25;

        // DRAM Mfr 매핑 (파일명 첫 '-' 이후 첫 글자 → Byte552, Byte553)
        // 복수 허용값: 같은 계열 DRAM을 공유하는 경우 배열로 등록
        private static readonly Dictionary<char, (byte B1, byte B2, string Name)[]> DRAM_MFR_MAP =
            new Dictionary<char, (byte, byte, string)[]>
            {
                { 'G', new (byte, byte, string)[] { (0x07, 0x25, "RAmos")  } },
                { 'S', new (byte, byte, string)[] { (0x07, 0x25, "RAmos")  } },
                { 'H', new (byte, byte, string)[] { (0x80, 0xAD, "SK Hynix") } },
                { 'N', new (byte, byte, string)[] { (0x83, 0x0B, "Nanya")  } },
                { 'C', new (byte, byte, string)[] { (0x8A, 0x91, "CXMT")   } },
                { 'M', new (byte, byte, string)[] { (0x80, 0x2C, "Micron"), (0x02, 0xB5, "Spectek") } },
            };

        private static CheckResult CheckModuleMfr(string fileName, byte[] data)
        {
            byte actual1 = data[MODULE_MFR_OFFSET];
            byte actual2 = data[MODULE_MFR_OFFSET + 1];
            bool pass    = actual1 == MODULE_MFR_B1 && actual2 == MODULE_MFR_B2;

            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "Module Mfr ID",
                Expected  = $"0x{MODULE_MFR_B1:X2} / 0x{MODULE_MFR_B2:X2}  (RAmos)",
                Actual    = $"0x{actual1:X2} / 0x{actual2:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = "Byte 512~513 (0x200~0x201)"
            };
        }

        // ── Phase 3: Byte Offsets ────────────────────────────────────────────
        private const int DRAM_TYPE_OFFSET    =   2;   // 0x002
        private const int MODULE_TYPE_OFFSET  =   3;   // 0x003
        private const int DIE_DENSITY_OFFSET  =   4;   // 0x004
        private const int IO_WIDTH_OFFSET     =   6;   // 0x006
        private const int BANK_OFFSET         =   7;   // 0x007
        private const int VDD_OFFSET          =  16;   // 0x010
        private const int TCK_AVG_MIN_OFFSET  =  20;   // 0x014 (LSB)
        private const int TAA_MIN_OFFSET      =  30;   // 0x01E (LSB)
        private const int TRCD_MIN_OFFSET     =  32;   // 0x020 (LSB)
        private const int TRP_MIN_OFFSET      =  34;   // 0x022 (LSB)
        private const int RANK_OFFSET         = 234;   // 0x0EA

        // ── Phase 3: Lookup Tables ───────────────────────────────────────────
        private static readonly Dictionary<char, byte> DIMM_TYPE_MAP =
            new Dictionary<char, byte>
            {
                { 'S', 0x03 },   // SODIMM
                { 'D', 0x02 },   // UDIMM
                { 'G', 0x02 },   // Gaming UDIMM (JEDEC 동일)
            };

        private static readonly Dictionary<char, byte> DIE_DENSITY_MAP =
            new Dictionary<char, byte>
            {
                { '4', 0x01 },   // 4 Gb
                { '8', 0x02 },   // 8 Gb
                { 'A', 0x04 },   // 16 Gb
                { 'H', 0x05 },   // 24 Gb
                { 'B', 0x06 },   // 32 Gb
            };

        private static readonly Dictionary<char, byte> IO_WIDTH_MAP =
            new Dictionary<char, byte>
            {
                { '4', 0x00 },   // x4  bits[7:5]=000
                { '8', 0x20 },   // x8  bits[7:5]=001
                { '6', 0x40 },   // x16 bits[7:5]=010
            };

        private static readonly Dictionary<char, byte> BANK_MAP =
            new Dictionary<char, byte>
            {
                { '4', 0x42 },   // 16 Bank / POD 1.2V  (4BG × 4B/BG)
                { '5', 0x62 },   // 32 Bank / POD 1.1V  (8BG × 4B/BG)
                { '6', 0x62 },   // 32 Bank / POD 1.35V (8BG × 4B/BG)
                { '7', 0x62 },   // 32 Bank / POD 1.4V  (8BG × 4B/BG)
            };

        private static readonly Dictionary<char, byte> RANK_MAP =
            new Dictionary<char, byte>
            {
                { '1', 0x00 },   // 1 Rank  bits[5:3]=000
                { '2', 0x08 },   // 2 Rank  bits[5:3]=001
            };

        private static readonly Dictionary<string, int> DENSITY_CODE_GB_MAP =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "1G",  1 }, { "2G",  2 }, { "4G",  4 }, { "8G",  8 },
                { "AG", 16 }, { "BG", 32 }, { "CG", 64 },
            };

        private static readonly Dictionary<byte, int> DIE_DENSITY_GB_MAP =
            new Dictionary<byte, int>
            {
                { 0x01,  4 }, { 0x02,  8 }, { 0x04, 16 }, { 0x05, 24 }, { 0x06, 32 },
            };

        private static readonly Dictionary<byte, int> DIES_PER_PKG_MAP =
            new Dictionary<byte, int>
            {
                { 0, 1 }, { 1, 2 }, { 2, 2 }, { 3, 4 }, { 4, 8 }, { 5, 16 },
            };

        private static readonly Dictionary<byte, int> IO_WIDTH_BITS_MAP =
            new Dictionary<byte, int>
            {
                { 0, 4 }, { 1, 8 }, { 2, 16 },
            };

        internal struct SpeedSpec
        {
            public string Name;
            public int    TckPs;
            public int    TckAvgMin;
            public int    CL;
            public int    TrcdNck;
            public int    TrpNck;
        }

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

        // XMP 속도 코드 (6000 이상) — JEDEC SPD는 WM(5600) 기준으로 검증
        internal static readonly HashSet<string> XMP_SPEED_CODES =
            new HashSet<string>(StringComparer.Ordinal) { "CM", "CQ", "CR", "CS" };

        // ── Phase 3: Part Number Field Parser ────────────────────────────────
        internal struct PartFields
        {
            public char   DimmType;
            public string DensityCode;
            public char   BankCode;
            public char   CompositionCode;
            public char   DieDensityCode;
            public char   RankCode;
            public char   DramMfrCode;   // '-' 이후 첫 글자 (G/S/H/N/C/M)
            public string SpeedCode;     // null = 미검출
            public bool   Valid;
            public string Error;
        }

        internal static PartFields ParsePartFields(string partNoFromName)
        {
            var f = new PartFields();

            int dashIdx = partNoFromName.IndexOf('-');
            string body = dashIdx >= 0
                ? partNoFromName.Substring(0, dashIdx)
                : partNoFromName;

            // prefix(2) 제거 후 body core
            if (body.Length < 2) { f.Error = "본체 너무 짧음"; return f; }
            string core = body.Substring(2);

            // core: [0]=DRAMType [1]=DimmType [2~3]=Density [4]=Bank [5]=Comp [6]=DieDensity [7]=Rank
            if (core.Length < 8) { f.Error = $"파트 본체 길이 부족 ({core.Length} < 8)"; return f; }

            f.DimmType        = char.ToUpper(core[1]);
            f.DensityCode     = core.Substring(2, 2).ToUpper();
            f.BankCode        = char.ToUpper(core[4]);
            f.CompositionCode = char.ToUpper(core[5]);
            f.DieDensityCode  = char.ToUpper(core[6]);
            f.RankCode        = core[7];

            // Speed 코드 + DRAM Mfr 코드: '-' 이후 문자열에서 파싱
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

        // ── Phase 3: Check Methods ───────────────────────────────────────────

        private static CheckResult CheckDramType(string fileName, byte[] data)
        {
            const byte EXPECTED = 0x12;
            byte actual = data[DRAM_TYPE_OFFSET];
            bool pass   = actual == EXPECTED;
            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "DRAM Type",
                Expected  = $"0x{EXPECTED:X2} (DDR5 SDRAM)",
                Actual    = $"0x{actual:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = "Byte 2 (0x002)"
            };
        }

        private static CheckResult CheckModuleType(string fileName, PartFields f, byte[] data)
        {
            byte actual = (byte)(data[MODULE_TYPE_OFFSET] & 0x0F);

            if (!DIMM_TYPE_MAP.TryGetValue(f.DimmType, out byte expected))
                return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "Module Type",
                    Expected  = $"UNKNOWN (DimmType='{f.DimmType}')",
                    Actual    = $"0x{actual:X2}",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "Byte 3 (0x003) bits[3:0] | 미정의 DIMM Type 코드"
                };

            bool   pass = actual == expected;
            string name = f.DimmType == 'S' ? "SODIMM" : (f.DimmType == 'G' ? "Gaming UDIMM" : "UDIMM");
            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "Module Type",
                Expected  = $"0x{expected:X2} ({name})",
                Actual    = $"0x{actual:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 3 (0x003) bits[3:0] | key='{f.DimmType}'"
            };
        }

        private static CheckResult CheckDieDensity(string fileName, PartFields f, byte[] data)
        {
            byte actual = (byte)(data[DIE_DENSITY_OFFSET] & 0x1F);

            if (!DIE_DENSITY_MAP.TryGetValue(f.DieDensityCode, out byte expected))
                return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "Die Density",
                    Expected  = $"UNKNOWN (code='{f.DieDensityCode}')",
                    Actual    = $"0x{actual:X2}",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "Byte 4 (0x004) bits[4:0] | 미정의 Die Density 코드"
                };

            bool pass = actual == expected;
            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "Die Density",
                Expected  = $"0x{expected:X2} (code='{f.DieDensityCode}')",
                Actual    = $"0x{actual:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 4 (0x004) bits[4:0] | key='{f.DieDensityCode}'"
            };
        }

        private static CheckResult CheckIoWidth(string fileName, PartFields f, byte[] data)
        {
            byte actual = (byte)(data[IO_WIDTH_OFFSET] & 0xE0);

            if (!IO_WIDTH_MAP.TryGetValue(f.CompositionCode, out byte expected))
                return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "I/O Width",
                    Expected  = $"UNKNOWN (code='{f.CompositionCode}')",
                    Actual    = $"0x{actual:X2}",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "Byte 6 (0x006) bits[7:5] | 미정의 Composition 코드"
                };

            bool   pass      = actual == expected;
            string widthName = f.CompositionCode == '4' ? "x4" : (f.CompositionCode == '8' ? "x8" : "x16");
            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "I/O Width",
                Expected  = $"0x{expected:X2} ({widthName})",
                Actual    = $"0x{actual:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 6 (0x006) bits[7:5] | key='{f.CompositionCode}'"
            };
        }

        private static CheckResult CheckBankGroups(string fileName, PartFields f, byte[] data)
        {
            byte actual = data[BANK_OFFSET];

            if (!BANK_MAP.TryGetValue(f.BankCode, out byte expected))
                return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "Bank Groups",
                    Expected  = $"UNKNOWN (code='{f.BankCode}')",
                    Actual    = $"0x{actual:X2}",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "Byte 7 (0x007) | 미정의 Bank 코드"
                };

            bool   pass     = actual == expected;
            string bankName = f.BankCode == '4' ? "16 Bank" : "32 Bank";
            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "Bank Groups",
                Expected  = $"0x{expected:X2} ({bankName})",
                Actual    = $"0x{actual:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 7 (0x007) | key='{f.BankCode}'"
            };
        }

        private static CheckResult CheckVdd(string fileName, byte[] data)
        {
            const byte EXPECTED = 0x00;
            byte actual = data[VDD_OFFSET];
            bool pass   = actual == EXPECTED;
            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "VDD Nominal",
                Expected  = $"0x{EXPECTED:X2} (1.1V / DDR5 standard)",
                Actual    = $"0x{actual:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = "Byte 16 (0x010)"
            };
        }

        private static IEnumerable<CheckResult> CheckSpeed(string fileName, PartFields f, byte[] data)
        {
            if (f.SpeedCode == null)
            {
                yield return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "tCKAVGmin",
                    Expected  = "-",
                    Actual    = "Speed 코드 미검출",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "파일명에서 Speed 코드(QK/WM/CM/CP/CQ/CR/CS)를 찾지 못함"
                };
                yield break;
            }

            SpeedSpec spec     = SPEED_MAP[f.SpeedCode];
            // 6000 이상 XMP 파트의 JEDEC SPD 영역은 WM(5600) 타이밍으로 기록됨
            SpeedSpec jedecSpec = XMP_SPEED_CODES.Contains(f.SpeedCode) ? SPEED_MAP["WM"] : spec;
            bool      isXmp     = jedecSpec.Name != spec.Name;

            // tCKAVGmin (Bytes 20~21, Little-Endian)
            int  actualTck = data[TCK_AVG_MIN_OFFSET] | (data[TCK_AVG_MIN_OFFSET + 1] << 8);
            bool passTck   = actualTck == jedecSpec.TckAvgMin;
            string tckExpected = isXmp
                ? $"0x{jedecSpec.TckAvgMin:X4} ({jedecSpec.Name}/JEDEC, tCK={jedecSpec.TckPs}ps)"
                : $"0x{jedecSpec.TckAvgMin:X4} ({jedecSpec.Name}, tCK={jedecSpec.TckPs}ps)";
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "tCKAVGmin",
                Expected  = tckExpected,
                Actual    = $"0x{actualTck:X4}",
                Pass      = passTck,
                Status    = passTck ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 20~21 (0x014~0x015) | speed='{f.SpeedCode}'"
            };

            // SPD 실측 tCK 사용 (0이면 나누기 방지)
            int tckPs = actualTck > 0 ? actualTck : jedecSpec.TckPs;

            // JEDEC 공식: nCK = TRUNCATE((timing_ps × 997 / tCK_ps + 1000) / 1000)
            int actualTaa    = data[TAA_MIN_OFFSET]   | (data[TAA_MIN_OFFSET   + 1] << 8);
            int actualTrcd   = data[TRCD_MIN_OFFSET]  | (data[TRCD_MIN_OFFSET  + 1] << 8);
            int actualTrp    = data[TRP_MIN_OFFSET]   | (data[TRP_MIN_OFFSET   + 1] << 8);

            int nckTaa  = (int)Math.Truncate((actualTaa  * 997.0 / tckPs + 1000.0) / 1000.0);
            if (nckTaa % 2 != 0) nckTaa += 1;   // CL은 짝수 보정
            int nckTrcd = (int)Math.Truncate((actualTrcd * 997.0 / tckPs + 1000.0) / 1000.0);
            int nckTrp  = (int)Math.Truncate((actualTrp  * 997.0 / tckPs + 1000.0) / 1000.0);

            bool passTaa  = nckTaa  == jedecSpec.CL;
            bool passTrcd = nckTrcd == jedecSpec.TrcdNck;
            bool passTrp  = nckTrp  == jedecSpec.TrpNck;

            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "tAA min",
                Expected  = $"CL{jedecSpec.CL}",
                Actual    = $"{actualTaa} ps → CL{nckTaa}",
                Pass      = passTaa,
                Status    = passTaa ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 30~31 | TRUNC(({actualTaa}×997/{tckPs}+1000)/1000)"
            };

            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "tRCD min",
                Expected  = $"{jedecSpec.TrcdNck} nCK",
                Actual    = $"{actualTrcd} ps → {nckTrcd} nCK",
                Pass      = passTrcd,
                Status    = passTrcd ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 32~33 | TRUNC(({actualTrcd}×997/{tckPs}+1000)/1000)"
            };

            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "tRP min",
                Expected  = $"{jedecSpec.TrpNck} nCK",
                Actual    = $"{actualTrp} ps → {nckTrp} nCK",
                Pass      = passTrp,
                Status    = passTrp ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 34~35 | TRUNC(({actualTrp}×997/{tckPs}+1000)/1000)"
            };
        }

        private static CheckResult CheckRank(string fileName, PartFields f, byte[] data)
        {
            byte actual = (byte)(data[RANK_OFFSET] & 0x38);   // bits[5:3]

            if (!RANK_MAP.TryGetValue(f.RankCode, out byte expected))
                return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "Module Rank",
                    Expected  = $"UNKNOWN (RankCode='{f.RankCode}')",
                    Actual    = $"0x{actual:X2}",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "Byte 234 (0x0EA) bits[5:3] | 미정의 Rank 코드"
                };

            bool   pass     = actual == expected;
            string rankName = f.RankCode == '1' ? "1 Rank" : "2 Rank";
            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "Module Rank",
                Expected  = $"0x{expected:X2} ({rankName})",
                Actual    = $"0x{actual:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 234 (0x0EA) bits[5:3] | key='{f.RankCode}'"
            };
        }

        private static CheckResult CheckModuleDensity(string fileName, PartFields f, byte[] data)
        {
            if (!DENSITY_CODE_GB_MAP.TryGetValue(f.DensityCode, out int expectedGb))
                return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "Module Density",
                    Expected  = $"UNKNOWN (code='{f.DensityCode}')",
                    Actual    = "-",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "파일명 Density 코드 미정의"
                };

            byte byte4       = data[DIE_DENSITY_OFFSET];
            byte densityCode = (byte)(byte4 & 0x1F);
            byte diesPkgCode = (byte)((byte4 >> 5) & 0x07);
            byte byte6       = data[IO_WIDTH_OFFSET];
            byte ioCode      = (byte)((byte6 >> 5) & 0x07);
            byte byte234     = data[RANK_OFFSET];
            byte rankBits    = (byte)((byte234 >> 3) & 0x07);

            if (!DIE_DENSITY_GB_MAP.TryGetValue(densityCode, out int dieDensityGb) ||
                !DIES_PER_PKG_MAP.TryGetValue(diesPkgCode, out int diesPerPkg) ||
                !IO_WIDTH_BITS_MAP.TryGetValue(ioCode, out int ioWidthBits))
                return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "Module Density",
                    Expected  = $"{expectedGb} GB (code='{f.DensityCode}')",
                    Actual    = $"B4=0x{byte4:X2} B6=0x{byte6:X2} B234=0x{byte234:X2}",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "SPD Byte 4/6/234 디코딩 실패"
                };

            int rankCount = rankBits + 1;
            int actualGb  = (dieDensityGb * diesPerPkg * rankCount * 64) / (ioWidthBits * 8);
            bool pass     = actualGb == expectedGb;

            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "Module Density",
                Expected  = $"{expectedGb} GB (code='{f.DensityCode}')",
                Actual    = $"{actualGb} GB ({dieDensityGb}Gb×{diesPerPkg}×{rankCount}R×64/{ioWidthBits}b×8)",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 4 (density+dies) / Byte 6 (IO width) / Byte 234 (rank)"
            };
        }

        // ── Phase 4: CRC ─────────────────────────────────────────────────────
        // JESD400-5C §7 Address Map: Byte 0~509 → CRC at Byte 510(LSB) 511(MSB)
        private const int CRC_OFFSET = 510;   // 0x1FE

        // ── XMP 3.0: Byte 상수 (JEDEC DDR5 End User Bytes 640~1023) ──────────
        private const int XMP_GLOBAL_BASE    = 640;   // 0x280
        private const int XMP_P1_BASE        = 704;   // 0x2C0
        private const int XMP_P2_BASE        = 768;   // 0x300
        private const int XMP_MIN_SIZE       = 832;   // Profile 2 CRC(Byte 831)까지 필요
        private const int XMP_P1_NAME_OFFSET = 654;   // Global 내 Profile 1 Name (16 bytes)
        private const int XMP_P2_NAME_OFFSET = 670;   // Global 내 Profile 2 Name (16 bytes)

        // Profile 2 기준 Speed 코드 (CM은 키 없음 → Skip)
        private static readonly Dictionary<string, string> XMP_P2_SPEED =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "CQ", "CM" },
                { "CR", "CQ" },
                { "CS", "CR" },
            };

        // Bank/VDD 코드 → XMP VDD/VDDQ 기대 Hex
        private static readonly Dictionary<char, (byte Vdd, byte Vddq)> BANK_VDD_XMP_MAP =
            new Dictionary<char, (byte, byte)>
            {
                { '5', (0x22, 0x22) },   // 1.1V / 1.1V
                { '6', (0x27, 0x27) },   // 1.35V / 1.35V
                { '7', (0x28, 0x28) },   // 1.4V / 1.4V
            };

        // Speed 코드 → Bank 코드 (Profile 2 VDD 산출용)
        private static readonly Dictionary<string, char> SPEED_TO_BANK_CODE =
            new Dictionary<string, char>(StringComparer.Ordinal)
            {
                { "WM", '5' }, { "CM", '6' }, { "CQ", '6' }, { "CR", '7' }, { "CS", '7' },
            };

        internal static ushort ComputeCrc16(byte[] data, int offset, int length)
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

        private static IEnumerable<CheckResult> CheckCrc(string fileName, byte[] data)
        {
            if (data.Length < 512)
            {
                yield return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "CRC",
                    Expected  = "-",
                    Actual    = $"{data.Length} bytes",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "CRC 검사 불가 — 파일 크기 512 bytes 미만"
                };
                yield break;
            }

            ushort calc   = ComputeCrc16(data, 0, 510);
            ushort stored = (ushort)(data[CRC_OFFSET] | (data[CRC_OFFSET + 1] << 8));
            bool   pass   = calc == stored;

            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "CRC",
                Expected  = $"0x{calc:X4}",
                Actual    = $"0x{stored:X4}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = "Byte 510~511 (0x1FE~0x1FF) | CRC-16 poly=0x1021 over Byte 0~509"
            };
        }

        // ── XMP 파트 DIMM Type 검증 ───────────────────────────────────────────
        // XMP High Speed(6000 이상) 파트는 파일명 DIMM Type이 반드시 'G' 이어야 함
        private static CheckResult CheckXmpDimmType(string fileName, PartFields fields)
        {
            bool pass = fields.DimmType == 'G';
            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "DIMM Type (XMP)",
                Expected  = "G (Gaming UDIMM)",
                Actual    = fields.DimmType.ToString(),
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = "XMP High Speed 파트는 파일명 DIMM Type이 'G' 이어야 함"
            };
        }

        // ── XMP 3.0: nCK 변환 (XMP 스펙 공식, 결과는 JEDEC와 동일) ─────────
        private static int CalcNckXmp(int timingPs, int tckPs) =>
            (int)Math.Truncate((timingPs * 1000.0 / tckPs + 998.0) / 1000.0);

        // ── XMP 3.0: 진입점 ───────────────────────────────────────────────────
        private static IEnumerable<CheckResult> CheckXmp(
            string fileName, PartFields fields, byte[] data)
        {
            if (data.Length < XMP_MIN_SIZE)
            {
                yield return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "[XMP] File Size",
                    Expected  = $">= {XMP_MIN_SIZE} bytes",
                    Actual    = $"{data.Length} bytes",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "XMP 검사 불가 — 파일 크기 부족"
                };
                yield break;
            }

            // [1] XMP ID (Byte 640/641/642)
            bool idOk = data[640] == 0x0C && data[641] == 0x4A && data[642] == 0x30;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "[XMP] ID",
                Expected  = "0x0C / 0x4A / 0x30",
                Actual    = $"0x{data[640]:X2} / 0x{data[641]:X2} / 0x{data[642]:X2}",
                Pass      = idOk,
                Status    = idOk ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = "Byte 640~642 (0x280~0x282)"
            };
            if (!idOk) yield break;

            // [2] Profiles Enabled (Byte 643)
            // CM → 0x01 (P1만), CQ/CR/CS → 0x03 (P1+P2)
            byte expEnabled = fields.SpeedCode == "CM" ? (byte)0x01 : (byte)0x03;
            byte actEnabled = data[643];
            bool passEnabled = actEnabled == expEnabled;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "[XMP] Profiles Enabled",
                Expected  = $"0x{expEnabled:X2}",
                Actual    = $"0x{actEnabled:X2}",
                Pass      = passEnabled,
                Status    = passEnabled ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = "Byte 643 (0x283) | bit0=P1 bit1=P2"
            };

            // [3] Global Section CRC (Byte 640~701 → 702~703)
            ushort calcGlobal   = ComputeCrc16(data, XMP_GLOBAL_BASE, 62);
            ushort storedGlobal = (ushort)(data[702] | (data[703] << 8));
            bool   passGCrc     = calcGlobal == storedGlobal;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "[XMP] Global CRC",
                Expected  = $"0x{calcGlobal:X4}",
                Actual    = $"0x{storedGlobal:X4}",
                Pass      = passGCrc,
                Status    = passGCrc ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = "Byte 702~703 (0x2BE~0x2BF) | CRC-16 over Byte 640~701"
            };

            // [4~12] Profile 1
            foreach (var r in CheckXmpProfile(
                fileName, fields.SpeedCode, fields.BankCode,
                XMP_P1_BASE, XMP_P1_NAME_OFFSET, data, 1))
                yield return r;

            // [13~21] Profile 2 (CM은 Skip)
            if (XMP_P2_SPEED.TryGetValue(fields.SpeedCode, out string p2Code))
            {
                char p2Bank = SPEED_TO_BANK_CODE.TryGetValue(p2Code, out char b) ? b : '6';
                foreach (var r in CheckXmpProfile(
                    fileName, p2Code, p2Bank,
                    XMP_P2_BASE, XMP_P2_NAME_OFFSET, data, 2))
                    yield return r;
            }
        }

        // ── XMP 3.0: Profile 검증 (P1/P2 공통) ───────────────────────────────
        private static IEnumerable<CheckResult> CheckXmpProfile(
            string fileName, string speedCode, char bankCode,
            int baseOffset, int nameOffset, byte[] data, int profileNum)
        {
            SpeedSpec spec   = SPEED_MAP[speedCode];
            string    prefix = $"[XMP] P{profileNum}";

            // VPP (BASE+0): 0x30 = 1.8V 고정
            const byte VPP_EXP = 0x30;
            byte vppAct  = data[baseOffset];
            bool vppPass = vppAct == VPP_EXP;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} VPP",
                Expected  = $"0x{VPP_EXP:X2} (1.8V)",
                Actual    = $"0x{vppAct:X2}",
                Pass      = vppPass,
                Status    = vppPass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset} (BASE+0)"
            };

            // VDD (BASE+1) / VDDQ (BASE+2)
            BANK_VDD_XMP_MAP.TryGetValue(bankCode, out var vddPair);
            byte expVdd  = vddPair.Vdd;
            byte expVddq = vddPair.Vddq;

            byte vddAct  = data[baseOffset + 1];
            bool vddPass = vddAct == expVdd;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} VDD",
                Expected  = $"0x{expVdd:X2} (BankCode='{bankCode}')",
                Actual    = $"0x{vddAct:X2}",
                Pass      = vddPass,
                Status    = vddPass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 1} (BASE+1)"
            };

            byte vddqAct  = data[baseOffset + 2];
            bool vddqPass = vddqAct == expVddq;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} VDDQ",
                Expected  = $"0x{expVddq:X2} (BankCode='{bankCode}')",
                Actual    = $"0x{vddqAct:X2}",
                Pass      = vddqPass,
                Status    = vddqPass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 2} (BASE+2)"
            };

            // tCKAVGmin (BASE+5~6 LE)
            int  actTck  = data[baseOffset + 5] | (data[baseOffset + 6] << 8);
            bool passTck = actTck == spec.TckAvgMin;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} tCKAVGmin",
                Expected  = $"0x{spec.TckAvgMin:X4} ({spec.Name}, tCK={spec.TckPs}ps)",
                Actual    = $"0x{actTck:X4}",
                Pass      = passTck,
                Status    = passTck ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 5}~{baseOffset + 6} (BASE+5~6)"
            };

            int tckPs = actTck > 0 ? actTck : spec.TckPs;

            // tAAmin (BASE+13~14 LE): CL × tCK_ps ±1ps
            int actTaa  = data[baseOffset + 13] | (data[baseOffset + 14] << 8);
            int expTaa  = spec.CL * spec.TckPs;
            bool passTaa = Math.Abs(actTaa - expTaa) <= 1;
            int nckTaa  = CalcNckXmp(actTaa, tckPs);
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} tAAmin",
                Expected  = $"CL{spec.CL} = {expTaa} ps",
                Actual    = $"{actTaa} ps → CL{nckTaa}",
                Pass      = passTaa,
                Status    = passTaa ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 13}~{baseOffset + 14} (BASE+13~14)"
            };

            // tRCDmin (BASE+15~16 LE)
            int actTrcd  = data[baseOffset + 15] | (data[baseOffset + 16] << 8);
            int expTrcd  = spec.TrcdNck * spec.TckPs;
            bool passTrcd = Math.Abs(actTrcd - expTrcd) <= 1;
            int nckTrcd  = CalcNckXmp(actTrcd, tckPs);
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} tRCDmin",
                Expected  = $"{spec.TrcdNck} nCK = {expTrcd} ps",
                Actual    = $"{actTrcd} ps → {nckTrcd} nCK",
                Pass      = passTrcd,
                Status    = passTrcd ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 15}~{baseOffset + 16} (BASE+15~16)"
            };

            // tRPmin (BASE+17~18 LE)
            int actTrp  = data[baseOffset + 17] | (data[baseOffset + 18] << 8);
            int expTrp  = spec.TrpNck * spec.TckPs;
            bool passTrp = Math.Abs(actTrp - expTrp) <= 1;
            int nckTrp  = CalcNckXmp(actTrp, tckPs);
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} tRPmin",
                Expected  = $"{spec.TrpNck} nCK = {expTrp} ps",
                Actual    = $"{actTrp} ps → {nckTrp} nCK",
                Pass      = passTrp,
                Status    = passTrp ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 17}~{baseOffset + 18} (BASE+17~18)"
            };

            // CL Mask (BASE+7~11): 목표 CL 비트 SET 확인
            // 인코딩: byte_offset=(CL-20)/16, bit_pos=((CL-20)%16)/2
            int  clByteIdx = (spec.CL - 20) / 16;
            int  clBitPos  = ((spec.CL - 20) % 16) / 2;
            byte clMask    = (byte)(1 << clBitPos);
            byte clByte    = data[baseOffset + 7 + clByteIdx];
            bool passCl    = (clByte & clMask) != 0;
            int  clAbsByte = baseOffset + 7 + clByteIdx;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} CL Mask",
                Expected  = $"CL{spec.CL} SET (bit{clBitPos}=1, mask=0x{clMask:X2})",
                Actual    = $"0x{clByte:X2} → CL{spec.CL} {(passCl ? "SET ✓" : "CLEAR ✗")}",
                Pass      = passCl,
                Status    = passCl ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {clAbsByte} (BASE+{7 + clByteIdx})"
            };

            // Name String 교차 검증
            foreach (var r in CheckXmpNameString(fileName, prefix, baseOffset, nameOffset, data))
                yield return r;

            // Profile CRC (BASE+62~63 LE, 계산범위 BASE~BASE+61)
            ushort calcCrc   = ComputeCrc16(data, baseOffset, 62);
            ushort storedCrc = (ushort)(data[baseOffset + 62] | (data[baseOffset + 63] << 8));
            bool   passCrc   = calcCrc == storedCrc;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} CRC",
                Expected  = $"0x{calcCrc:X4}",
                Actual    = $"0x{storedCrc:X4}",
                Pass      = passCrc,
                Status    = passCrc ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 62}~{baseOffset + 63} | CRC-16 over Byte {baseOffset}~{baseOffset + 61}"
            };
        }

        // ── XMP 3.0: Name String 교차 검증 ────────────────────────────────────
        // 형식: "RM-[DataRate]-[CL]-[tRCD]-[tRAS]"  예: "RM-6000-34-44-84"
        private static IEnumerable<CheckResult> CheckXmpNameString(
            string fileName, string prefix, int baseOffset, int nameOffset, byte[] data)
        {
            string nameRaw = Encoding.ASCII.GetString(data, nameOffset, 16).TrimEnd(' ', '\0');
            string[] parts = nameRaw.Split('-');

            // ① Brand = "RM"
            string brand     = parts.Length > 0 ? parts[0] : "";
            bool   brandPass = brand == "RM";
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} Name Brand",
                Expected  = "RM",
                Actual    = brand,
                Pass      = brandPass,
                Status    = brandPass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Name String: '{nameRaw}'"
            };

            if (parts.Length < 5 ||
                !int.TryParse(parts[1], out int dataRate) || dataRate <= 0 ||
                !int.TryParse(parts[2], out int clFromName) ||
                !int.TryParse(parts[3], out int trcdFromName) ||
                !int.TryParse(parts[4], out int trasFromName))
            {
                yield return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = $"{prefix} Name Timings",
                    Expected  = "RM-[DataRate]-[CL]-[tRCD]-[tRAS]",
                    Actual    = nameRaw,
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "Name String 형식 오류 또는 숫자 파싱 실패"
                };
                yield break;
            }

            int tckFromName = 2_000_000 / dataRate;   // truncate

            // ② DataRate → tCK vs 실제 tCKAVGmin (BASE+5~6)
            int  actTck     = data[baseOffset + 5] | (data[baseOffset + 6] << 8);
            bool passTckN   = Math.Abs(tckFromName - actTck) <= 1;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} Name tCK",
                Expected  = $"{tckFromName} ps (2000000÷{dataRate})",
                Actual    = $"{actTck} ps",
                Pass      = passTckN,
                Status    = passTckN ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 5}~{baseOffset + 6} (BASE+5~6)"
            };

            // ③ CL × tCK vs 실제 tAAmin (BASE+13~14)
            int  taaFromName = clFromName * tckFromName;
            int  actTaa      = data[baseOffset + 13] | (data[baseOffset + 14] << 8);
            bool passTaaN    = Math.Abs(taaFromName - actTaa) <= 1;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} Name tAA",
                Expected  = $"{clFromName}×{tckFromName}={taaFromName} ps",
                Actual    = $"{actTaa} ps",
                Pass      = passTaaN,
                Status    = passTaaN ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 13}~{baseOffset + 14} (BASE+13~14)"
            };

            // ④ tRCD × tCK vs 실제 tRCDmin (BASE+15~16)
            int  trcdFromName2 = trcdFromName * tckFromName;
            int  actTrcd       = data[baseOffset + 15] | (data[baseOffset + 16] << 8);
            bool passTrcdN     = Math.Abs(trcdFromName2 - actTrcd) <= 1;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} Name tRCD",
                Expected  = $"{trcdFromName}×{tckFromName}={trcdFromName2} ps",
                Actual    = $"{actTrcd} ps",
                Pass      = passTrcdN,
                Status    = passTrcdN ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 15}~{baseOffset + 16} (BASE+15~16)"
            };

            // ⑤ tRAS × tCK vs 실제 tRASmin (BASE+19~20)
            int  trasCalc  = trasFromName * tckFromName;
            int  actTras   = data[baseOffset + 19] | (data[baseOffset + 20] << 8);
            bool passTrasN = Math.Abs(trasCalc - actTras) <= 1;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = $"{prefix} Name tRAS",
                Expected  = $"{trasFromName}×{tckFromName}={trasCalc} ps",
                Actual    = $"{actTras} ps",
                Pass      = passTrasN,
                Status    = passTrasN ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte {baseOffset + 19}~{baseOffset + 20} (BASE+19~20)"
            };
        }

        private static CheckResult CheckDramMfr(string fileName, string partNumberFromName, byte[] data)
        {
            byte actual1 = data[DRAM_MFR_OFFSET];
            byte actual2 = data[DRAM_MFR_OFFSET + 1];

            // 파일명 첫 '-' 이후 첫 글자 추출
            int dashIdx = partNumberFromName.IndexOf('-');
            if (dashIdx < 0 || dashIdx + 1 >= partNumberFromName.Length)
            {
                return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "DRAM Mfr ID",
                    Expected  = "Unknown (cannot parse filename)",
                    Actual    = $"0x{actual1:X2} / 0x{actual2:X2}",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "Byte 552~553 (0x228~0x229) | 파일명 파싱 실패"
                };
            }

            char key = char.ToUpper(partNumberFromName[dashIdx + 1]);

            if (!DRAM_MFR_MAP.TryGetValue(key, out var candidates))
            {
                return new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "DRAM Mfr ID",
                    Expected  = $"UNKNOWN (key='{key}')",
                    Actual    = $"0x{actual1:X2} / 0x{actual2:X2}",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = $"Byte 552~553 | 미정의 DRAM 코드 '{key}'"
                };
            }

            // 허용값 배열에서 일치 항목 탐색
            string matchName = null;
            foreach (var c in candidates)
            {
                if (c.B1 == actual1 && c.B2 == actual2)
                {
                    matchName = c.Name;
                    break;
                }
            }
            bool pass = matchName != null;

            string expectedStr = string.Join(" / ", candidates.Select(
                c => $"0x{c.B1:X2} 0x{c.B2:X2} ({c.Name})"));

            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "DRAM Mfr ID",
                Expected  = expectedStr,
                Actual    = pass
                    ? $"0x{actual1:X2} / 0x{actual2:X2}  ({matchName})"
                    : $"0x{actual1:X2} / 0x{actual2:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 552~553 (0x228~0x229) | key='{key}'"
            };
        }
    }
}
