using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SPD_Checker.Models;

namespace SPD_Checker.Logic
{
    internal static class VerificationLogger
    {
        private const string LOG_FILE     = "verification_log.csv";
        private const string LOG_HEADER   = "FileName,SHA256,CheckDate,OverallResult,PassCount,FailCount,SkipCount";

        internal struct SaveSummary
        {
            public int Saved;           // PASS → 이력 기록됨 (복사 없음)
            public int AlreadyVerified; // 동일 해시 → 스킵
            public int Modified;        // 동일 파일명 + 다른 해시 (수정된 파일)
            public int Fail;            // FAIL 결과
            public int Incomplete;      // SKIP/INCOMPLETE 결과
        }

        internal static SaveSummary Save(
            IEnumerable<string>                       filePaths,
            Dictionary<string, List<CheckResult>>     fileResults)
        {
            var summary = new SaveSummary();

            var byDir = filePaths
                .Where(p => fileResults.ContainsKey(Path.GetFileName(p)))
                .GroupBy(p => Path.GetDirectoryName(p) ?? ".");

            foreach (var group in byDir)
            {
                string sourceDir = group.Key;
                string logPath   = Path.Combine(sourceDir, LOG_FILE);

                var log = ReadLog(logPath);

                foreach (string filePath in group)
                {
                    string fileName = Path.GetFileName(filePath);
                    if (!fileResults.TryGetValue(fileName, out var results)) continue;

                    byte[] data;
                    try   { data = File.ReadAllBytes(filePath); }
                    catch { continue; }

                    string hash          = ComputeSha256(data);
                    string overallResult = GetOverallResult(results);
                    int    passCount     = results.Count(r => r.Status == CheckStatus.Pass);
                    int    failCount     = results.Count(r => r.Status == CheckStatus.Fail);
                    int    skipCount     = results.Count(r => r.Status == CheckStatus.Skip);

                    // 동일 해시가 이미 로그에 있으면 스킵
                    if (log.Any(e => e.Sha256 == hash))
                    {
                        summary.AlreadyVerified++;
                        continue;
                    }

                    // 동일 파일명 + 다른 해시 → 수정된 파일 (신규 행 추가)
                    if (log.Any(e => e.FileName == fileName))
                        summary.Modified++;

                    log.Add(new LogEntry
                    {
                        FileName      = fileName,
                        Sha256        = hash,
                        CheckDate     = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        OverallResult = overallResult,
                        PassCount     = passCount,
                        FailCount     = failCount,
                        SkipCount     = skipCount,
                    });

                    // 파일 복사 없이 CSV 이력만 기록 (PASS/FAIL/INCOMPLETE 전부 로그)
                    if (overallResult == "PASS")      summary.Saved++;
                    else if (overallResult == "FAIL") summary.Fail++;
                    else                              summary.Incomplete++;
                }

                WriteLog(logPath, log);
            }

            return summary;
        }

        // PASS: FAIL·SKIP 모두 없음 / FAIL: FAIL 1개 이상 / INCOMPLETE: SKIP 1개 이상
        private static string GetOverallResult(List<CheckResult> results)
        {
            if (results.Any(r => r.Status == CheckStatus.Skip))  return "INCOMPLETE";
            if (results.Any(r => r.Status == CheckStatus.Fail))  return "FAIL";
            return "PASS";
        }

        private static string ComputeSha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static List<LogEntry> ReadLog(string logPath)
        {
            var list = new List<LogEntry>();
            if (!File.Exists(logPath)) return list;
            try
            {
                var lines = File.ReadAllLines(logPath, Encoding.UTF8);
                foreach (var line in lines.Skip(1))
                {
                    var p = line.Split(',');
                    if (p.Length < 7) continue;
                    list.Add(new LogEntry
                    {
                        FileName      = p[0].Trim('"'),
                        Sha256        = p[1].Trim('"'),
                        CheckDate     = p[2].Trim('"'),
                        OverallResult = p[3].Trim('"'),
                        PassCount     = int.TryParse(p[4], out int pc) ? pc : 0,
                        FailCount     = int.TryParse(p[5], out int fc) ? fc : 0,
                        SkipCount     = int.TryParse(p[6], out int sc) ? sc : 0,
                    });
                }
            }
            catch { }
            return list;
        }

        private static void WriteLog(string logPath, List<LogEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine(LOG_HEADER);
            foreach (var e in entries)
                sb.AppendLine(string.Format("\"{0}\",\"{1}\",\"{2}\",{3},{4},{5},{6}",
                    e.FileName, e.Sha256, e.CheckDate, e.OverallResult,
                    e.PassCount, e.FailCount, e.SkipCount));
            File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
        }

        private class LogEntry
        {
            public string FileName;
            public string Sha256;
            public string CheckDate;
            public string OverallResult;
            public int    PassCount;
            public int    FailCount;
            public int    SkipCount;
        }
    }
}
