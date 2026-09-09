# Tosun Flux

토순의 파일 컨버터입니다. Windows 네이티브 WPF GUI와 로컬 변환 백엔드로 동작합니다.

현재 버전: `v1.0.8`

라이선스: [MIT License](LICENSE)

저작권 표기: © 2026 Tosun Studio. All rights reserved.

- Windows Acrylic 글래스 배경과 Per-Monitor V2 DPI 대응
- 파일 드래그 앤 드롭 및 파일별 삭제
- 이미지·영상·PDF 최적화
- 해상도(4K UHD·4K·QHD·FHD·HD·SD)·화면비·맞춤 방식·직접 픽셀 지정
- 영상 프레임 변환과 PNG/JPG 프레임 시퀀스 추출
- GitHub Releases 기반 업데이트 확인과 설치
- 단일 실행 방지 및 시스템 트레이 최소화

## 구조

```text
Content/TosunFlux          아이콘, 토순 이미지, Pretendard 폰트
Source/TosunFlux           WPF 앱
Source/TosunFluxBackend    변환 엔진과 CLI
Source/TosunFluxInstaller  Windows 설치기
Build                      Windows 패키징 스크립트와 중간 산출물
Tests                      변환 엔진 테스트
packaged                   로컬 패키징 결과, Git 제외
```

## 지원 변환

- 이미지: PNG, JPG, WEBP, BMP, TIFF, GIF, PDF
- PDF: PDF → PNG/JPG, PDF 내부 이미지·구조 최적화
- 문서: DOCX → TXT/MD, TXT/MD → DOCX
- 데이터: CSV/TSV ↔ JSON/TXT
- 영상: MP4, WEBM, MOV, MKV, AVI, GIF
- 음성: MP3, WAV, FLAC, M4A, OGG

영상·음성은 FFmpeg, PDF 페이지 렌더링은 Poppler, PDF 최적화는 pypdf를 사용합니다.

## 실행

WPF 앱은 다음 프로젝트를 빌드합니다.

```powershell
dotnet build .\Source\TosunFlux\TosunFlux.csproj
```

## Windows 패키징

패키징에는 Python, PyInstaller, FFmpeg, Poppler가 필요합니다. 경로는 소스에 하드코딩하지 않고 매개변수 또는 환경변수로 지정합니다.

```powershell
$env:TOSUN_PYTHON = 'C:\Tools\Python\python.exe'
$env:TOSUN_FFMPEG = 'C:\Tools\ffmpeg\bin\ffmpeg.exe'
$env:TOSUN_POPPLER_BIN = 'C:\Tools\poppler\Library\bin'
.\Build\Package-Windows.ps1
.\Build\Make-Installer.ps1
```

개발 패키지는 `packaged/Tosun Flux Dev`, 설치 payload는 `packaged/User Install`, 설치기는 `packaged/Installer`, 릴리즈 검증용 로컬 묶음은 `packaged/Tosun Flux`에 생성됩니다. 이 폴더들은 저장소에 커밋하지 않으며 배포 바이너리는 GitHub Release에만 올립니다.

## 테스트

```powershell
python -m unittest discover -s Tests -v
```

## 배포

소스 저장소와 설치 파일을 분리합니다.

- 소스: [Tosun Flux](https://github.com/Tosun0/Tosun-Flux)
- 설치 파일: [GitHub Releases](https://github.com/Tosun0/Tosun-Flux/releases)

설치 파일은 Windows x64용 단일 설치 프로그램이며, 게시자는 `Tosun Studio`입니다.
