using System;
using System.IO;
using System.Text;

namespace SPD_Checker.Logic
{
    public class AutoGenResult
    {
        public bool   Success;
        public bool   AlreadyExists;     // 이미 같은 이름의 .sp5 존재 (생성 스킵, 정상)
        public string OutputPath;        // 생성된 또는 기존 .sp5 경로
        public string TemplateUsed;      // 사용한 템플릿 파일명
        public string ErrorMessage;      // 실패 사유
        public string PartNo;            // 입력 Part Number (suffix 포함 원본)
        public string Creator;           // 작업자 이름
    }

    public static class SpdAutoGen
    {
        public const string TEMPLATES_DIR = "Templates";
        public const string LOG_DIR       = "Auto_Gen_Log";
        public const string LOG_FILE      = "AutoGen_Log.csv";

        // ── 메인 진입점 ──────────────────────────────────────────────────────
        public static AutoGenResult GenerateFromTemplate(string partNoInput, string folderPath, string creator = "")
        {
            var r = new AutoGenResult { PartNo = partNoInput, Creator = creator?.Trim() ?? "" };

            // 1) 입력 검증
            if (string.IsNullOrWhiteSpace(partNoInput))
            {
                r.ErrorMessage = "Part Number가 비어 있음";
                return Log(r, folderPath);
            }

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                r.ErrorMessage = $"폴더를 찾을 수 없음: {folderPath}";
                return Log(r, folderPath);
            }

            string partNo  = partNoInput.Trim();
            string outPath = Path.Combine(folderPath, partNo + ".sp5");

            // 2) 이미 존재하면 정상 처리 (생성 스킵)
            if (File.Exists(outPath))
            {
                r.Success       = true;
                r.AlreadyExists = true;
                r.OutputPath    = outPath;
                return Log(r, folderPath);
            }

            // 3) 파트 넘버 파싱
            string nameNoSuffix = SpdParser.StripSuffix(partNo);
            var fields = SpdParser.ParsePartFields(nameNoSuffix);
            if (!fields.Valid)
            {
                r.ErrorMessage = $"Part Number 파싱 실패: {fields.Error}";
                return Log(r, folderPath);
            }

            string expectedTpl = SpdParser.BuildTemplateFileName(fields);
            if (expectedTpl == null)
            {
                r.ErrorMessage = "템플릿 파일명 계산 실패";
                return Log(r, folderPath);
            }
            r.TemplateUsed = expectedTpl;

            // 4) Templates 폴더 확인
            string tplDir = Path.Combine(folderPath, TEMPLATES_DIR);
            if (!Directory.Exists(tplDir))
            {
                r.ErrorMessage = $"Templates 폴더 없음: {tplDir}";
                return Log(r, folderPath);
            }

            string tplPath = Path.Combine(tplDir, expectedTpl);
            if (!File.Exists(tplPath))
            {
                r.ErrorMessage = $"매칭 템플릿 없음: {expectedTpl}";
                return Log(r, folderPath);
            }

            // 5) 템플릿 → byte[] → ApplyFixes (Part Number 치환 + CRC 재계산)
            try
            {
                byte[] tplData = SpdParser.ParseFile(tplPath);
                if (tplData.Length < SpdParser.SPD_FULL_SIZE)
                {
                    byte[] padded = new byte[SpdParser.SPD_FULL_SIZE];
                    Array.Copy(tplData, padded, tplData.Length);
                    tplData = padded;
                }

                // SpdFixer는 filePath의 파일명에서 Part Number를 파싱
                string virtualPath = Path.Combine(folderPath, partNo + ".sp5");
                byte[] fixedData   = SpdFixer.ApplyFixes(tplData, virtualPath);

                File.WriteAllText(outPath, SpdFixer.SerializeToSp5(fixedData), Encoding.ASCII);

                r.Success    = true;
                r.OutputPath = outPath;
                return Log(r, folderPath);
            }
            catch (Exception ex)
            {
                r.ErrorMessage = "생성 중 오류: " + ex.Message;
                return Log(r, folderPath);
            }
        }

        // ── AutoGen_Log.csv 한 줄 기록 ───────────────────────────────────────
        private static AutoGenResult Log(AutoGenResult r, string folderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                    return r;

                string logDir  = Path.Combine(folderPath, LOG_DIR);
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, LOG_FILE);
                bool   header  = !File.Exists(logPath);

                string status = r.Success
                    ? (r.AlreadyExists ? "EXISTS" : "GENERATED")
                    : "FAIL";

                var sb = new StringBuilder();
                if (header)
                    sb.AppendLine("DateTime,PartNo,Template,Result,Creator,ErrorMessage");

                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
                  .Append(Csv(r.PartNo)).Append(',')
                  .Append(Csv(r.TemplateUsed)).Append(',')
                  .Append(status).Append(',')
                  .Append(Csv(r.Creator)).Append(',')
                  .Append(Csv(r.ErrorMessage))
                  .AppendLine();

                File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 로그 실패는 결과에 영향 없음
            }
            return r;
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
