# Intel XMP 3.0 Byte 위치 참조

**출처:** Intel Extreme Memory Profile (XMP) 3.0 Specification Rev 1.01 (March 2021)  
**위치:** JEDEC DDR5 SPD End User Bytes **640~1023** (총 384 bytes)

---

## 전체 구조 개요

XMP 3.0 = **6개 섹션 × 64 bytes** (각 섹션 끝에 CRC 2 bytes 포함)

| 섹션 | Byte 범위 | 용도 |
|------|-----------|------|
| Global | 640~703 | 모든 프로파일 공통 데이터 |
| Profile 1 | 704~767 | Enthusiast / Certified Settings |
| Profile 2 | 768~831 | Extreme Settings (Optional) |
| Profile 3 | 832~895 | Extreme Settings (Optional) |
| Profile 4 | 896~959 | User Settings (사용자 저장용) |
| Profile 5 | 960~1023 | User Settings (사용자 저장용) |

> **참고:** Profile 구조는 5개 모두 동일. 아래 Profile 내부 오프셋 표는 Profile 1 기준이며 나머지도 동일하게 적용.

---

## 1. XMP 존재 여부 확인 (Identification)

| Byte | 기대값 | 설명 |
|------|--------|------|
| 640 | `0x0C` | XMP ID String (상위) |
| 641 | `0x4A` | XMP ID String (하위) |
| 642 | `0x30` | XMP Version = 3.0 |

> Byte 640~641 = `0x0C4A` 이어야 XMP 3.0 유효 모듈.  
> 이 값이 없으면 XMP 미지원 모듈로 판단.

---

## 2. Global Section: Bytes 640~703

| Byte (Dec) | Hex | 항목 | 인코딩 |
|-----------|-----|------|--------|
| 640~641 | 0x280~0x281 | XMP ID String | `0x0C` / `0x4A` 고정 |
| 642 | 0x282 | XMP Version | `0x30` = Version 3.0 |
| 643 | 0x283 | Profiles Enabled | 아래 비트맵 표 |
| 644 | 0x284 | Recommended Channel Config | 아래 비트맵 표 |
| 645~646 | 0x285~0x286 | PMIC Vendor ID | 2바이트 Vendor ID |
| 647 | 0x287 | Number of PMICs on DIMM | 정수값 |
| 648 | 0x288 | PMIC Capabilities | 아래 비트맵 표 |
| 649~653 | 0x289~0x28D | RSVD | 0x00 고정 |
| 654~669 | 0x28E~0x29D | Profile 1 String Name | ASCII 16자 |
| 670~685 | 0x29E~0x2AD | Profile 2 String Name | ASCII 16자 |
| 686~701 | 0x2AE~0x2BD | Profile 3 String Name | ASCII 16자 |
| 702 | 0x2BE | Global Section CRC LSB | CRC-16 (Bytes 640~701) |
| 703 | 0x2BF | Global Section CRC MSB | CRC-16 (Bytes 640~701) |

### Byte 643 — Profiles Enabled/Disabled

| 비트 | 항목 | 인코딩 |
|------|------|--------|
| Bit 0 | Profile 1 Enabled | **항상 1** (필수) |
| Bit 1 | Profile 2 Enabled | 0=Disabled / 1=Enabled |
| Bit 2 | Profile 3 Enabled | 0=Disabled / 1=Enabled |
| Bits 7~3 | RSVD | 0x00 |

### Byte 644 — Recommended Channel Config

| 비트 | 항목 | 인코딩 |
|------|------|--------|
| Bits 1~0 | Profile 1 DPC | 00=1DPC / 01=2DPC / 10=3DPC / 11=4DPC |
| Bits 3~2 | Profile 2 DPC | 동일 |
| Bits 5~4 | Profile 3 DPC | 동일 |
| Bits 7~6 | RSVD | 0 |

### Byte 648 — PMIC Capabilities

| 비트 | 항목 | 인코딩 |
|------|------|--------|
| Bit 0 | PMIC OC 지원 여부 | 0=JEDEC PMIC / 1=OC PMIC |
| Bit 1 | Current PMIC OC 활성화 | 0=Disabled / 1=Enabled |
| Bit 3 | 전압 기본 스텝 크기 | 0=5mV / 1=10mV |
| Bits 7~4 | RSVD | 0 |

---

## 3. Profile Section 내부 구조 (각 Profile 공통)

각 프로파일은 **64 bytes**. Profile N의 시작 주소를 `BASE_N`이라 할 때:

