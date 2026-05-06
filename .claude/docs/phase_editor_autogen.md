# Phase Editor / Auto-Gen — 기획 + 진행 현황

> **목적:** 기존 Check 전용 프로그램에 SPD 편집(Editor) / 자동 생성(Auto-Gen) 기능 추가.

---

## 0. 진행 현황

| 단계 | 내용 | 상태 |
|------|------|------|
| E-0  | LaunchForm + 3-mode 라우팅 + 모드 전환 버튼 | ✅ 완료 |
| E-1  | SpdParser / SpdInfo 분리 + 골든 샘플 명명 규칙 | ✅ 완료 |
| E-2  | SpdEditorForm 골격 (Hex Grid + Part Info + Key Bytes) | ✅ 완료 |
| E-2.5 | Key Byte 툴팁 / 행 헤더 hex / 그룹화 + 의미 디코딩 | ✅ 완료 |
| E-3  | CRC 일괄 재계산 + 실시간 검증 ✅/❌ + 셀 색상 | ✅ 완료 |
| E-4  | Auto-Fix 통합 (구 SpdFixer) | ✅ 완료 |
| E-5  | Save / Load (.sp5 / .bin) + dirty 추적 마무리 | ✅ 완료 |
| E-6  | AutoGenForm + SpdAutoGen 로직 | ✅ 완료 |
| E-7  | 모드 전환 [Mode ▾] 드롭다운 + FAIL → Editor 점프 | ✅ 완료 |
| E-3.5 | Part Info Form 입력 (TextBox + ComboBox 9개) → byte 역계산 + 조합 검증 (Speed↔Bank) | ✅ 완료 |

---

## 1. 3-Mode 구조

| Mode | 사용자 | 용도 |
|------|--------|------|
| ✅ **Check**   | 엔지니어 | 다중 파일 일괄 검증 (기존) |
| ✏️ **Editor**  | 엔지니어 | 단일 파일 편집·CRC·Auto-Fix |
| 🤖 **Auto-Gen** | 작업자   | QR 스캔 → 골든 샘플 자동 생성 |

`LaunchForm`에서 시작 모드 선택. 각 폼 상단 `← 모드 선택` 버튼으로 전환 (E-7에서 `Mode ▾` 드롭다운으로 확장 예정).

---

## 2. Editor Mode (E-0~E-3 구현 완료)

### 2.1 레이아웃

```
┌─ 헤더: DDR5 SPD Editor                    [← 모드 선택] ┐
├─ 툴바: [Load] [New] [Save] [Save As]  [🔄 CRC 재계산]    ┤
├──────────────────────┬────────────────────────────────┤
│ Hex Grid (64×16)     │ Part Information               │
│ ■ 파랑 = Key Byte    │ 🗂 템플릿 파일명                │
│ ■ 초록 = PASS        │ Key Bytes (✅/❌ + 디코딩)      │
│ ■ 빨강 = FAIL        │                                │
├──────────────────────┴────────────────────────────────┤
│ 상태바: 파일명 * | 1024 bytes | XMP: ON/OFF            │
└──────────────────────────────────────────────────────┘
```

### 2.2 Key Bytes 그룹 + 디코더 (19개 그룹)

관련 바이트를 그룹으로 묶고 의미 디코딩:

```
✅ 002       0x12        DRAM Type       DDR5 SDRAM
✅ 014-015   0x0165      tCKAVGmin       357 ps (DDR5-5600)
❌ 1FE-1FF   0x0000      JEDEC CRC       (stored)
✅ 200-201   07/25       Module Mfr ID   RAmos
```

각 그룹은 `KeyByteGroup { Offset, Length, Name, CheckItem, Decode }` 구조.  
`Decode = (data, offset) → (Raw, Meaning)` 함수로 의미 추출.

### 2.3 실시간 검증 흐름

```
셀 편집 → RefreshDisplay()
  ├─ SpdInfo.FromBytes(_data)
  ├─ SpdChecker.CheckBytes(_data, fileName, partNo, skipFilenameChecks: true)
  ├─ CheckResult 리스트 → Dictionary<CheckItem, CheckResult>
  └─ KEY_BYTE_GROUPS 순회 → ✅/❌ 표시 + 셀 색상
```

`SpdChecker.CheckBytes(skipFilenameChecks=true)`: 파일명 기반 검사(Mfr 라우팅 / Prefix / Part No) 생략. byte 521~550 ASCII에서 PartFields 파싱 → 모든 byte 검증.

### 2.4 CRC 일괄 재계산

[🔄 CRC 재계산] 버튼 — 섹션별 정확히 계산:

| 섹션 | 범위 | 저장 | 조건 |
|------|------|------|------|
| JEDEC | 0~509 | 510~511 | 항상 |
| XMP Global | 640~701 | 702~703 | XMP 활성 |
| XMP P1 | 704~765 | 766~767 | XMP 활성 |
| XMP P2 | 768~829 | 830~831 | Byte 643 bit1=1 |

