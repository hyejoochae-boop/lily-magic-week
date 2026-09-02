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
