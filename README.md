# Tosun Flux

토순의 파일 컨버터

버전: v0.4.1

브라우저 없이 실행되는 Windows 네이티브 WPF 파일 통합 변환기입니다.

- Windows Acrylic 배경으로 뒤쪽 창이 비치는 글래스 효과
- Per-Monitor V2 DPI 대응
- 네이티브 파일 드래그 앤 드롭
- 전체 화면 스크롤 없이 파일 목록만 스크롤
- 하단 고정 변환 버튼
- 이미지·영상 최적화와 해상도/화면비/맞춤 방식 조정
- 가로·세로 픽셀 직접 지정
- PDF 텍스트를 유지하는 내부 이미지·구조 최적화
- GitHub Releases 기반 업데이트 알림·무결성 검증·설치

## 지원 변환

- 이미지: PNG, JPG, WEBP, BMP, TIFF, GIF, PDF
- PDF: PDF → PNG/JPG, PDF 내부 이미지·구조 최적화
- 문서: DOCX → TXT/MD, TXT/MD → DOCX
- 데이터: CSV/TSV ↔ JSON/TXT
- 영상: MP4, WEBM, MOV, MKV, AVI, GIF
- 음성: MP3, WAV, FLAC, M4A, OGG

영상·음성은 FFmpeg, PDF 페이지 렌더링은 Poppler, PDF 최적화는 pypdf를 사용합니다. 패키징 시 필요한 런타임을 함께 포함합니다.

## 실행

개발 실행은 `wpf/TosunConverter.Wpf.csproj`를 빌드한 뒤 실행합니다. 변환 기능까지 포함한 배포판은 아래 패키징 명령으로 만듭니다.

## EXE 패키징

```powershell
.\package-wpf.ps1
```

생성 위치: `packaged/Tosun Flux/Tosun Flux.exe`

배포 패키지 전체도 저장소의 `packaged/Tosun Flux`에 포함하며, 대용량 파일은 Git LFS로 관리합니다.

설치파일은 `make-installer.ps1`로 생성합니다. 설치 프로그램에서 설치 폴더와 바탕화면·시작 메뉴 바로가기를 선택할 수 있으며, 기본 위치는 `C:\Program Files\Tosun Flux`입니다. Windows의 프로그램 설치 및 제거 목록과 App Paths에 게시자 `Tosun`, 버전 `0.4.1`으로 등록됩니다. 설치 완료 전 포함된 변환 백엔드 상태를 검사하며, 설치 시 관리자 권한이 필요합니다.

## 검증

```powershell
python -m unittest discover -s tests -v
```
