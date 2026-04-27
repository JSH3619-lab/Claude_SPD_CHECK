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
| 3 | 파트 내용 검증 (DIMM Type / Density / Width / Rank / Speed) | ✅ 완료 |
| 4 | CRC 검증 | 설계 완료 — 코드 미착수 |

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
│       └── phase4_crc.md            ← Phase 4 CRC 설계 (구현 가이드 포함)
└── SPD_Checker\
    ├── SPD_Checker.csproj
    ├── Program.cs
    ├── MainForm.cs
    ├── Logic\SpdChecker.cs          ← 핵심 검증 로직
    └── Models\CheckResult.cs
```

---

## 6. 개발 주의사항 (반복 실수 방지)

- **타입 명시:** `new[] { (0x07, 0x25, "RAmos") }` → `new (byte, byte, string)[] { ... }` 로 명시 안 하면 CS1950 에러
- **타이밍 변환 공식:** `nCK = TRUNCATE((ps × 997 / tCK_ps + 1000) / 1000)`
- **CL 보정:** CL만 홀수 결과 시 +1 (짝수 보정)
- **타이밍 비교:** ps 단위 직접 비교, ±1ps 오차 허용

---

@.claude/docs/part_number_parsing.md
@.claude/docs/jesd400_bytes.md
@.claude/docs/phase4_crc.md
