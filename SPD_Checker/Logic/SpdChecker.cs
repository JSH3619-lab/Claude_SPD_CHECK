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

            // Phase 2+ 추가 예정
            // results.Add(CheckManufacturerId(...));
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
    }
}