| BASE+N | Profile 1 절대 Byte | 항목 | 인코딩 |
|--------|-------------------|------|--------|
| +0 | 704 | Module VPP Voltage Level | 전압 인코딩 (아래 참조) |
| +1 | 705 | Module VDD Voltage Level | 전압 인코딩 |
| +2 | 706 | Module VDDQ Voltage Level | 전압 인코딩 |
| +3 | 707 | Module TBD Voltage Level | **현재 RSVD** (0x00) |
| +4 | 708 | Memory Controller Voltage Level | 전압 인코딩 |
| +5 | 709 | tCKAVGmin LSB | **ps 단위** 16-bit Little-Endian |
| +6 | 710 | tCKAVGmin MSB | |
| +7 | 711 | CAS Latencies Supported, Byte 1 | 비트맵 (JEDEC 동일 방식) |
| +8 | 712 | CAS Latencies Supported, Byte 2 | |
| +9 | 713 | CAS Latencies Supported, Byte 3 | |
| +10 | 714 | CAS Latencies Supported, Byte 4 | |
| +11 | 715 | CAS Latencies Supported, Byte 5 | |
| +12 | 716 | CAS Latencies Supported, RSVD | 0x00 |
| +13 | 717 | tAAmin LSB | **ps 단위** 16-bit |
| +14 | 718 | tAAmin MSB | |
| +15 | 719 | tRCDmin LSB | **ps 단위** 16-bit |
| +16 | 720 | tRCDmin MSB | |
| +17 | 721 | tRPmin LSB | **ps 단위** 16-bit |
| +18 | 722 | tRPmin MSB | |
| +19 | 723 | tRASmin LSB | **ps 단위** 16-bit |
| +20 | 724 | tRASmin MSB | |
| +21 | 725 | tRCmin LSB | **ps 단위** 16-bit |
| +22 | 726 | tRCmin MSB | |
| +23 | 727 | tWRmin LSB | **ps 단위** 16-bit |
| +24 | 728 | tWRmin MSB | |
| +25 | 729 | tRFC1min LSB | **ns 단위** 16-bit (DDR5 16Gb = 295 ns) |
| +26 | 730 | tRFC1min MSB | |
| +27 | 731 | tRFC2min LSB | **ns 단위** 16-bit (DDR5 16Gb = 160 ns) |
| +28 | 732 | tRFC2min MSB | |
| +29 | 733 | tRFCsb LSB | **ns 단위** 16-bit |
| +30 | 734 | tRFCsb MSB | |
| +31~58 | 735~762 | RSVD | 0x00 |
| +59 | 763 | Advanced Memory Overclocking Features | 비트맵 |
| +60 | 764 | System Command Rate Mode | 인코딩 미공개 |
| +61 | 765 | Vendor Personality Byte | 벤더 정의 |
| +62 | 766 | Profile CRC LSB | CRC-16 (해당 Profile +0~+61) |
| +63 | 767 | Profile CRC MSB | |

### 각 Profile의 절대 BASE 주소

| Profile | BASE | CRC 위치 |
|---------|------|---------|
| Profile 1 | 704 | 766~767 |
| Profile 2 | 768 | 830~831 |
| Profile 3 | 832 | 894~895 |
| Profile 4 | 896 | 958~959 |
| Profile 5 | 960 | 1022~1023 |

---

## 4. 전압 인코딩 (VPP / VDD / VDDQ / TBD / MCU 공통)

```
Byte [7]     : RSVD = 0
Byte [6:5]   : Subfield A — Integer 부분
               00 = 0.xV (VPP에서만)
               01 = 1.xV
               10 = 2.xV
               11 = Undefined
Byte [4:0]   : Subfield B — 소수점 부분 (0.05V 단위)
               00000 = .00V
               00001 = .05V
               ...
               10011 = .95V
```

**계산식:** `Voltage = SubfieldA_int + SubfieldB × 0.05`

**주요 전압 → 16진수 변환 예시:**

| 전압 | SubA | SubB | Hex | 용도 |
|------|------|------|-----|------|
| 1.1V | 01 | 00010 | 0x22 | DDR5 표준 VDD |
| 1.35V | 01 | 00111 | 0x27 | Low Power VDD |
| 1.4V | 01 | 01000 | 0x28 | |
| 1.5V | 01 | 01010 | 0x2A | DDR5 표준 VPP |
| 2.0V | 10 | 00000 | 0x40 | |
| 2.5V | 10 | 01010 | 0x4A | DDR5 VPP 2.5V |

