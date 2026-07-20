# DDR5 SPD Checker — 프로젝트 규칙 (CLAUDE.md)

**참조 표준:** `JESD400-5C_DDR5.pdf` / `JEDEC ID_2025 (1).pdf`  
**핵심 가정:** `.sp5` 파일명 = Part Number (정확하다고 가정)

---

## 1. 작업 진행 규칙 (必守)

> **명시적 승인 없이 파일 생성·수정·삭제 금지.**

1. 새 기능·변경은 **계획 텍스트 제시 → 승인 → 코드 작성** 순서.
2. 기존 파일 수정 시 변경 범위 먼저 명시.
3. **양쪽 경로 모두 수정 필수:** 워크트리(`.claude/worktrees/...`)에만 수정하면 `C:\JSH_Folder\PGM\SPD_Check_PGM`에 반영 안 됨. 항상 main 경로 직접 수정 또는 머지.
4. **빌드 명령:** `dotnet build C:\JSH_Folder\PGM\SPD_Check_PGM\SPD_Checker\SPD_Checker.csproj -c Debug`

---

## 2. 에이전트 모델 운영 전략

| 상황 | 모델 |
|------|------|
| 파일 탐색, 단순 검색 | `haiku` |
| 일반 코딩, 버그 수정 | `sonnet` (기본) |
| 복잡한 설계, 규격 해석 | `opus` |

---

## 3. 개발 Phase 현황

| Phase | 항목 | 상태 |
|-------|------|------|
| 예외 | 파일 확장자 / Module Mfr 라우팅 / 접미사 처리 | ✅ 완료 |
| 1 | Part Number 검증 (파일명 vs Byte 521~550) | ✅ 완료 |
| 2 | Manufacturer ID 검증 (Module Mfr / DRAM Mfr) | ✅ 완료 |
| 3 | DRAM Type / Module Type / Die Density / I/O Width / Bank Groups / VDD | ✅ 완료 |
| 3 | tCKAVGmin / tAA / tRCD / tRP (Speed 코드 기반 타이밍 검증) | ✅ 완료 |
| 3 | Module Rank / Module Density (계산값 비교) | ✅ 완료 |
| 4 | CRC-16 검증 (Byte 0~509 → Byte 510~511, poly=0x1021) | ✅ 완료 |
| XMP | XMP 3.0 검증 (6000 이상 속도 코드 파트: CM/CQ/CR/CS) | ✅ 완료 |
| Fix | FAIL 항목 자동 수정 (Save as _FIXED / Overwrite 2가지 모드) | ✅ 완료 |
| SID | PID→SID 변환 (`SpdParser.BuildSid`) — 파일명=PID, Byte 521~550=SID. Check 대조·Fix 기입 모두 SID 기준. Check 모드 `🔧 FAIL 일괄수정` 버튼(다량 파일 ApplyFixes 일괄, 덮어쓰기/_FIXED 선택). Editor도 SID 기준 대조 + Part Info 상단 PID/SID 2줄 라벨 + Key Bytes 풀 SID 표시 | ✅ 완료 (→ part_number_parsing.md) |
| AutoGen | 체계 검증 (`SpdParser.ValidatePartSystem`) — 자리별 허용값 화이트리스트 + Purchaser 규칙(자사=금지/외주=필수) 위반 시 생성 차단. 파일명 `-TN` 디폴트 부착(`EnsureGradeSuffix`) | ✅ 완료 (→ part_number_parsing.md) |
| Ver | v2.0 (AssemblyVersion 2.0.0.0 + UI 전 화면) — SID 검증 도입으로 기존 PASS→FAIL 호환성 변경(major bump) | ✅ 완료 |
| Ver | v2.1 (AssemblyVersion 2.1.0.0 + UI 전 화면) — DRAM Mfr G/S→Samsung(80/CE) 매핑 변경 + Check 모드 선택 수정(체크박스) 기능 | ✅ 완료 |
| Editor | SPD Editor / Auto-Gen 3-mode 구조 — E-0~E-7 + E-3.5 모두 완료 (LaunchForm / Hex Grid + 실시간 검증 / Auto-Fix / Save·Load / Auto-Gen / Mode 드롭다운 + FAIL 점프 / Part Info Form 입력 + 조합 검증) | ✅ 완료 (→ phase_editor_autogen.md) |
| UI/UX | LaunchForm 버튼 줄 넘침 제거 (창 720px, Panel 기반 버튼) / AutoGen 폴더 경로 영속 저장 (autogen_settings.cfg) / Editor 초기화 버튼 (↩ 마지막 로드·저장 상태로 복원) | ✅ 완료 |
| UI/UX | Editor Key Bytes 상태 아이콘 색상 표시 (✓ 초록 / ✗ 빨강, RichTextBox 전환) | ✅ 완료 |
| Verify | 검증 이력 저장 — Save Verified 버튼 / SHA256 해시 기반 중복 감지 / **파일 복사 없이** verification_log.csv 이력만 (소스 폴더에 직접, 날짜 포함) | ✅ 완료 |
| Logging | 시스템 로그 — `%LOCALAPPDATA%\SPD_Studio\logs\app_YYYYMMDD.log` / INFO·WARN·ERROR·FATAL 4단계 / 7일 자동 삭제 / 전역 예외 핸들러 | ✅ 완료 |
| Stepping | DRAM Stepping(Byte 554) 자동 유도 — `SpdParser.BuildDramStepping` (CompGen=Die Gen 글자 → ASCII, 삼성B=95·하이닉스M=FF 벤더 예외). Check 검증 + Fix/Auto-Gen 기입 + Editor 표시. CRC 범위 밖이라 재계산 불필요 (→ jesd400_bytes.md) | ✅ 완료 |
| Hub/PMIC | SPD Hub(194~197)·PMIC(198~201) 고정값 검증 — ANPEC API2201-B24=`0B10 8000` / APW8502=`0B10 8244`. 과거 삼성 stale값(86 32/80 B3) → Check FAIL, Fix 자동 교정(CRC 범위 안이라 재계산). 전 파트 공통 고정 상수, 완전 일치 판정. Editor 표시 (→ jesd400_bytes.md) | ✅ 완료 |

