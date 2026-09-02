# Lily 아이템 추가 도구

## ImgTool.cs (배경 제거 + 리사이즈)
Python 없이 PowerShell에서 C#으로 컴파일해 사용:
```powershell
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition (Get-Content .\lily_tools\ImgTool.cs -Raw -Encoding UTF8)
# 아바타 아이템: 전체 캔버스 유지, 335x600
[ImgTool]::RemoveBg($src, $dst, 20, 0.01, $false, 0, 335, 600, 0, 12)
# 가구: 내용물 bbox로 크롭(+4% 여백), 최대 500px
[ImgTool]::RemoveBg($src, $dst, 20, 0.01, $true, 4, 0, 0, 500, 12)
# 가랜드처럼 줄이 사라진 경우 별 위치로 줄 다시 그리기
[ImgTool]::DrawString($src, $dst, 3, 92, 64, 51, 14)
# 결과 확인용 컨택트시트(마젠타 배경)
[ImgTool]::Sheet($files, $dst, 4, 240, 330)
```
AI 생성 이미지의 가짜 체커보드 배경(불투명)을 테두리 색 팔레트 + 무채색 규칙으로 플러드필 제거.

## fit_simulator.html
`../fit_simulator.html` — 새 아이템 위치/크기(AV_FIT, PNG_FURNITURE w) 조정 후 "내보내기" 텍스트 복사.

```powershell
# 바깥과 연결되지 않은(둘러싸인) 체커보드 조각 제거 — 회색 사각형(밝기 185~225) 위치를 찾아 그 영역의 무채색 픽셀만 지움
[ImgTool]::RemoveNeutralPatch($src, $dst, 185, 225, 8, 175, 3, 200)
```

```powershell
# 상점 썸네일: 가장 큰 덩어리 기준으로 내용물만 잘라 160px 정사각형 가운데 배치 (잡점 무시)
[ImgTool]::ThumbSmart($src, "lily2_assets\thumbs\<id>.png", 160, 8, 90, 0.05)
# 작은 조각 중 회색(체커 잔여물)만 버리고 색 있는 조각(반짝이·잔가지)은 유지
[ImgTool]::KeepColourfulSmall = $true; [ImgTool]::RemoveBg(...)
# 빛 번짐 제거: 강한 설정 마스크를 y 기준 위쪽에만 적용해 합성 (달 조명)
[ImgTool]::CombineAlphaAbove($origFull, $maskFull, $dst, $yCut, 4, 500)
```