> **VPP JEDEC 최소:** 1.5V  
> **VDD JEDEC 최소:** 0.8V  
> **VDDQ JEDEC 최소:** 0.8V

---

## 5. CRC 규칙

XMP의 CRC는 JEDEC DDR5 SPD와 **동일한 CRC-16** 알고리즘 사용.

| 항목 | 값 |
|------|-----|
| 알고리즘 | CRC-16 |
| 다항식 | `0x1021` |
| 초기값 | `0x0000` |
| 입출력 반전 | 없음 |
| 저장 방식 | Little-Endian (LSB 먼저) |

### 각 섹션별 CRC 계산 범위

| 섹션 | 계산 범위 | CRC 저장 위치 |
|------|-----------|--------------|
| Global | Bytes 640~701 (62 bytes) | 702(LSB) / 703(MSB) |
| Profile 1 | Bytes 704~765 (62 bytes) | 766(LSB) / 767(MSB) |
| Profile 2 | Bytes 768~829 (62 bytes) | 830(LSB) / 831(MSB) |
| Profile 3 | Bytes 832~893 (62 bytes) | 894(LSB) / 895(MSB) |
| Profile 4 | Bytes 896~957 (62 bytes) | 958(LSB) / 959(MSB) |
| Profile 5 | Bytes 960~1021 (62 bytes) | 1022(LSB) / 1023(MSB) |

### C# CRC 구현 (JEDEC/XMP 공통)

```csharp
private static ushort ComputeCrc16(byte[] data, int offset, int length)
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
```

---

## 6. nCK 변환 공식 (XMP)

XMP 스펙은 JEDEC와 **보정 계수가 다름** (998 vs 997):

```
nCK = TRUNCATE( (param_ps × 1000 / tCK_ps + 998) / 1000 )
```

> JESD400-5C 기존 공식: `TRUNCATE((ps × 997 / tCK_ps + 1000) / 1000)`  
> 두 공식은 수학적으로 동일 결과 (XMP 스펙 명시). 충돌 시 XMP 공식 우선.

---

## 7. XMP 검증 설계 (확정 항목)

### 트리거 조건

```
Speed 코드 ∈ {CM, CQ, CR, CS} (6000 이상) → XMP 검증 수행
Speed 코드 ∈ {QK, WM}         (5600 이하) → XMP 검증 스킵
```

---

### JEDEC SPD 검증 기준 (6000 이상 파트 공통)

6000 이상 속도 파트의 JEDEC SPD 영역(Byte 0~511)은 **WM(DDR5-5600) 타이밍**으로 검증.

| 항목 | 기대값 |
|------|--------|
| tCKAVGmin | `0x0165` (357 ps) |
| tAAmin | 46 × 357 = 16,422 ps |
| tRCDmin | 45 × 357 = 16,065 ps |
| tRPmin | 45 × 357 = 16,065 ps |

> Phase 1~4(파트 넘버/Mfr ID/타이밍/CRC)는 WM 기준 그대로 검증.

---

### XMP Profile 구성 규칙 (파트 속도별)

| 파트 P/N 속도 | Speed 코드 | Profile 1 | Profile 2 |
|-------------|-----------|-----------|-----------|
| DDR5-6000 | CM | CM (6000) | **없음** |
| DDR5-6400 | CQ | CQ (6400) | CM (6000) |
| DDR5-6800 | CR | CR (6800) | CQ (6400) |
| DDR5-7200 | CS | CS (7200) | CR (6800) |

> Profile 2가 없는 경우(CM) → Profile 2 검증 전체 Skip.

---

### Profile Name String 포맷

Profile Name String은 16 ASCII bytes (Global Section 내 Byte 654~669/670~685/686~701).  
나머지 자리는 `0x20` (space) 패딩.

```
RAmos 실제 예시:
  Profile 1 Name (Byte 654~669): "RM-6000-34-44-84"
  Profile 2 Name (Byte 670~685): "RM-5600-40-40-84"
  Profile 3 Name (Byte 686~701): "                " (미사용 = space 패딩)

형식: [Brand]-[DataRate]-[CL]-[tRCD]-[tRAS]
```

| 필드 | 설명 | 단위 | RAmos 고정값 |
|------|------|------|------------|
| Brand | 제품 브랜드 | ASCII | **`RM` 고정** |
| DataRate | 데이터 레이트 | MT/s (예: 6000) | Speed 코드 대응 |
| CL | CAS Latency | nCK | — |
| tRCD | RAS to CAS Delay | nCK | — |
| tRAS | Row Active Strobe | nCK | — |