### XMP 3.0 검증 항목 (Phase XMP 세부)

| 항목 | 내용 |
|------|------|
| ID | Byte 640~642 고정값 확인 |
| Profiles Enabled | CM=0x01(P1만), CQ/CR/CS=0x03(P1+P2) |
| Global CRC | Byte 640~701 → Byte 702~703 |
| P1/P2 VPP / VDD / VDDQ | Bank 코드 기반 전압값 확인 |
| P1/P2 tCKAVGmin / tAAmin / tRCDmin / tRPmin | 속도 코드 기반 타이밍 확인 |
| P1/P2 CL Mask | 목표 CL 비트 SET 확인 |
| P1/P2 Name String | "RM-[DataRate]-[CL]-[tRCD]-[tRAS]" 교차 검증 |
| P1/P2 Profile CRC | 각 프로파일 64byte CRC-16 확인 |
| P2 속도 | CQ→CM / CR→CQ / CS→CR (한 단계 낮은 속도) |

---

## 4. 기술 스택

- **언어:** C# / **프레임워크:** .NET / **UI:** WinForms
- **배포:** .exe 단독 실행 (별도 런타임 설치 불필요)

---

## 5. 폴더 구조

```
C:\JSH_Folder\PGM\SPD_Check_PGM\
├── CLAUDE.md
├── JESD400-5C_DDR5.pdf
├── JEDEC ID_2025 (1).pdf
├── .claude\
│   ├── settings.local.json          ← Bash/PowerShell 자동 승인 설정
│   └── docs\
│       ├── part_number_parsing.md   ← 파트 넘버 파싱 규칙 + Speed 코드 표
│       ├── jesd400_bytes.md         ← JESD400-5C Byte 위치 + JEDEC ID 전체 참조
│       ├── phase4_crc.md            ← Phase 4 CRC 설계 (구현 가이드 포함)
│       ├── xmp_bytes.md             ← Intel XMP 3.0 전체 Byte 위치 참조
│       └── phase_editor_autogen.md  ← Editor / Auto-Gen 기획 + 진행 현황
└── SPD_Checker\
    ├── SPD_Checker.csproj
    ├── Program.cs
    ├── MainForm.cs
    ├── Forms\
    │   ├── LaunchForm.cs            ← 모드 선택 화면 (Check / Editor / Auto-Gen)
    │   ├── SpdEditorForm.cs         ← SPD Editor (Hex Grid + 검증 + Auto-Fix + 초기화)
    │   ├── AutoGenForm.cs           ← Auto-Gen (QR → 골든샘플 자동 생성, 폴더 영속 저장)
    │   ├── ModeDropdown.cs          ← 모드 전환 드롭다운
    │   └── HudTooltipForm.cs        ← Key Byte 툴팁 HUD
    ├── Logic\
    │   ├── SpdChecker.cs            ← 핵심 검증 로직
    │   ├── SpdFixer.cs              ← FAIL 항목 자동 수정 로직
    │   ├── SpdParser.cs             ← 공유 타입·파싱·CRC·템플릿명 조립
    │   ├── SpdAutoGen.cs            ← Auto-Gen 생성 로직
    │   ├── VerificationLogger.cs    ← 검증 이력 저장 (SHA256·CSV·Verified/ 복사)
    │   └── AppLogger.cs             ← 시스템 로그 (%LOCALAPPDATA%\SPD_Studio\logs\)
    └── Models\
        ├── CheckResult.cs
        └── SpdInfo.cs
```

