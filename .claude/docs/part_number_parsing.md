# 파트 넘버 파싱 규칙

예시: `RMRDAG58A1P-GPWRRWM7-TN`

---

## 첫 번째 '-' 이전 (본체 파트)

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

---

## 첫 번째 '-' 이후 (후미 파트)

| 위치 | 항목 | 비고 |
|------|------|------|
| [0] | DRAM Mfr 코드 | G/S=RAmos / H=SK Hynix / N=Nanya / C=CXMT / M=Micron·Spectek (Micron 계열) |
| 문자열 탐색 | Speed 코드 (2자) | 아래 Speed 코드 표 참조 — 고정 위치 아님, 부분문자열 탐색 (Contains) |

---

## 접미사 처리 (StripSuffix)

| 접미사 | 처리 |
|--------|------|
| `0Y` | Part Number 비교 시 제거 (먼저 처리) |
| `-TN` | Part Number 비교 시 제거 |

---

## Speed 코드 → 기대값 매핑

| Speed 코드 | 속도 등급 | Clock | tCK (ps) | tCKAVGmin | CL | tRCD(nCK) | tRP(nCK) |
|------------|---------|-------|----------|-----------|----|-----------| ---------|
| QK | DDR5-4800 | 2400 MHz | 416 ps | 0x01A0 | 40 | 39 | 39 |
| WM | DDR5-5600 | 2800 MHz | 357 ps | 0x0165 | 46 | 45 | 45 |
| CM | DDR5-6000 | 3000 MHz | 333 ps | 0x014D | 34 | 44 | 44 |
| CP | DDR5-6400 | 3200 MHz | 312 ps | 0x0138 | 52 | 52 | 52 |
| CQ | DDR5-6400 | 3200 MHz | 312 ps | 0x0138 | 36 | 44 | 44 |
| CR | DDR5-6800 | 3400 MHz | 294 ps | 0x0126 | 36 | 44 | 44 |
| CS | DDR5-7200 | 3600 MHz | 277 ps | 0x0115 | 38 | 46 | 46 |

> **tCK 계산:** tCK_ps = truncate(2,000,000 / DataRate_MT_s)  
> **tAA 기대값 (ps):** CL × tCK_ps  
> **tRCD/tRP 기대값 (ps):** nCK × tCK_ps  
> **타이밍 비교:** ±1ps 오차 허용 (반올림 차이)  
> **CL 보정:** 계산된 CL이 홀수이면 +1 (짝수 보정)  
> **Speed 코드 파싱:** 첫 번째 '-' 이후 문자열에서 Contains 검색

---

## Speed → Bank/VDD 코드 매핑 (파일명 검증용)

속도 등급별로 기대되는 Bank/VDD 코드가 정해져 있음 (POD 기준).

| 속도 등급 | Speed 코드 | 기대 Bank/VDD 코드 | 의미 |
|-----------|-----------|-------------------|------|
| DDR5-4800 | QK | **5** | 32 Bank / POD 1.1V |
| DDR5-5600 | WM | **5** | 32 Bank / POD 1.1V |
| DDR5-6000 | CM | **6** | 32 Bank / POD 1.35V |
| DDR5-6400 | CQ | **6** | 32 Bank / POD 1.35V |
| DDR5-6800 | CR | **7** | 32 Bank / POD 1.4V |
| DDR5-7200 | CS | **7** | 32 Bank / POD 1.4V |

> **검증 방법:** 파일명에서 파싱한 Bank/VDD 코드가 Speed 코드에 대응하는 기대값과 일치하는지 확인.  
> **그룹 요약:** 4800·5600 → `5`, 6000·6400 → `6`, 6800·7200 → `7`
