using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SPD_Checker.Logic
{
    // Phase 2: 식별 규칙 외부화. rules.json(앱 경로)에서 로드, 없으면 기본값으로 생성.
    // 기본값 = 현재 하드코딩 값과 동일 → config 없으면 동작 무변경.
    // Check/Fix가 상수 대신 이 값을 참조.
    internal static class RulesConfig
    {
        public static byte[] ModuleMfr { get; private set; }   // 512~513
        public static byte[] SpdHub    { get; private set; }   // 194~197
        public static byte[] Pmic      { get; private set; }   // 198~201
        public static Dictionary<char, (byte B1, byte B2, string Name)[]> DramMfr { get; private set; }

        private static volatile bool _loaded;
        private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "rules.json");

        static RulesConfig() { Init(); }

        public static void Init()
        {
            if (_loaded) return;

            bool fileExists = File.Exists(ConfigPath);
            Dto  dto        = null;
            if (fileExists)
            {
                try { dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(ConfigPath)); }
                catch (Exception ex) { Warn("rules.json 파싱 실패 — 기본값으로 동작: " + ex.Message); }
            }

            // 값 적용 — 잘못된 hex/구조여도 크래시 없이 기본값 폴백
            try
            {
                if (dto?.identity != null) Apply(dto);
                else { Apply(DefaultDto()); if (fileExists) Warn("rules.json 내용 무효 — 기본값으로 동작"); }
            }
            catch (Exception ex)
            {
                Warn("rules.json 값 오류(hex 등) — 기본값으로 동작: " + ex.Message);
                Apply(DefaultDto());
            }

            // 파일이 아예 없을 때만 기본값으로 생성 (손상된 편집본은 보존 → 사용자가 수정)
            if (!fileExists)
            {
                try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(DefaultDto(), new JsonSerializerOptions { WriteIndented = true })); }
                catch (Exception ex) { Warn("rules.json 생성 실패: " + ex.Message); }
            }

            _loaded = true;
        }

        private static void Warn(string msg) { try { AppLogger.Warn("RulesConfig", msg); } catch { } }

        private static void Apply(Dto dto)
        {
            ModuleMfr = ParseBytes(dto.identity.moduleMfr);
            SpdHub    = ParseBytes(dto.identity.spdHub);
            Pmic      = ParseBytes(dto.identity.pmic);
            DramMfr   = new Dictionary<char, (byte, byte, string)[]>();
            foreach (var kv in dto.identity.dramMfr)
            {
                char key = char.ToUpperInvariant(kv.Key[0]);
                DramMfr[key] = kv.Value.Select(m =>
                {
                    var b = ParseBytes(m.id);
                    return (b[0], b[1], m.name);
                }).ToArray();
            }
        }

        private static byte[] ParseBytes(string hex) =>
            hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(t => Convert.ToByte(t, 16)).ToArray();

        // 기본값 = 현재 하드코딩 값 (시드 소스, 단일 출처)
        private static Dto DefaultDto() => new Dto
        {
            version  = 1,
            identity = new IdentityDto
            {
                moduleMfr = "07 25",
                spdHub    = "0B 10 80 00",
                pmic      = "0B 10 82 44",
                dramMfr   = new Dictionary<string, MfrDto[]>
                {
                    { "G", new[] { new MfrDto { id = "80 CE", name = "Samsung"  } } },
                    { "S", new[] { new MfrDto { id = "80 CE", name = "Samsung"  } } },
                    { "H", new[] { new MfrDto { id = "80 AD", name = "SK Hynix" } } },
                    { "N", new[] { new MfrDto { id = "83 0B", name = "Nanya"    } } },
                    { "C", new[] { new MfrDto { id = "8A 91", name = "CXMT"     } } },
                    { "M", new[] { new MfrDto { id = "80 2C", name = "Micron" }, new MfrDto { id = "02 B5", name = "Spectek" } } },
                }
            }
        };

        // ── DTO (System.Text.Json) ────────────────────────────────────────────
        private class Dto { public int version { get; set; } public IdentityDto identity { get; set; } }
        private class IdentityDto
        {
            public string moduleMfr { get; set; }
            public string spdHub { get; set; }
            public string pmic { get; set; }
            public Dictionary<string, MfrDto[]> dramMfr { get; set; }
        }
        private class MfrDto { public string id { get; set; } public string name { get; set; } }
    }
}