---

## 6. 개발 주의사항 (반복 실수 방지)

- **타입 명시:** `new[] { (0x07, 0x25, "RAmos") }` → `new (byte, byte, string)[] { ... }` 로 명시 안 하면 CS1950 에러
- **타이밍 변환 공식:** `nCK = TRUNCATE((ps × 997 / tCK_ps + 1000) / 1000)`
- **CL 보정:** CL만 홀수 결과 시 +1 (짝수 보정)
- **타이밍 비교:** ps 단위 직접 비교, ±1ps 오차 허용
- **Fix 로직:** CRC는 반드시 모든 바이트 수정 마지막에 재계산 (JEDEC CRC → XMP Global CRC → XMP Profile CRC 순)
- **Module Density Fix 불가:** 단독 바이트 없음, Die Density/IO Width/Rank 3개 바이트에서 파생되므로 Fix 대상 제외
- **internal 접근자:** SpdFixer.cs가 SpdChecker의 내부 타입(PartFields, SpeedSpec 등) 사용 — 동일 어셈블리 내 `internal` 선언 필수
- **Auto-Gen 템플릿명:** `TPL_{DIMM}{Density}{Bank}{IO}{Die}{Rank}_{Speed}.sp5` — Sourcing(RM/TM/CM/BM)·DRAM Type(R)·DRAM Mfr 제외 (모두 ApplyFixes로 자동 치환). 예: `TPL_DAG58A1_WM.sp5`
- **Auto-Gen 폴더 설정:** `autogen_settings.cfg` (앱 실행 경로)에 저장·복원. `AutoGenForm` 생성자에서 `LoadSavedFolder()` 호출
- **Editor 초기화 버튼:** `_originalData` 스냅샷(로드/저장 시 갱신)을 `_data`에 복사. `_dirty=false` 후 `SyncGridFromData()` + `RefreshDisplay()`
- **VerificationLogger:** SHA256 해시로 파일 동일성 판단. 동일 해시 → 스킵. 동일 파일명+다른 해시 → 수정된 파일로 신규 행 추가. **파일 복사 안 함** — PASS/FAIL/INCOMPLETE 전부 CSV 이력에만 기록. INCOMPLETE = SKIP 1개 이상.
- **verification_log.csv:** 검증 대상 파일과 **같은 소스 폴더에 직접** 생성(Verified/ 폴더 없음). 컬럼: FileName, SHA256, CheckDate(날짜+시각), OverallResult, Pass/Fail/SkipCount. 엑셀에서 바로 열림.
- **AppLogger 로그 위치:** `%LOCALAPPDATA%\SPD_Studio\logs\app_YYYYMMDD.log` (사용자 본인 폴더라 권한 이슈 없음). 7일 경과 자동 삭제. `Program.Main` 시작 시 `AppLogger.Init()` 호출 + 전역 예외 핸들러 2개(`Application.ThreadException` / `AppDomain.UnhandledException`) 등록 → 모든 미처리 예외 FATAL 기록 후 다이얼로그에 로그 위치 안내.
- **로그 기록 정책:** 정상 클릭(Browse/Clear/Filter 등)은 로그 안 함. 결과 행위(Run/Save/Load/AutoFix/CRC/AutoGen)는 INFO. 사용자 실수(파일 미선택 Run / 작업자 미입력 AutoGen 등)는 WARN. 시스템 예외는 ERROR + stack trace. Hex 셀 편집 오타는 빈도 너무 높아 스킵.

---

@.claude/docs/part_number_parsing.md
@.claude/docs/jesd400_bytes.md
@.claude/docs/phase4_crc.md
@.claude/docs/xmp_bytes.md
@.claude/docs/phase_editor_autogen.md
