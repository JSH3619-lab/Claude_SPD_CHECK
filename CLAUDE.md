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
| 1 | Part Number 검증 (파일명 vs Byte 521~550) | ✅ 완료 (커밋됨) |
| 2 | Manufacturer ID 검증 (Module Mfr / DRAM Mfr) | ✅ 완료 (커밋됨) |
| 3 | 파트 내용 전체 검증 (DIMM Type / Density / Width / Rank / Speed) | 설계 완료 — 코드 미착수 |
| 4 | CRC 검증 | 미시작 (최후 순위) |
| 5 | 기타 예외 조건 추가 | 미시작 |

---

## 5. 파트 넘버 파싱 규칙 (Phase 3 기준)

예시: `RMRDAG58A1P-GPWRRWM7-TN`

### 5-1. 첫 번째 '-' 이전 (본체 파트)

| 위치 | 자릿수 | 항목 | 코드 → 의미 |
|------|--------|------|------------|
| 0~1 | 2 | Sourcing Type | RM=RAmos DRAM / TM=RAmos 3rd / CM=CTST DRAM / BM=CTST 3rd |
| 2 | 1 | DRAM Type | 4=DDR4 / R=DDR5 |
| 3 | 1 | DIMM Type | S=SODIMM / D=UDIMM(288) / G=Gaming UDIMM / C=Comp |
| 4~5 | 1~2 | Module Density | 1G=1GB / 2G=2GB / 4G=4GB / 8G=8GB / AG=16GB / BG=32GB / CG=64GB |
| 다음 | 1 | Bank / VDD | 4=16Bank/1.2V / 5=32Bank/1.1V / 6=32Bank/1.35V / 7=32Bank/1.4V |
| 다음 | 1 | Composition | 4=X4 / 8=X8 / 6=X16 |
| 다음 | 1 | Base Die Density | 4=4Gb / 8=8Gb / A=16Gb / H=24Gb / B=32Gb |
| 다음 | 1 | Rank | 0=Comp / 1=1Rank / 2=2Rank |

> **파싱 방법:** prefix(2) 제거 후 앞에서부터 순서대로 읽음.  
> Module Density는 두 번째 문자가 'G'이면 2자리(AG, BG, CG), 아니면 1자리(1G, 2G, 4G, 8G)로 판단.

### 5-2. 첫 번째 '-' 이후 (후미 파트)

| 위치 | 항목 | 비고 |
|------|------|------|
| [0] | DRAM Mfr 코드 | G/S=RAmos / H=SK Hynix / N=Nanya / C=CXMT / M=Micron·Spectek (Micron 계열) |
| 문자열 탐색 | Speed 코드 (2자) | 아래 Speed 코드 표 참조 — 고정 위치 아님, 부분문자열 탐색 |

> **Speed 코드 탐색:** 첫 번째 '-' 이후 문자열에서 아래 표의 코드가 포함되는지 검색 (Contains).

### 5-3. 두 번째 '-' 이후

| 접미사 | 처리 |
|--------|------|
| -TN | Part Number 비교 시 제거 후 비교 (항상 PASS) |

---

## 6. JESD400-5C 핵심 Byte 위치 참조

### 6-1. General Config (Bytes 0~127) — Phase 3 검증 대상

| 항목 | Byte (Dec) | Hex | 비트 | 인코딩 | Phase 3 체크 |
|------|-----------|-----|------|--------|-------------|
| DRAM Type | 2 | 0x002 | [7:0] | 0x12 = DDR5 SDRAM | DDR5 고정값 확인 |
| Module Type | 3 | 0x003 | [3:0] | 0x01=RDIMM / 0x02=UDIMM / 0x03=SODIMM | 파일명 DIMM Type 코드와 비교 |
| Die Density + Die/Pkg | 4 | 0x004 | [7:5]=Die수 / [4:0]=Density | 아래 표 참조 | 파일명 Die Density 코드와 비교 |
| I/O Width (Composition) | 6 | 0x006 | [7:5] | 000=x4 / 001=x8 / 010=x16 | 파일명 Composition 코드와 비교 |
| Bank Groups / Banks | 7 | 0x007 | [7:5]=BG수 / [2:0]=Banks/BG | 아래 표 참조 | 파일명 Bank 코드와 비교 |
| VDD Nominal | 16 | 0x010 | [7:4] | 0000 = 1.1V (전체 0x00) | DDR5는 0x00 고정 |
| tCKAVGmin LSB | 20 | 0x014 | [7:0] | ps 단위 16-bit 값 하위 | Speed 코드로 기대값 비교 |
| tCKAVGmin MSB | 21 | 0x015 | [7:0] | ps 단위 16-bit 값 상위 | Speed 코드로 기대값 비교 |
| CAS Latency Mask | 24~28 | 0x018~0x01C | bit map | 각 비트 = 지원 CL 번호 | Speed 코드의 CL 지원 여부 확인 |
| tAA min LSB | 30 | 0x01E | [7:0] | ps 단위 16-bit 하위 | CL × tCK(ps) 계산값과 비교 |
| tAA min MSB | 31 | 0x01F | [7:0] | ps 단위 16-bit 상위 | |
| tRCD min LSB | 32 | 0x020 | [7:0] | ps 단위 16-bit 하위 | tRCD × tCK(ps) 계산값과 비교 |
| tRCD min MSB | 33 | 0x021 | [7:0] | ps 단위 16-bit 상위 | |
| tRP min LSB | 34 | 0x022 | [7:0] | ps 단위 16-bit 하위 | tRP × tCK(ps) 계산값과 비교 |
| tRP min MSB | 35 | 0x023 | [7:0] | ps 단위 16-bit 상위 | |