순서: JEDEC → Global → P1 → P2.

---

## 3. Auto-Gen Mode (E-6 예정)

### 3.1 동작 흐름

```
QR 입력 (Part No) → 폴더에 <PartNo>.sp5 존재?
  ├─ Yes → 정상 (기존 파일 사용)
  └─ No  → Templates/ 검색 (명명 규칙 매칭)
            ├─ 일치 → 복제 + Part No 치환 + CRC 재계산 → 저장
            └─ 불일치 → 거부 ("엔지니어 호출")
```

### 3.2 골든 샘플 명명 규칙

```
TPL_{DIMM}{Density}{Bank}{IO}{Die}{Rank}_{Speed}.sp5
```

Sourcing(RM/TM/CM/BM), DRAM Type(R), DRAM Mfr 는 제외 — 모두 Auto-Gen 시 자동 치환됨.

| Part No (입력) | 골든 샘플 |
|---|---|
| `RMRDAG58A1P-GPWRRWM7-TN` | `TPL_DAG58A1_WM.sp5` |
| `RMRDBG68B2P-GPWRRCM6-TN` | `TPL_DBG68B2_CM.sp5` |
| `TMRDAG58A1P-GPWNRWM7-TN` | `TPL_DAG58A1_WM.sp5` ← RM과 동일 템플릿 사용 |
| `CMRDAG58A1P-GPWRRWM7-TN` | `TPL_DAG58A1_WM.sp5` ← CM도 동일 템플릿 사용 |

`SpdParser.BuildTemplateFileName(PartFields)` 구현 완료.

### 3.3 폴더 구조

```
SPD_Folder/
├── Templates/                     ← 엔지니어가 직접 배치
│   ├── TPL_DAG58A1_WM.sp5
│   └── … (Sourcing/Mfr 관계없이 시스템 구성별 1개)
├── AutoGen_Log.csv                ← 자동생성 이력
└── (실제 SPD 파일들)
```

### 3.4 안전 장치

- 카테고리 9개 필드 모두 정확 일치만 허용 (추정 X)
- Templates 폴더 분리
- 자동생성 이력 로그
- 작업자용 화면은 단순 (QR 입력창 + 결과)

---

## 4. 신규/수정 파일 (E-0~E-3)

### 신규
| 파일 | 역할 |
|------|------|
| `Forms/LaunchForm.cs` | 모드 선택 + `AppMode` enum |
| `Forms/SpdEditorForm.cs` | Editor 메인 폼 |
| `Logic/SpdParser.cs` | 공유 타입(`PartFields`, `SpeedSpec`) + 파싱/CRC/명명 |
| `Models/SpdInfo.cs` | Editor 데이터 모델 |

### 수정
| 파일 | 변경 |
|------|------|
| `Program.cs` | `while` 루프 + 모드 라우팅 |
| `MainForm.cs` | 헤더에 `← 모드 선택` 버튼 + `SwitchRequested` |
| `Logic/SpdChecker.cs` | `CheckBytes` 분리 (skipFilenameChecks 옵션) |
| `Logic/SpdFixer.cs` | `SpdParser`로 호출 경로 변경 |

### E-6 시 신규 예정
- `Forms/AutoGenForm.cs`
- `Logic/SpdAutoGen.cs`
- `Logic/SpdWriter.cs` (E-5)

---

## 5. 다음 단계 가이드

### E-4 — Auto-Fix 통합 ⭐ 다음 작업

- Editor 우측 패널 또는 툴바에 `[🔧 자동 수정]` 버튼 추가
- `SpdFixer.ApplyFixes(byte[], filePath)` 호출 → byte[] 갱신
- CRC 자동 재계산 (Fix 후 마지막 단계)
- 메모리만 적용 (저장은 Save로 별도 확정)
- Check 모드의 `Fix FAILs ▼` 버튼은 **제거** (Editor로 통합)

### E-5 — Save / Load
- 현재 Save/SaveAs는 placeholder MessageBox
- `SpdFixer.SerializeToSp5` 재사용 (CSV hex)
- `.bin` 옵션 (raw binary) 추가
- `dirty` flag 처리 마무리 (제목 `*`, 닫을 때 확인)

### E-6 — Auto-Gen
- `AutoGenForm` (작업자용 단순 UI: QR 입력창 + 폴더 선택 + 결과 표시)
- `SpdAutoGen.GenerateFromTemplate(partNo, folderPath)` 로직
- `AutoGen_Log.csv` 이력 기록

### E-7 — 모드 전환 마무리
- 현재 `← 모드 선택`만 → `Mode ▾` 드롭다운으로 확장
- Check 결과 FAIL 행 더블클릭 → Editor 자동 전환 + 파일 로드 + 문제 byte 스크롤

### E-3.5 (보류) — Form-based Input
- Part Info를 읽기 Label → 입력 Form (TextBox/ComboBox)
- Speed/Density/Module/Rank 입력 시 해당 byte 자동 갱신
- `SpdFixer` 역방향 매핑 로직 재사용

