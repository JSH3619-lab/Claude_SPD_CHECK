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
        public char   CompGen;           // CompGen(#9) = Die Gen 글자 (A/B/P/E/M …) — Stepping 유도용
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
        public const int DRAM_STEP_OFFSET     = 554;   // 0x22A (DRAM Stepping)
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
            if (core.Length >= 9) f.CompGen = char.ToUpper(core[8]);   // CompGen(#9) = Die Gen

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

        // ── DRAM Mfr 코드 → IC Brand 코드 ──────────────────────────────────
        // 파트 P/N 첫 후미 문자 → JEDEC 제조사 → IC 코드
        // S/G = RAmos(07/25)→S1, H = SK Hynix→S2, M = Micron→S3,
        // C = CXMT→S6, N = Nanya→S9
        public static string DramMfrCodeToIc(char dramMfrCode)
        {
            switch (char.ToUpperInvariant(dramMfrCode))
            {
                case 'S':
                case 'G': return "S1";
                case 'H': return "S2";
                case 'M': return "S3";
                case 'C': return "S6";
                case 'N': return "S9";
                default:  return "S?";
            }
        }

        // ── DRAM Stepping (Byte 554) 유도 ──────────────────────────────────────
        // DieGen(CompGen 글자) → stepping byte.
        // 벤더 예외(업체 정의, JEDEC 아님): 삼성 B-die=0x95 / 하이닉스 M-die=0xFF.
        // 그 외 = 글자의 ASCII (JEDEC §20.8 규칙). DieGen 없음/무효 = 0xFF(미제공).
        public static byte BuildDramStepping(PartFields f)
        {
            char gen = char.ToUpperInvariant(f.CompGen);
            if (gen < 'A' || gen > 'Z') return 0xFF;          // 없음/무효 → 미제공
            char mfr = char.ToUpperInvariant(f.DramMfrCode);
            bool samsung = mfr == 'G' || mfr == 'S';
            bool hynix   = mfr == 'H';
            if (samsung && gen == 'B') return 0x95;           // 예외 (벤더 정의)
            if (hynix   && gen == 'M') return 0xFF;           // 예외 (벤더 정의)
            return (byte)gen;                                 // ASCII (A=0x41 …)
        }

        // ── 골든 샘플 템플릿 파일명 조립 ────────────────────────────────────
        // 형식: TPL_{DIMM}{Density}{Bank}{IO}{Die}{Rank}_{Speed}.sp5
        // 예: RMRDAG58A1P-GPWRRWM7-TN → TPL_DAG58A1_WM.sp5
        // Sourcing(RM/TM/CM/BM), DRAM Type(R), DRAM Mfr 는 제외
        // — Sourcing/DRAM Type은 PN에만 영향, DRAM Mfr는 ApplyFixes()가 552~553 자동 재기입
        public static string BuildTemplateFileName(PartFields f)
        {
            if (!f.Valid)
                return null;

            string speed = f.SpeedCode ?? "??";
            return $"TPL_{f.DimmType}{f.DensityCode}" +
                   $"{f.BankCode}{f.CompositionCode}{f.DieDensityCode}{f.RankCode}_{speed}.sp5";
        }

        // ── PID → SID 변환 (SPD Byte 521~550 기입값) ────────────────────────
        // 파일명은 PID. SPD 내부 Part No 자리에는 SID가 들어가야 함.
        //   HEAD: Sourcing TM→RM / BM→CM, 나머지 유지
        //   TAIL(첫 '-' ~ 둘째 '-' 이전) 위치 파싱:
        //     [0]IC [1]CompType [2]CompTest(제거) [3]SMT [4]Test [5][6]Speed
        //     [7]PCB→색상(DDR5='B'/DDR4='G') [8]Vendor(제거)
        //     [9]Purchaser({V,H,A}만 유지) / 이후(특수코드·0Y) 전부 제거
        //   형식 미달 시 null 반환 (호출부에서 PID로 fallback)
        // 예: TMRDAG58A1A-NYWRRQK7GH0Y → RMRDAG58A1A-NYRRQKBH
        public static string BuildSid(string pid)
        {
            if (string.IsNullOrEmpty(pid)) return null;

            int d1 = pid.IndexOf('-');
            if (d1 < 3) return null;                 // Sourcing(2) + DRAM Type(1) 최소
            string head = pid.Substring(0, d1);
            string rest = pid.Substring(d1 + 1);

            int d2 = rest.IndexOf('-');
            string tail = d2 >= 0 ? rest.Substring(0, d2) : rest;
            if (tail.Length < 8) return null;        // IC~PCB 최소 8자

            // HEAD: Sourcing 치환
            string src = head.Substring(0, 2).ToUpperInvariant();
            if (src == "TM")      src = "RM";
            else if (src == "BM") src = "CM";
            string headOut = src + head.Substring(2);

            // 색상: DRAM Type (HEAD[2]) — DDR5(R)='B'(검정), DDR4(4)='G'(초록)
            char color = char.ToUpperInvariant(head[2]) == '4' ? 'G' : 'B';

            var sb = new StringBuilder(12);
            sb.Append(tail[0]);                      // IC Brand
            sb.Append(tail[1]);                      // Comp Type
            // tail[2] Comp Test → 제거
            sb.Append(tail[3]);                      // Module SMT
            sb.Append(tail[4]);                      // Module Test
            sb.Append(tail[5]);                      // Speed[0]
            sb.Append(tail[6]);                      // Speed[1]
            sb.Append(color);                        // tail[7] PCB → 색상
            // tail[8] Vendor → 제거
            if (tail.Length >= 10)                   // tail[9] Purchaser
            {
                char p = char.ToUpperInvariant(tail[9]);
                if (p == 'V' || p == 'H' || p == 'A') sb.Append(p);
            }

            return headOut + "-" + sb.ToString();
        }

        // ── Part 체계 자리별 허용값 (RAMOS DRAM PRODUCT PART 표 기준) ────────
        private const string ALLOW_DRAMTYPE = "4R";
        private const string ALLOW_DIMM     = "SDGC";
        private const string ALLOW_BANK     = "4567";
        private const string ALLOW_COMP     = "486";
        private const string ALLOW_DIE      = "48AHB";
        private const string ALLOW_RANK     = "012";
        private const string ALLOW_IC       = "SGHMCN";
        private const string ALLOW_COMPTYPE = "PUNHMCDGTFEQWJAXYZ";
        private const string ALLOW_COMPTEST = "RSAWG1245";
        private const string ALLOW_SMT      = "0RETGYDL1245";
        private const string ALLOW_TEST     = "0RTGYDL1245S";
        private const string ALLOW_PCB      = "0123456789ABGK";
        private const string ALLOW_VENDOR   = "SGBA";
        private const string ALLOW_PURCH    = "0VHA";
        private static readonly HashSet<string> DENSITY_SET =
            new HashSet<string>(StringComparer.Ordinal) { "1G", "2G", "4G", "8G", "AG", "BG", "CG" };
        // CompGen(#9)은 표가 'M,A,B,C,D,E,F,G…'로 열려 있어 A~Z 영문자 전체 허용

        // ── Part 체계 검증 (AutoGen 생성 차단용) ─────────────────────────────
        // null 반환 = 적합. 문자열 반환 = 차단 사유.
        // 검사: 본체/후미 자리별 허용값 + Speed 코드 + Purchaser 규칙(자사=없음/외주=필수)
        public static string ValidatePartSystem(string pid)
        {
            if (string.IsNullOrWhiteSpace(pid))
                return "Part Number가 비어 있음";

            string clean = StripSuffix(pid.Trim());
            var f = ParsePartFields(clean);
            if (!f.Valid)
                return $"Part 본체 구조 오류: {f.Error}";
            if (f.SpeedCode == null)
                return "Speed 코드를 찾을 수 없음 — 체계 불일치";

            // HEAD 자리별 허용값
            if (f.Sourcing != "RM" && f.Sourcing != "TM" && f.Sourcing != "CM" && f.Sourcing != "BM")
                return $"Sourcing '{f.Sourcing}' 무효 (RM/TM/CM/BM)";
            if (ALLOW_DRAMTYPE.IndexOf(f.DramTypeCode) < 0) return $"DRAM Type '{f.DramTypeCode}' 무효 (4/R)";
            if (ALLOW_DIMM.IndexOf(f.DimmType) < 0)         return $"DIMM Type '{f.DimmType}' 무효 (S/D/G/C)";
            if (!DENSITY_SET.Contains(f.DensityCode))       return $"Density '{f.DensityCode}' 무효";
            if (ALLOW_BANK.IndexOf(f.BankCode) < 0)         return $"Bank/VDD '{f.BankCode}' 무효 (4/5/6/7)";
            if (ALLOW_COMP.IndexOf(f.CompositionCode) < 0)  return $"Composition '{f.CompositionCode}' 무효 (4/8/6)";
            if (ALLOW_DIE.IndexOf(f.DieDensityCode) < 0)    return $"Die Density '{f.DieDensityCode}' 무효 (4/8/A/H/B)";
            if (ALLOW_RANK.IndexOf(f.RankCode) < 0)         return $"Rank '{f.RankCode}' 무효 (0/1/2)";

            // CompGen(#9): Sourcing(2)+core(8) 다음 자리 — A~Z 영문자만
            string headBody = clean.Substring(0, clean.IndexOf('-'));
            if (headBody.Length >= 11)
            {
                char cg = char.ToUpperInvariant(headBody[10]);
                if (cg < 'A' || cg > 'Z')
                    return $"Component Gen '{headBody[10]}' 무효 (A~Z)";
            }

            // TAIL 추출 (첫 '-' ~ 둘째 '-' 이전)
            int d1 = clean.IndexOf('-');
            string rest = clean.Substring(d1 + 1);
            int d2 = rest.IndexOf('-');
            string tail = d2 >= 0 ? rest.Substring(0, d2) : rest;
            if (tail.Length < 8)
                return "후미(IC~PCB) 구조 부족 — 체계 불일치";

            // TAIL 자리별 허용값 (t[5..6]=Speed는 위 SpeedCode로 검증)
            if (ALLOW_IC.IndexOf(char.ToUpperInvariant(tail[0])) < 0)       return $"IC Brand '{tail[0]}' 무효 (S/G/H/M/C/N)";
            if (ALLOW_COMPTYPE.IndexOf(char.ToUpperInvariant(tail[1])) < 0) return $"Comp Type '{tail[1]}' 무효";
            if (ALLOW_COMPTEST.IndexOf(char.ToUpperInvariant(tail[2])) < 0) return $"Comp Test '{tail[2]}' 무효";
            if (ALLOW_SMT.IndexOf(char.ToUpperInvariant(tail[3])) < 0)      return $"Module SMT '{tail[3]}' 무효";
            if (ALLOW_TEST.IndexOf(char.ToUpperInvariant(tail[4])) < 0)     return $"Module Test '{tail[4]}' 무효";
            if (ALLOW_PCB.IndexOf(char.ToUpperInvariant(tail[7])) < 0)      return $"PCB '{tail[7]}' 무효";
            if (tail.Length >= 9 && ALLOW_VENDOR.IndexOf(char.ToUpperInvariant(tail[8])) < 0)
                return $"Vendor '{tail[8]}' 무효 (S/G/B/A)";
            if (tail.Length >= 10 && ALLOW_PURCH.IndexOf(char.ToUpperInvariant(tail[9])) < 0)
                return $"Purchaser '{tail[9]}' 무효 (0/V/H/A)";

            // Purchaser 규칙: 자사(RM/CM)=없어야 / 외주(TM/BM)=필수
            bool third    = f.Sourcing == "TM" || f.Sourcing == "BM";
            bool hasPurch = HasPurchaser(clean);   // {V,H,A}
            if (third  && !hasPurch) return "외주(TM/BM) Part는 Purchaser(V/H/A) 자리가 필요합니다";
            if (!third &&  hasPurch) return "자사(RM/CM) Part는 Purchaser가 없어야 합니다";

            if (BuildSid(clean) == null)
                return "SID 변환 불가 — 체계 불일치";
            return null;
        }

        // 후미 t[9] Purchaser 존재 여부 ({V,H,A})
        private static bool HasPurchaser(string pid)
        {
            int d1 = pid.IndexOf('-');
            if (d1 < 0) return false;
            string rest = pid.Substring(d1 + 1);
            int d2 = rest.IndexOf('-');
            string tail = d2 >= 0 ? rest.Substring(0, d2) : rest;
            if (tail.Length < 10) return false;
            char p = char.ToUpperInvariant(tail[9]);
            return p == 'V' || p == 'H' || p == 'A';
        }

        // ── 파일명 Grade Code 디폴트 부착 (-TN) ─────────────────────────────
        // 2번째 '-'(Grade Code 자리)가 이미 있으면 그대로, 없으면 "-TN" 추가
        public static string EnsureGradeSuffix(string partNo)
        {
            string p = partNo.Trim();
            int d1 = p.IndexOf('-');
            if (d1 < 0) return p;                          // 비정상 — 그대로
            bool hasGrade = p.IndexOf('-', d1 + 1) >= 0;   // 2번째 '-' = Grade 존재
            return hasGrade ? p : p + "-TN";
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