### 6-2. Module Organization (Annex A Common Byte) — Rank 검증

| 항목 | Byte (Dec) | Hex | 비트 | 인코딩 | Phase 3 체크 |
|------|-----------|-----|------|--------|-------------|
| Module Organization | 234 | 0x0EA | [5:3] | 000=1Rank / 001=2Rank / 010=3Rank ... | 파일명 Rank 코드와 비교 |
| Rank Mix | 234 | 0x0EA | [6] | 0=Symmetrical / 1=Asymmetrical | |

### 6-3. Manufacturing Info (Bytes 512~639)

| 항목 | Byte (Dec) | Hex 주소 | 형식 | 비고 |
|------|-----------|---------|------|------|
| Module Mfr ID 1st | 512 | 0x200 | - | continuation 수 + parity(bit7) |
| Module Mfr ID 2nd | 513 | 0x201 | - | 제조사 코드 (JEP106) |
| Module 제조 위치 | 514 | 0x202 | - | |
| Module 제조 날짜 | 515 ~ 516 | 0x203 ~ 0x204 | BCD | Year/Week |
| Module 시리얼 번호 | 517 ~ 520 | 0x205 ~ 0x208 | - | |
| Part Number | 521 ~ 550 | 0x209 ~ 0x226 | ASCII | 미사용 자리 = 0x20 (space) |
| Module Revision | 551 | 0x227 | - | |
| DRAM Mfr ID 1st | 552 | 0x228 | - | Module ID와 동일 인코딩 |
| DRAM Mfr ID 2nd | 553 | 0x229 | - | |
| DRAM Stepping | 554 | 0x22A | ASCII/HEX | 0xFF = 미정의 |

### 6-4. Byte 4 — Die Density 인코딩 (bits 4~0)

| 파일명 코드 | 의미 | Byte 4 bits[4:0] | Hex (bits only) |
|------------|------|-----------------|-----------------|
| '4' | 4 Gb | 00001 | 0x01 |
| '8' | 8 Gb | 00010 | 0x02 |
| 'A' | 16 Gb | 00100 | 0x04 |
| 'H' | 24 Gb | 00101 | 0x05 |
| 'B' | 32 Gb | 00110 | 0x06 |

> Byte 4 bits[7:5] = Die Per Package: 000=Monolithic(1die) / 001=DDP(2die) / 010=2H 3DS / 011=4H 3DS / 100=8H 3DS / 101=16H 3DS  
> 대부분의 일반 모듈은 000 (Monolithic). Byte 4 전체 = (DiePerPkg << 5) | DensityCode

### 6-5. Byte 6 — I/O Width 인코딩 (bits 7~5)

| 파일명 코드 | 의미 | Byte 6 bits[7:5] | Byte 6 전체값 |
|------------|------|-----------------|--------------|
| '4' | X4 | 000 | 0x00 |
| '8' | X8 | 001 | 0x20 |
| '6' | X16 | 010 | 0x40 |

### 6-6. Byte 7 — Bank Groups / Banks Per Bank Group

| 파일명 Bank 코드 | 총 Bank 수 | Bank Groups (bits 7~5) | Banks/BG (bits 2~0) | Byte 7 값 |
|----------------|-----------|----------------------|---------------------|----------|
| '4' | 16 Bank | 010 (4 BG) | 010 (4 Banks/BG) | 0x42 |
| '5' | 32 Bank | 011 (8 BG) | 010 (4 Banks/BG) | 0x62 |

> bits[4:3] = Reserved, must be 00

### 6-7. Byte 3 — Module Type (bits 3~0)

