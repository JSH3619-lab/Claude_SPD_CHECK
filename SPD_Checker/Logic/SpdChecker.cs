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

            // ── 전체 크기 확인 (Part Number 영역까지) ────────────────────────
            int minRequired = PART_NUMBER_OFFSET + PART_NUMBER_LENGTH;
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
                results.Add(CheckDieDensity(fileName, fields, data));
                results.Add(CheckIoWidth(fileName, fields, data));
                results.Add(CheckBankGroups(fileName, fields, data));
                results.Add(CheckVdd(fileName, data));
                results.AddRange(CheckSpeed(fileName, fields, data));
                results.Add(CheckRank(fileName, fields, data));
            }

            return results;
        }

        // ── CSV Hex 텍스트 파싱 ──────────────────────────────────────────────
        // 형식: "30,12,02,..." CRLF 구분, 16바이트/줄, 64줄 = 1024 논리 바이트
        private static byte[] ParseSpdText(string filePath)
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
        private static string StripSuffix(string nameNoExt)
        {
            if (nameNoExt.EndsWith("0Y", StringComparison.OrdinalIgnoreCase))
                nameNoExt = nameNoExt.Substring(0, nameNoExt.Length - 2);
            if (nameNoExt.EndsWith("-TN", StringComparison.OrdinalIgnoreCase))
                nameNoExt = nameNoExt.Substring(0, nameNoExt.Length - 3);
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
            bool   pass       = string.Equals(expectedPartNumber, actualTrim, StringComparison.Ordinal);
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
                { '4', 0x42 },   // 16 Bank (4BG × 4B/BG)
                { '5', 0x62 },   // 32 Bank (8BG × 4B/BG)
            };

        private static readonly Dictionary<char, byte> RANK_MAP =
            new Dictionary<char, byte>
            {
                { '1', 0x00 },   // 1 Rank  bits[5:3]=000
                { '2', 0x08 },   // 2 Rank  bits[5:3]=001
            };

        private struct SpeedSpec
        {
            public string Name;
            public int    TckPs;
            public int    TckAvgMin;
            public int    CL;
            public int    TrcdNck;
            public int    TrpNck;
        }

        private static readonly Dictionary<string, SpeedSpec> SPEED_MAP =
            new Dictionary<string, SpeedSpec>(StringComparer.Ordinal)
            {
                { "QK", new SpeedSpec { Name="DDR5-4800", TckPs=416, TckAvgMin=0x01A0, CL=40, TrcdNck=39, TrpNck=39 } },
                { "WM", new SpeedSpec { Name="DDR5-5600", TckPs=357, TckAvgMin=0x0165, CL=46, TrcdNck=45, TrpNck=45 } },
                { "CM", new SpeedSpec { Name="DDR5-6000", TckPs=333, TckAvgMin=0x014D, CL=34, TrcdNck=44, TrpNck=44 } },
                { "CP", new SpeedSpec { Name="DDR5-6400", TckPs=312, TckAvgMin=0x0138, CL=52, TrcdNck=52, TrpNck=52 } },
                { "CQ", new SpeedSpec { Name="DDR5-6400", TckPs=312, TckAvgMin=0x0138, CL=36, TrcdNck=44, TrpNck=44 } },
                { "CR", new SpeedSpec { Name="DDR5-6800", TckPs=294, TckAvgMin=0x0126, CL=44, TrcdNck=44, TrpNck=44 } },
                { "CS", new SpeedSpec { Name="DDR5-7200", TckPs=277, TckAvgMin=0x0115, CL=38, TrcdNck=46, TrpNck=46 } },
            };

        // ── Phase 3: Part Number Field Parser ────────────────────────────────
        private struct PartFields
        {
            public char   DimmType;
            public string DensityCode;
            public char   BankCode;
            public char   CompositionCode;
            public char   DieDensityCode;
            public char   RankCode;
            public string SpeedCode;   // null = 미검출
            public bool   Valid;
            public string Error;
        }

        private static PartFields ParsePartFields(string partNoFromName)
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

            // Speed 코드: '-' 이후 문자열에서 Contains 탐색
            if (dashIdx >= 0 && dashIdx + 1 < partNoFromName.Length)
            {
                string suffix = partNoFromName.Substring(dashIdx + 1);
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
                    CheckItem = "Speed",
                    Expected  = "-",
                    Actual    = "Speed 코드 미검출",
                    Pass      = false,
                    Status    = CheckStatus.Fail,
                    Note      = "파일명에서 Speed 코드(QK/WM/CM/CP/CQ/CR/CS)를 찾지 못함"
                };
                yield break;
            }

            SpeedSpec spec = SPEED_MAP[f.SpeedCode];

            // tCKAVGmin (Bytes 20~21, Little-Endian)
            int  actualTck = data[TCK_AVG_MIN_OFFSET] | (data[TCK_AVG_MIN_OFFSET + 1] << 8);
            bool passTck   = actualTck == spec.TckAvgMin;
            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "tCKAVGmin",
                Expected  = $"0x{spec.TckAvgMin:X4} ({spec.Name}, tCK={spec.TckPs}ps)",
                Actual    = $"0x{actualTck:X4}",
                Pass      = passTck,
                Status    = passTck ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 20~21 (0x014~0x015) | speed='{f.SpeedCode}'"
            };

            // SPD 실측 tCK 사용 (0이면 나누기 방지)
            int tckPs = actualTck > 0 ? actualTck : spec.TckPs;

            // JEDEC 공식: nCK = TRUNCATE((timing_ps × 997 / tCK_ps + 1000) / 1000)
            int actualTaa    = data[TAA_MIN_OFFSET]   | (data[TAA_MIN_OFFSET   + 1] << 8);
            int actualTrcd   = data[TRCD_MIN_OFFSET]  | (data[TRCD_MIN_OFFSET  + 1] << 8);
            int actualTrp    = data[TRP_MIN_OFFSET]   | (data[TRP_MIN_OFFSET   + 1] << 8);

            int nckTaa  = (int)Math.Truncate((actualTaa  * 997.0 / tckPs + 1000.0) / 1000.0);
            if (nckTaa % 2 != 0) nckTaa += 1;   // CL은 짝수 보정
            int nckTrcd = (int)Math.Truncate((actualTrcd * 997.0 / tckPs + 1000.0) / 1000.0);
            int nckTrp  = (int)Math.Truncate((actualTrp  * 997.0 / tckPs + 1000.0) / 1000.0);

            bool passTaa  = nckTaa  == spec.CL;
            bool passTrcd = nckTrcd == spec.TrcdNck;
            bool passTrp  = nckTrp  == spec.TrpNck;

            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "tAA min",
                Expected  = $"CL{spec.CL}",
                Actual    = $"{actualTaa} ps → CL{nckTaa}",
                Pass      = passTaa,
                Status    = passTaa ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 30~31 | TRUNC(({actualTaa}×997/{tckPs}+1000)/1000)"
            };

            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "tRCD min",
                Expected  = $"{spec.TrcdNck} nCK",
                Actual    = $"{actualTrcd} ps → {nckTrcd} nCK",
                Pass      = passTrcd,
                Status    = passTrcd ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 32~33 | TRUNC(({actualTrcd}×997/{tckPs}+1000)/1000)"
            };

            yield return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "tRP min",
                Expected  = $"{spec.TrpNck} nCK",
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

        // ── Phase 2: Manufacturer ID ─────────────────────────────────────────
        // Module Mfr : Byte 512~513 (고정: RAmos 0x07/0x25)
        // DRAM Mfr   : Byte 552~553 (파일명 첫 '-' 이후 첫 글자로 결정)

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
