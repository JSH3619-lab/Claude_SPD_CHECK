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

        // ── Public Entry Point ───────────────────────────────────────────────
        public static List<CheckResult> CheckFile(string filePath)
        {
            var    results  = new List<CheckResult>();
            string fileName = Path.GetFileName(filePath);
            string nameNoExt = Path.GetFileNameWithoutExtension(filePath);

            // ── A100 예외 파일 감지 (RM/TM/CM/BM 으로 시작하지 않는 파일) ──
            bool isStandard = STANDARD_PREFIXES.Any(p =>
                nameNoExt.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            if (!isStandard)
            {
                results.Add(new CheckResult
                {
                    FileName  = fileName,
                    CheckItem = "File Type",
                    Expected  = "RM / TM / CM / BM prefix",
                    Actual    = nameNoExt,
                    Pass      = false,
                    Status    = CheckStatus.Skip,
                    Note      = "A100 예외 파일 — 검사 생략"
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

            // ── 최소 크기 확인 ───────────────────────────────────────────────
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

            // Phase 3+ 추가 예정
            // results.Add(CheckCrc(...));

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
        // "-TN" 등 SPD에 포함되지 않는 접미사 제거
        private static string StripSuffix(string nameNoExt)
        {
            if (nameNoExt.EndsWith("-TN", StringComparison.OrdinalIgnoreCase))
                return nameNoExt.Substring(0, nameNoExt.Length - 3);
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
        private static readonly Dictionary<char, (byte B1, byte B2, string Name)> DRAM_MFR_MAP =
            new Dictionary<char, (byte, byte, string)>
            {
                { 'G', (0x07, 0x25, "RAmos")  },
                { 'S', (0x07, 0x25, "RAmos")  },
                { 'N', (0x83, 0x0B, "Nanya")  },
                { 'C', (0x8A, 0x91, "CXMT")   },
                { 'M', (0x02, 0xB5, "Micron") },
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

            if (!DRAM_MFR_MAP.TryGetValue(key, out var expected))
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

            bool pass = actual1 == expected.B1 && actual2 == expected.B2;

            return new CheckResult
            {
                FileName  = fileName,
                CheckItem = "DRAM Mfr ID",
                Expected  = $"0x{expected.B1:X2} / 0x{expected.B2:X2}  ({expected.Name})",
                Actual    = $"0x{actual1:X2} / 0x{actual2:X2}",
                Pass      = pass,
                Status    = pass ? CheckStatus.Pass : CheckStatus.Fail,
                Note      = $"Byte 552~553 (0x228~0x229) | key='{key}'"
            };
        }
    }
}
