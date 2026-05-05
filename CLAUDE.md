# DDR5 SPD Checker — 프로젝트 규칙 (CLAUDE.md)

**참조 표준:** `JESD400-5C_DDR5.pdf` / `JEDEC ID_2025 (1).pdf`  
**핵심 가정:** `.sp5` 파일명 = Part Number (정확하다고 가정)

---

## 1. 작업 진행 규칙 (必守)

> **명시적 승인 없이 파일 생성·수정·삭제 금지.**

1. 새 기능·변경은 **계획 텍스트 제시 → 승인 → 코드 작성** 순서.
2. 기존 파일 수정 시 변경 범위 먼저 명시.
3. **양쪽 경로 모두 수정 필수:** 워크트리(`.claude/worktrees/...`)에만 수정하면 `C:\JSH_Folder\SPD_Check_PGM`에 반영 안 됨. 항상 main 경로 직접 수정 또는 머지.
4. **빌드 명령:** `dotnet build C:\JSH_Folder\SPD_Check_PGM\SPD_Checker\SPD_Checker.csproj -c Debug`

### 코딩 원칙 (Andrej Karpathy)

> 불필요한 변경·과도한 추상화·착수 후 수정을 줄이기 위한 가이드라인.  
> 빠른 속도보다 신중함을 우선하므로, 단순한 작업은 판단에 따라 조정.

**5. 코딩 전에 먼저 생각하라 (Think Before Coding)**

- 가정은 명시적으로 표현. 불확실하면 질문.
- 여러 해석이 가능하면 제시 — 묵묵히 선택하지 말 것.
- 더 단순한 접근이 있으면 말할 것. 필요하면 반론.
- 불명확하면 멈추고, 무엇이 혼란스러운지 명시한 뒤 질문.

**6. 단순함 우선 (Simplicity First)**

- 요청된 것만 해결하는 최소 코드.
- 단일 용도 코드에 추상화 없음.
- 요청하지 않은 "유연성"·"설정 가능성" 없음.
- 불가능한 시나리오에 대한 오류 처리 없음.
- 200줄로 작성했는데 50줄이면 충분하다면 다시 작성.

**7. 외과적 변경 (Surgical Changes)**

- 수정 범위 최소화 — 인접 코드·주석·포맷 "개선" 금지.
- 망가지지 않은 것은 리팩터링 금지.
- 기존 스타일에 맞춤 (개인 취향 배제).
- 관련 없는 dead code 발견 시 언급만 — 삭제 금지.
- 내 변경으로 생긴 orphan(import/변수/함수)은 직접 제거.

**8. 목표 주도 실행 (Goal-Driven Execution)**

- 작업을 검증 가능한 목표로 변환:
  - "검증 추가" → "잘못된 입력 테스트 작성 → 통과"
  - "버그 수정" → "재현 테스트 작성 → 통과"
  - "X 리팩터" → "전후 테스트 통과 확인"
- 다단계 작업은 간략한 계획을 먼저 제시:
  ```
  1. [단계] → 검증: [확인 항목]
  2. [단계] → 검증: [확인 항목]
  ```

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
| Editor | SPD Editor / Auto-Gen 3-mode 구조 — E-0~E-3 ✅ (LaunchForm / SpdParser·SpdInfo / Hex Grid + 실시간 검증 + CRC) / E-4부터 진행 | 🔄 진행 중 (→ phase_editor_autogen.md) |

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
C:\JSH_Folder\SPD_Check_PGM\
├── CLAUDE.md
├── JESD400-5C_DDR5.pdf
├── JEDEC ID_2025 (1).pdf
├── .claude\
│   ├── settings.local.json          ← Bash/PowerShell 자동 승인 설정
│   └── docs\
│       ├── part_number_parsing.md   ← 파트 넘버 파싱 규칙 + Speed 코드 표
│       ├── jesd400_bytes.md         ← JESD400-5C Byte 위치 + JEDEC ID 전체 참조
│       ├── phase4_crc.md            ← Phase 4 CRC 설계 (구현 가이드 포함)
│       └── xmp_bytes.md             ← Intel XMP 3.0 전체 Byte 위치 참조
└── SPD_Checker\
    ├── SPD_Checker.csproj
    ├── Program.cs
    ├── MainForm.cs
    ├── Logic\SpdChecker.cs          ← 핵심 검증 로직
    ├── Logic\SpdFixer.cs            ← FAIL 항목 자동 수정 로직
    └── Models\CheckResult.cs
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

---

@.claude/docs/part_number_parsing.md
@.claude/docs/jesd400_bytes.md
@.claude/docs/phase4_crc.md
@.claude/docs/xmp_bytes.md
@.claude/docs/phase_editor_autogen.md