> **검증 방식 (2단계):**  
> ① Brand = `"RM"` 확인 → 다르면 FAIL  
> ② Name String 파싱값(DataRate/CL/tRCD/tRAS) ↔ 해당 Profile 실제 타이밍 바이트 **교차 검증**  
> Name String에 기재된 값과 실제 Byte 값이 다르면 기입 오류로 FAIL 처리.

---

### 확정 검증 항목

| 순서 | 항목 | 검증 내용 | 판정 |
|------|------|-----------|------|
| 1 | XMP 식별 | Byte 640=`0x0C`, 641=`0x4A`, 642=`0x30` | P/F |
| 2 | Profiles Enabled | Byte 643: CM=`0x01`, CQ/CR/CS=`0x03` | P/F |
| 3 | Global Section CRC | Byte 640~701 계산값 vs Byte 702~703 저장값 | P/F |
| 4 | Profile 1 VPP | `0x30` (1.8V) 고정 | P/F |
| 5 | Profile 1 VDD | Bank/VDD 코드 기대값과 비교 | P/F |
| 6 | Profile 1 VDDQ | Bank/VDD 코드 기대값과 비교 | P/F |
| 7 | Profile 1 tCKAVGmin | 파트 Speed 코드 기대값과 비교 | P/F |
| 8 | Profile 1 tAAmin | CL × tCK(ps) 기대값과 비교 | P/F |
| 9 | Profile 1 tRCDmin | nCK × tCK(ps) 기대값과 비교 | P/F |
| 10 | Profile 1 tRPmin | nCK × tCK(ps) 기대값과 비교 | P/F |
| 11 | Profile 1 Name String | Name의 Speed/CL/tRCD/tRAS ↔ 실제 타이밍 바이트 교차 검증 | P/F |
| 12 | Profile 1 CRC | Byte 704~765 계산값 vs Byte 766~767 저장값 | P/F |
| 13 | Profile 2 VPP | `0x30` (1.8V) 고정 | P/F |
| 14 | Profile 2 VDD | Bank/VDD 코드 기대값과 비교 | P/F |
| 15 | Profile 2 VDDQ | Bank/VDD 코드 기대값과 비교 | P/F |
| 16 | Profile 2 tCKAVGmin | 한 단계 낮은 Speed 코드 기대값과 비교 | P/F |
| 17 | Profile 2 tAAmin | CL × tCK(ps) 기대값과 비교 | P/F |
| 18 | Profile 2 tRCDmin | nCK × tCK(ps) 기대값과 비교 | P/F |
| 19 | Profile 2 tRPmin | nCK × tCK(ps) 기대값과 비교 | P/F |
| 20 | Profile 2 Name String | Name의 Speed/CL/tRCD/tRAS ↔ 실제 타이밍 바이트 교차 검증 | P/F |
| 21 | Profile 2 CRC | Byte 768~829 계산값 vs Byte 830~831 저장값 | P/F |

> **타이밍 비교:** ±1ps 오차 허용  
> **Profile 2 (순서 13~21):** CM 파트는 전체 Skip, 나머지는 아래 기대값 표 참조  
> **Name String 교차 검증:** tRAS 기대값은 Speed 코드별로 별도 정의 불필요 — Name String의 tRAS nCK × tCKAVGmin(ps) 계산값과 실제 tRASmin 바이트를 직접 비교

---

### 전압 기대값 (Bank/VDD 코드 → XMP Hex)

| Bank 코드 | 속도 | 기대 VDD | 기대 VDDQ | VDD Byte | VDDQ Byte |
|----------|------|---------|---------|----------|----------|
| `5` | 4800 / 5600 | 1.1V | 1.1V | `0x22` | `0x22` |
| `6` | 6000 / 6400 | 1.35V | 1.35V | `0x27` | `0x27` |
| `7` | 6800 / 7200 | 1.4V | 1.4V | `0x28` | `0x28` |

> VDD = Profile BASE+1, VDDQ = Profile BASE+2

---

### 타이밍 기대값 (Speed 코드별 — Profile 1/2 공통 참조)

