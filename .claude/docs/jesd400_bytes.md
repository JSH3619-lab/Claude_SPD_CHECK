# JESD400-5C 핵심 Byte 위치 참조

---

## General Config (Bytes 0~127) — Phase 3 검증 대상

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
| tAA min LSB | 30 | 0x01E | [7:0] | ps 단위 16-bit 하위 | CL × tCK(ps) 계산값과 비교 |
| tAA min MSB | 31 | 0x01F | [7:0] | ps 단위 16-bit 상위 | |
| tRCD min LSB | 32 | 0x020 | [7:0] | ps 단위 16-bit 하위 | tRCD × tCK(ps) 계산값과 비교 |
| tRCD min MSB | 33 | 0x021 | [7:0] | ps 단위 16-bit 상위 | |
| tRP min LSB | 34 | 0x022 | [7:0] | ps 단위 16-bit 하위 | tRP × tCK(ps) 계산값과 비교 |
| tRP min MSB | 35 | 0x023 | [7:0] | ps 단위 16-bit 상위 | |

---

## Module Organization (Annex A Common Byte) — Rank 검증

| 항목 | Byte (Dec) | Hex | 비트 | 인코딩 | Phase 3 체크 |
|------|-----------|-----|------|--------|-------------|
| Module Organization | 234 | 0x0EA | [5:3] | 000=1Rank / 001=2Rank / 010=3Rank ... | 파일명 Rank 코드와 비교 |
| Rank Mix | 234 | 0x0EA | [6] | 0=Symmetrical / 1=Asymmetrical | |

---

## Manufacturing Info (Bytes 512~639)

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

---

## CRC (Bytes 510~511, 0x1FE~0x1FF) — Phase 4 검증 대상

| 항목 | 내용 |
|------|------|
| 계산 범위 | Byte 0 ~ 509 (0x000 ~ 0x1FD), 총 510바이트 |
| CRC LSB | Byte 510 (0x1FE) |
| CRC MSB | Byte 511 (0x1FF) |
| 바이트 순서 | Little-Endian |

> Bytes 448~509 (Block 7 Reserved, 모두 0x00)도 계산 범위에 포함됨.

---

## Byte 인코딩 세부 표

### Byte 4 — Die Density (bits 4~0)

| 파일명 코드 | 의미 | Byte 4 bits[4:0] | Hex |
|------------|------|-----------------|-----|
| '4' | 4 Gb | 00001 | 0x01 |
| '8' | 8 Gb | 00010 | 0x02 |
| 'A' | 16 Gb | 00100 | 0x04 |
| 'H' | 24 Gb | 00101 | 0x05 |
| 'B' | 32 Gb | 00110 | 0x06 |

> Byte 4 bits[7:5] = Die Per Package: 000=Monolithic / 001=DDP / 010=2H 3DS / 011=4H 3DS / 100=8H 3DS / 101=16H 3DS  
> Byte 4 전체 = (DiePerPkg << 5) | DensityCode

### Byte 6 — I/O Width (bits 7~5)

| 파일명 코드 | 의미 | Byte 6 bits[7:5] | Byte 6 전체값 |
|------------|------|-----------------|--------------|
| '4' | X4 | 000 | 0x00 |
| '8' | X8 | 001 | 0x20 |
| '6' | X16 | 010 | 0x40 |

### Byte 7 — Bank Groups / Banks Per Bank Group

| 파일명 Bank 코드 | 총 Bank 수 | Bank Groups (bits 7~5) | Banks/BG (bits 2~0) | Byte 7 값 |
|----------------|-----------|----------------------|---------------------|----------|
| '4' | 16 Bank | 010 (4 BG) | 010 (4 Banks/BG) | 0x42 |
| '5' | 32 Bank | 011 (8 BG) | 010 (4 Banks/BG) | 0x62 |

> bits[4:3] = Reserved, must be 00

### Byte 3 — Module Type (bits 3~0)

| 파일명 DIMM Type | 의미 | Byte 3 값 |
|----------------|------|----------|
| 'S' | SODIMM | 0x03 |
| 'D' | UDIMM (288-pin) | 0x02 |
| 'G' | Gaming UDIMM (288-pin) | 0x02 (JEDEC 동일) |
| 'C' | Comp | 미정의 |

### Byte 234 — Module Organization (Rank)

| 파일명 Rank 코드 | 의미 | Byte 234 bits[5:3] | Byte 234 값 |
|----------------|------|-------------------|------------|
| '1' | 1 Package Rank | 000 | 0x00 |
| '2' | 2 Package Ranks | 001 | 0x08 |

> bit[6] = 0 (Symmetrical), bits[2:0] = 000 (Reserved)

---

## JEDEC ID 인코딩 규칙 (JEP106)

- **Byte 512 bits 6~0**: continuation code 수 (= Bank 번호 - 1)
- **Byte 512 bit 7**: bits 6~0에 대한 odd parity
- **Byte 513**: 해당 Bank 내 제조사 코드

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
| ADATA | - | 0x04 | 0xCB | Module Mfr 라우팅용 (Skip 대상) |

---

## Module Density 계산 공식 (참고)

```
Total_GB = (Die_Density_Gb × Dies_per_pkg × Package_Ranks × 64) / (IO_Width_bits × 8)
```

| 파일명 코드 | 용량 | 검증 예시 (x8, 16Gb, 1R) |
|------------|------|------------------------|
| 8G | 8 GB | 8Gb × 1 × 1 × 64 / (8×8) = 8 GB ✓ |
| AG | 16 GB | 16Gb × 1 × 1 × 64 / (8×8) = 16 GB ✓ |
| BG | 32 GB | 16Gb × 1 × 2 × 64 / (8×8) = 32 GB ✓ |
