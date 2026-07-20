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
| [0] | DRAM Mfr 코드 | G/S=Samsung (80/CE) / H=SK Hynix / N=Nanya / C=CXMT / M=Micron·Spectek (Micron 계열) |
| 문자열 탐색 | Speed 코드 (2자) | 아래 Speed 코드 표 참조 — 고정 위치 아님, 부분문자열 탐색 (Contains) |

---

## 접미사 처리 (StripSuffix)

| 접미사 | 처리 |
|--------|------|
| `0Y` | Part Number 비교 시 제거 (먼저 처리) |
| `-TN` | Part Number 비교 시 제거 |

---

## PID → SID 변환 (SPD Byte 521~550 기입값)

**파일명 = PID**, 그러나 **SPD 내부 Part No 자리(Byte 521~550)에는 SID**가 들어감.
Check 시 `BuildSid(파일명 PID)` 값과 Byte 521~550을 대조. Fix/Auto-Gen 시 SID를 기입.

> 구현: `SpdParser.BuildSid(string pid)` — null 반환 시 호출부에서 PID로 fallback.

### 변환 규칙

**HEAD (첫 '-' 이전):** Sourcing `TM→RM` / `BM→CM` 치환, 나머지 그대로.

**TAIL (첫 '-' ~ 둘째 '-' 이전):** 위치 고정 파싱

| 위치 | 필드 | 처리 |
|------|------|------|
| t[0] | I.C Brand | 유지 |
| t[1] | Comp Type | 유지 |
| t[2] | Comp Test 업체 | **제거** |
| t[3] | Module SMT 업체 | 유지 |
| t[4] | Module Test 업체 | 유지 |
| t[5]·t[6] | Speed (2자) | 유지 |
| t[7] | PCB & Revision | **색상 치환**: DDR5(`R`)→`B`(검정) / DDR4(`4`)→`G`(초록) |
| t[8] | Vendor | **제거** |
| t[9] | Purchaser | `{V,H,A}`만 유지, 그 외(특수코드·`0Y`)·이후 전부 제거 |

> TAIL 8자 미만이면 변환 불가 → null. PCB 색상은 HEAD[2](DRAM Type)로 판별.
> 구형/신형 Part가 섞여도 t[0]~t[7](IC~PCB)는 고정 정렬이라 안전.

### 검증 예시

| PID (파일명) | SID (Byte 521~550) |
|-------------|-------------------|
| `RMRDAG58A1B-SPWRRWM7SB` | `RMRDAG58A1B-SPRRWMB` |
| `TMRDAG58A1A-NZWRRWM7GH0Y` | `RMRDAG58A1A-NZRRWMBH` |
| `CMRDAG58A1P-CPWRWWM7G` | `CMRDAG58A1P-CPRWWMB` |
| `TMRSAG58A1A-NYWRRQK7GH0Y` | `RMRSAG58A1A-NYRRQKBH` |

---

## AutoGen 생성 체계 검증 (`SpdParser.ValidatePartSystem`)

AutoGen 생성 전, Part Number가 체계에 맞는지 검사. **위반 시 생성 차단**(이력 그리드 FAIL + 사유).
파일명은 `EnsureGradeSuffix`로 **`-TN` 디폴트 부착**(이미 2번째 `-` 있으면 유지).

### 자리별 허용값 (없으면 차단)

| 자리 | 허용값 |
|------|--------|
| Sourcing | RM / TM / CM / BM |
| DRAM Type | 4 / R |
| DIMM Type | S / D / G / C |
| Density | 1G / 2G / 4G / 8G / AG / BG / CG |
| Bank/VDD | 4 / 5 / 6 / 7 |
| Composition | 4 / 8 / 6 |
| Die Density | 4 / 8 / A / H / B |
| Rank | 0 / 1 / 2 |
| CompGen(#9) | A~Z (영문자 전체) |
| IC Brand(t0) | S / G / H / M / C / N |
| Comp Type(t1) | P/U/N/H/M/C/D/G/T/F/E/Q/W/J/A/X/Y/Z |
| Comp Test(t2) | R / S / A / W / G / 1 / 2 / 4 / 5 |
| Module SMT(t3) | 0 / R / E / T / G / Y / D / L / 1 / 2 / 4 / 5 |
| Module Test(t4) | 0 / R / T / G / Y / D / L / 1 / 2 / 4 / 5 / S |
| PCB(t7) | 0~9 / A / B / G / K |
| Vendor(t8, 있으면) | S / G / B / A |
| Purchaser(t9, 있으면) | 0 / V / H / A |

> Speed(t5~6)는 SPEED_MAP 등록 코드로 별도 검증. CompGen은 표가 `…`로 열려 있어 A~Z 전체 허용.

### Purchaser 규칙 (양방향)

- **외주(TM/BM)** → Purchaser(V/H/A) **필수** (없으면 차단)
- **자사(RM/CM)** → Purchaser **금지** (있으면 차단)

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
