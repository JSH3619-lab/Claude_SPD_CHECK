# Phase 4 — CRC 검증 설계

상태: **설계 완료 — 코드 미착수**

> **출처:** JESD400-5C Table 10, Table 13 (Address Map p.12~13), Section 7 Address Map

---

## CRC 블록 구조

| 항목 | 내용 |
|------|------|
| 계산 범위 | Byte 0 ~ 509 (0x000 ~ 0x1FD), 총 510바이트 |
| CRC 저장 위치 | Byte 510 (0x1FE) = LSB, Byte 511 (0x1FF) = MSB |
| 바이트 순서 | Little-Endian |

> **주의:** Block 7 내 Bytes 448~509 (0x1C0~0x1FD)는 Reserved (모두 0x00)이지만 CRC 계산 대상에 **포함**됨.

---

## CRC 알고리즘

- **종류:** CRC-16
- **다항식:** `0x1021`
- **초기값:** `0x0000`
- **입력/출력 반전:** 없음

### JEDEC 공식 C 코드 (JESD400-5C p.13)

```c
int Crc16(char *ptr, int count)
{
    int crc, i;
    crc = 0;
    while (--count >= 0) {
        crc = crc ^ (int)*ptr++ << 8;
        for (i = 0; i < 8; ++i)
            if (crc & 0x8000)
                crc = crc << 1 ^ 0x1021;
            else
                crc = crc << 1;
    }
    return (crc & 0xFFFF);
}

// 저장 방식:
// SPD_byte_510 = (char)(data16 & 0xFF);   ← LSB
// SPD_byte_511 = (char)(data16 >> 8);     ← MSB
```

### C# 구현 예시

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

## 구현 위치

- **파일:** `SPD_Checker\Logic\SpdChecker.cs`
- **추가 위치:** `CheckFile()` 내 Phase 3 결과 뒤
- **호출 형태:**

```csharp
results.AddRange(CheckCrc(fileName, data));
```

### CheckCrc 메서드 구조

```csharp
private static IEnumerable<CheckResult> CheckCrc(string fileName, byte[] data)
{
    // Byte 0~509 → CRC 저장: Byte 510(LSB), Byte 511(MSB)
    ushort calc   = ComputeCrc16(data, 0, 510);
    ushort stored = (ushort)(data[510] | (data[511] << 8));
    bool   pass   = calc == stored;
    yield return new CheckResult
    {
        FileName  = fileName,
        CheckItem = "CRC",
        Expected  = $"0x{calc:X4}",
        Actual    = $"0x{stored:X4}",
        Pass      = pass
    };
}
```

---

## 주의 사항

- 최소 파일 크기: **512 bytes 이상** 필요 (CRC 저장 위치 Byte 511까지)
- `CheckFile()` 상단의 크기 확인 로직과 연동 필요 (`data.Length < 512` 체크)
- CRC 체크는 Phase 1~3 이후 마지막에 실행
- Bytes 448~509는 0x00이어야 하므로 CRC 값에 영향은 없으나 계산 범위에는 포함