| 파일명 DIMM Type | 의미 | Byte 3 값 |
|----------------|------|----------|
| 'S' | SODIMM | 0x03 |
| 'D' | UDIMM (288-pin) | 0x02 |
| 'G' | Gaming UDIMM (288-pin) | 0x02 (JEDEC 동일) |
| 'C' | Comp | 미정의 |

### 6-8. Byte 234 — Module Organization (Rank)

| 파일명 Rank 코드 | 의미 | Byte 234 bits[5:3] | Byte 234 값 |
|----------------|------|-------------------|------------|
| '1' | 1 Package Rank | 000 | 0x00 |
| '2' | 2 Package Ranks | 001 | 0x08 |

> bit[6] = 0 (Symmetrical), bits[2:0] = 000 (Reserved)

### 6-9. Module Density 계산 공식

파일명의 Module Density 코드를 SPD 바이트에서 역산하는 공식 (DDR5 UDIMM 기준):

```
Total_GB = (Die_Density_Gb × Dies_per_pkg × Package_Ranks × 64) / (IO_Width_bits × 8)
```

| 파일명 코드 | 용량 | 검증 예시 (x8, 16Gb, 1R) |
|------------|------|------------------------|
| 1G | 1 GB | |
| 2G | 2 GB | |
| 4G | 4 GB | |
| 8G | 8 GB | 8Gb × 1 × 1 × 64 / (8×8) = 8 GB ✓ |
| AG | 16 GB | 16Gb × 1 × 1 × 64 / (8×8) = 16 GB ✓ |
| BG | 32 GB | 16Gb × 1 × 2 × 64 / (8×8) = 32 GB ✓ |
| CG | 64 GB | |

---

### 6-10. Speed 코드 → 기대값 매핑

| Speed 코드 | 속도 등급 | Clock | tCK (ps) | tCKAVGmin | CL | tRCD(nCK) | tRP(nCK) |
|------------|---------|-------|----------|-----------|----|-----------| ---------|
| QK | DDR5-4800 | 2400 MHz | 416 ps | 0x01A0 | 40 | 39 | 39 |
| WM | DDR5-5600 | 2800 MHz | 357 ps | 0x0165 | 46 | 45 | 45 |
| CM | DDR5-6000 | 3000 MHz | 333 ps | 0x014D | 34 | 44 | 44 |
| CP | DDR5-6400 | 3200 MHz | 312 ps | 0x0138 | 52 | 52 | 52 |
| CQ | DDR5-6400 | 3200 MHz | 312 ps | 0x0138 | 36 | 44 | 44 |
| CR | DDR5-6800 | 3400 MHz | 294 ps | 0x0126 | 44 | 44 | 44 |
| CS | DDR5-7200 | 3600 MHz | 277 ps | 0x0115 | 38 | 46 | 46 |

> **tCK 계산:** tCK_ps = truncate(2,000,000 / DataRate_MT_s)  
> **tAA 기대값 (ps):** CL × tCK_ps  
> **tRCD/tRP 기대값 (ps):** nCK × tCK_ps  
> **검증 방법:** SPD에 저장된 tCKAVGmin(Byte 20~21) 값이 기대값과 일치하는지 확인.  
> 타이밍(tAA/tRCD/tRP)은 ps 값으로 비교 시 ±1ps 오차 허용 (반올림 차이).

> **Speed 코드 파싱:** 파일명에서 첫 번째 '-' 이후 문자열에 위 코드가 포함되어 있는지 Contains 검색.

---

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
| CXMT | 11 | 0x8A | 0x91 | |
| Nanya | 4 | 0x83 | 0x0B | |
| Spectek | 3 | 0x02 | 0xB5 | Micron 계열 |

---

## 7. 기술 스택 (확정 전 — 승인 필요)

- **언어:** C#
- **프레임워크:** .NET Framework 4.7.2 (Windows 10/11 기본 내장)
- **UI:** WinForms
- **배포:** .exe 단독 실행 파일 (별도 런타임 설치 불필요)

---

## 8. UI 요구사항 (확정 전 — 승인 필요)

- 파일 업로드: Drag & Drop + 파일 탐색기 선택 (다량 동시 선택 가능)
- 진행 표시: Progress bar (현재 처리 중인 파일명 표시)
- 결과 표시: 표 형식 — 파일명 / Check 항목 / Expected 값 / Actual 값 / PASS·FAIL
- 로그: 결과를 파일로 Export (CSV 또는 TXT)

---

## 9. 현재 폴더 상태

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

> Phase 1, 2 완료 및 커밋됨. Phase 3 설계 규칙 이 파일에 기록 완료. 다음 세션에서 코드 구현 시작.