| Speed 코드 | 속도 | tCK (ps) | tCKAVGmin | CL | tRCD(nCK) | tRP(nCK) | tAAmin (ps) | tRCDmin (ps) | tRPmin (ps) |
|-----------|------|----------|-----------|----|-----------|---------|-----------:|------------:|----------:|
| WM | DDR5-5600 | 357 | `0x0165` | 46 | 45 | 45 | 16,422 | 16,065 | 16,065 |
| CM | DDR5-6000 | 333 | `0x014D` | 34 | 44 | 44 | 11,322 | 14,652 | 14,652 |
| CQ | DDR5-6400 | 312 | `0x0138` | 36 | 44 | 44 | 11,232 | 13,728 | 13,728 |
| CR | DDR5-6800 | 294 | `0x0126` | 36 | 44 | 44 | 10,584 | 12,936 | 12,936 |
| CS | DDR5-7200 | 277 | `0x0115` | 38 | 46 | 46 | 10,526 | 12,742 | 12,742 |

> **tAAmin** = CL × tCK_ps (CL 홀수 시 +1 보정 후 계산)  
> **tRCDmin / tRPmin** = nCK × tCK_ps

---

### Profile 2 기대 Speed (파트 Speed 코드 → P2 기준 Speed)

| 파트 Speed | P2 기준 Speed | P2 Bank 코드 |
|-----------|-------------|------------|
| CM | Skip | — |
| CQ | WM → **CM** | `6` |
| CR | **CQ** | `6` |
| CS | **CR** | `7` |

---

### UI 분리 요건

- XMP 검증 결과는 기존 JEDEC SPD 검증 결과와 **시각적으로 구분** (섹션 헤더 또는 배경색 구분)
- XMP 항목의 CheckItem 접두사: `[XMP]` 또는 별도 섹션으로 분류

---

## 8. 구현 가이드 (다음 세션용)

> **전제:** Phase 1~4 (JEDEC SPD 검증) 완료 상태에서 XMP 검증 추가.  
> **수정 파일:** `SPD_Checker\Logic\SpdChecker.cs` 단일 파일.

---

### Step 0 — JEDEC Speed 검증 로직 수정 (기존 코드 버그)

**문제:** 현재 `CheckSpeed()`는 파트 Speed 코드(CM/CQ/CR/CS)로 JEDEC SPD를 직접 검증.  
6000 이상 파트의 JEDEC SPD는 **WM(5600) 타이밍**이어야 하므로 수정 필요.

**수정 내용 (2곳):**

```csharp
// 1. SPEED_MAP 내 CR 의 CL 수정 (44 → 36 오탈자)
{ "CR", new SpeedSpec { Name="DDR5-6800", TckPs=294, TckAvgMin=0x0126, CL=36, TrcdNck=44, TrpNck=44 } },

// 2. CheckSpeed() 내 — XMP 파트(6000 이상)는 WM 기준으로 JEDEC 타이밍 검증
// Speed 코드가 CM/CQ/CR/CS 이면 JEDEC 검증 시 WM spec 사용
private static readonly HashSet<string> XMP_SPEED_CODES =
    new HashSet<string>(StringComparer.Ordinal) { "CM", "CQ", "CR", "CS" };

// CheckSpeed() 시작 부분에 추가:
SpeedSpec jedecSpec = (f.SpeedCode != null && XMP_SPEED_CODES.Contains(f.SpeedCode))
    ? SPEED_MAP["WM"]   // 6000 이상은 JEDEC 영역은 WM 기준
    : spec;
// 이후 tCKAVGmin / tAA / tRCD / tRP 비교 시 jedecSpec 사용
```

---

### Step 1 — CheckFile() 끝에 XMP 호출 추가

```csharp
// Phase 4: CRC 다음에 추가
if (fields.Valid && XMP_SPEED_CODES.Contains(fields.SpeedCode ?? ""))
    results.AddRange(CheckXmp(fileName, fields, data));
```

---

### Step 2 — XMP 상수 및 SpeedSpec 확장

```csharp
// XMP Byte 상수
private const int XMP_GLOBAL_BASE  = 640;   // 0x280
private const int XMP_P1_BASE      = 704;   // 0x2C0
private const int XMP_P2_BASE      = 768;   // 0x300
private const int XMP_MIN_SIZE     = 832;   // Profile 2 CRC(831)까지 필요

// Global Section 내 Name String 절대 위치
private const int XMP_P1_NAME_OFFSET = 654;  // 0x28E  (16 bytes)
private const int XMP_P2_NAME_OFFSET = 670;  // 0x29E  (16 bytes)

// Profile 내 상대 오프셋
// +0  VPP, +1 VDD, +2 VDDQ, +5~6 tCKAVGmin(LE),
// +13~14 tAAmin(LE), +15~16 tRCDmin(LE), +17~18 tRPmin(LE),
// +19~20 tRASmin(LE), +62~63 CRC(LE)

// Profile 2 기준 Speed 매핑
private static readonly Dictionary<string, string> XMP_P2_SPEED =
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // CM → P2 없음 (키 없음)
        { "CQ", "CM" },
        { "CR", "CQ" },
        { "CS", "CR" },
    };
```

