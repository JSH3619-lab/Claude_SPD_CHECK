# DDR5 SPD Checker — 프로젝트 규칙 (CLAUDE.md)

---

## 1. 프로젝트 개요

**목적:** DDR5 SPD 파일(`.sp5`)의 내용이 규격(JESD400-5C)에 맞게 올바르게 작성되었는지 자동으로 검증하는 Windows GUI 프로그램.

**핵심 가정:** `.sp5` 파일명 = Part Number (정확하다고 가정)

**참조 표준:**
- `JESD400-5C_DDR5.pdf` — DDR5 SPD Byte 정의
- `JEDEC ID_2025 (1).pdf` — JEP106BM 제조사 ID 코드표

---

## 2. 작업 진행 규칙 (必守)

> **명시적 승인 없이 파일 생성·수정·삭제 금지.**

1. 새 기능이나 변경 사항은 **먼저 계획을 텍스트로 제시**하고 승인 후 진행.
2. 코드 작성 전 반드시 "이렇게 진행해도 될까요?" 확인.
3. 계획 단계에서는 분석·설계만, 실제 파일 작업은 승인 이후.
4. 기존 파일 수정 시 변경 범위를 먼저 명시.

---

## 3. 에이전트 모델 운영 전략 (Advisor Strategy)

서브에이전트(`Agent` 툴)를 띄울 때 아래 기준으로 모델 선택:

| 상황 | 모델 | 예시 |
|------|------|------|
| 파일 탐색, 단순 검색, 빠른 확인 | `haiku` | 특정 함수 위치 찾기, 파일 목록 확인 |
| 일반 코딩, 코드 리뷰, 중간 분석 | `sonnet` (기본) | 기능 구현, 버그 수정 |
| 복잡한 설계, 아키텍처 결정, 규격 해석 | `opus` | Phase 설계, JEDEC 규격 분석, 전체 구조 검토 |

---

## 4. 개발 Phase 계획

| Phase | 항목 | 상태 |
|-------|------|------|
| 1 | Part Number 검증 (파일명 vs Byte 521~550) | 코드 초안 존재 (미승인) |
| 2 | CRC 검증 | 미시작 |
| 3 | JEDEC Manufacturer ID 검증 | 미시작 |
| 4 | Speed 등급 Hex 값 검증 | 미시작 |
| 5 | 기타 예외 조건 추가 | 미시작 |

---

## 5. JESD400-5C 핵심 Byte 위치 참조

| 항목 | Byte (Dec) | Hex 주소 | 형식 | 비고 |
|------|-----------|---------|------|------|
| Part Number | 521 ~ 550 | 0x209 ~ 0x226 | ASCII | 미사용 자리 = 0x20 (space) |
| Module Mfr ID 1st | 512 | 0x200 | - | continuation 수 + parity(bit7) |
| Module Mfr ID 2nd | 513 | 0x201 | - | 제조사 코드 (JEP106) |
| Module 제조 위치 | 514 | 0x202 | - | |
| Module 제조 날짜 | 515 ~ 516 | 0x203 ~ 0x204 | BCD | Year/Week |
| Module 시리얼 번호 | 517 ~ 520 | 0x205 ~ 0x208 | - | |
| Module Revision | 551 | 0x227 | - | |
| DRAM Mfr ID 1st | 552 | 0x228 | - | Module ID와 동일 인코딩 |
| DRAM Mfr ID 2nd | 553 | 0x229 | - | |
| DRAM Stepping | 554 | 0x22A | ASCII/HEX | 0xFF = 미정의 |

### JEDEC ID 인코딩 규칙 (JEP106)
- **Byte 512 bits 6~0**: continuation code 수 (= Bank 번호 - 1)
- **Byte 512 bit 7**: bits 6~0에 대한 odd parity
- **Byte 513**: 해당 Bank 내 제조사 코드 (last non-zero byte)

### 주요 제조사 JEDEC ID

| 제조사 | Bank | Byte 512 | Byte 513 | 비고 |
|--------|------|---------|---------|------|
| RAmos Technology | 8 | 0x07 | 0x25 | Module Mfr **고정값** |
| SK Hynix | 1 | 0x80 | 0xAD | |
| Micron | 1 | 0x80 | 0x2C | |
| Samsung | 1 | 0x80 | 0xCE | |
| CXMT | TBD | TBD | TBD | 추후 확인 필요 |
| Nanya | TBD | TBD | TBD | 추후 확인 필요 |
| Spectek | TBD | TBD | TBD | Micron 계열, 확인 필요 |

---

## 6. 기술 스택 (확정 전 — 승인 필요)

- **언어:** C#
- **프레임워크:** .NET Framework 4.7.2 (Windows 10/11 기본 내장)
- **UI:** WinForms
- **배포:** .exe 단독 실행 파일 (별도 런타임 설치 불필요)

---

## 7. UI 요구사항 (확정 전 — 승인 필요)

- 파일 업로드: Drag & Drop + 파일 탐색기 선택 (다량 동시 선택 가능)
- 진행 표시: Progress bar (현재 처리 중인 파일명 표시)
- 결과 표시: 표 형식 — 파일명 / Check 항목 / Expected 값 / Actual 값 / PASS·FAIL
- 로그: 결과를 파일로 Export (CSV 또는 TXT)

---

## 8. 현재 폴더 상태

```
C:\JSH_Folder\SPD_Check_PGM\
├── CLAUDE.md                        ← 이 파일
├── JESD400-5C_DDR5.pdf
├── JEDEC ID_2025 (1).pdf
└── SPD_Checker\                     ← 미승인 초안 코드 (검토 후 결정)
    ├── SPD_Checker.csproj
    ├── Program.cs
    ├── MainForm.cs
    ├── Logic\SpdChecker.cs
    └── Models\CheckResult.cs
```

> `SPD_Checker\` 폴더는 이전에 무단으로 생성된 초안입니다.  
> 검토 후 사용 여부를 결정해 주세요. 삭제 요청 시 즉시 삭제합니다.
