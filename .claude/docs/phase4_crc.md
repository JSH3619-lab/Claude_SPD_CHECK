# Phase 4 — CRC 검증 설계

상태: **설계 완료 — 코드 미착수**

---

## CRC 블록 구조

| 블록 | 계산 범위 | CRC 저장 위치 | 바이트 순서 |
|------|-----------|--------------|-----------|
| Block 0 | Byte 0 ~ 125 | Byte 126~127 | Little-Endian |
| Block 1 | Byte 128 ~ 253 | Byte 254~255 | Little-Endian |

---

## CRC 알고리즘

- **종류:** CRC-16/CCITT
- **다항식:** `0x1021`
- **초기값:** `0x0000`
- **입력/출력 반전:** 없음

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
    // Block 0: Byte 0~125 → 저장값 Byte 126~127
    ushort calc0   = ComputeCrc16(data, 0, 126);
    ushort stored0 = (ushort)(data[126] | (data[127] << 8));
    bool   pass0   = calc0 == stored0;
    yield return new CheckResult { ... };

    // Block 1: Byte 128~253 → 저장값 Byte 254~255
    ushort calc1   = ComputeCrc16(data, 128, 126);
    ushort stored1 = (ushort)(data[254] | (data[255] << 8));
    bool   pass1   = calc1 == stored1;
    yield return new CheckResult { ... };
}
```

---

## 주의 사항

- 최소 파일 크기: 256 bytes 이상 필요 (Block 1 CRC 저장 위치 Byte 255까지)
- `CheckFile()` 상단의 크기 확인 로직과 연동 필요
- CRC 체크는 Phase 1~3 이후 마지막에 실행