---

### Step 3 — CheckXmp() 메서드 구조

```csharp
private static IEnumerable<CheckResult> CheckXmp(
    string fileName, PartFields fields, byte[] data)
{
    // 파일 크기 확인
    if (data.Length < XMP_MIN_SIZE) { yield return ...; yield break; }

    // [1] XMP 식별 (Byte 640/641/642)
    // [2] Profiles Enabled (Byte 643)
    //     CM → 기대 0x01 / CQ·CR·CS → 기대 0x03
    // [3] Global CRC (640~701, 62bytes, stored 702~703)

    // [4~12] Profile 1
    //   CheckXmpProfile(fileName, fields.SpeedCode, XMP_P1_BASE,
    //                   XMP_P1_NAME_OFFSET, data, profileNum: 1)

    // [13~21] Profile 2 (CM은 Skip)
    //   if (XMP_P2_SPEED.TryGetValue(fields.SpeedCode, out string p2Code))
    //       CheckXmpProfile(fileName, p2Code, XMP_P2_BASE,
    //                       XMP_P2_NAME_OFFSET, data, profileNum: 2)
}
```

---

### Step 4 — CheckXmpProfile() 메서드 구조

```csharp
private static IEnumerable<CheckResult> CheckXmpProfile(
    string fileName, string speedCode, int baseOffset,
    int nameOffset, byte[] data, int profileNum)
{
    SpeedSpec spec = SPEED_MAP[speedCode];
    string prefix  = $"[XMP] P{profileNum}";

    // VPP  (base+0): 기대 0x30
    // VDD  (base+1): Bank 코드 기대값
    // VDDQ (base+2): Bank 코드 기대값
    // tCKAVGmin (base+5~6 LE): spec.TckAvgMin
    // tAAmin    (base+13~14 LE): spec.CL × spec.TckPs (±1ps)
    // tRCDmin   (base+15~16 LE): spec.TrcdNck × spec.TckPs (±1ps)
    // tRPmin    (base+17~18 LE): spec.TrpNck × spec.TckPs (±1ps)

    // Name String 검증 (nameOffset~nameOffset+15)
    //   파싱: "RM-[DataRate]-[CL]-[tRCD]-[tRAS]"
    //   ① Brand = "RM" 확인
    //   ② DataRate → tCK = 2,000,000 / DataRate (truncate)
    //      vs 실제 tCKAVGmin 바이트 비교
    //   ③ CL_name × tCK vs 실제 tAAmin 바이트 (±1ps)
    //   ④ tRCD_name × tCK vs 실제 tRCDmin 바이트 (±1ps)
    //   ⑤ tRAS_name × tCK vs 실제 tRASmin 바이트 (±1ps)

    // CRC (base+62~63 LE, 계산범위 base~base+61, 62bytes)
}
```

---

### Name String 파싱 로직

```csharp
// "RM-6000-34-44-84" → parts = ["RM", "6000", "34", "44", "84"]
string nameRaw   = Encoding.ASCII.GetString(data, nameOffset, 16).TrimEnd(' ', '\0');
string[] parts   = nameRaw.Split('-');
// parts[0] = Brand (기대 "RM")
// parts[1] = DataRate (int.Parse)
// parts[2] = CL      (int.Parse)
// parts[3] = tRCD    (int.Parse)
// parts[4] = tRAS    (int.Parse)
int tckFromName  = 2_000_000 / dataRate;   // truncate
int taaFromName  = cl   * tckFromName;
int trcdFromName = trcd * tckFromName;
int trasFromName = tras * tckFromName;
// 실제 바이트와 ±1ps 허용 비교
```

---

## 9. 파일 크기 요건

XMP 데이터 접근 시 SPD 파일 최소 크기:
- XMP 존재 여부만 확인: **643 bytes 이상** (Byte 642까지)
- Global Section 전체 (CRC 포함): **704 bytes 이상**
- Profile 1 전체: **768 bytes 이상**
- 모든 Profile 5까지: **1024 bytes 이상**
