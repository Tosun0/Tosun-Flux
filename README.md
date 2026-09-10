# Tosun Flux

토순의 파일 컨버터입니다. Windows 네이티브 WPF GUI와 로컬 변환 백엔드로 동작합니다.

현재 버전: `v1.2.1`

라이선스: [MIT License](LICENSE)

저작권 표기: © 2026 Tosun Studio. All rights reserved.

- Windows Acrylic 글래스 배경과 Per-Monitor V2 DPI 대응
- macOS Avalonia GUI와 `.app`/`.dmg` 패키징 경로
- 파일 드래그 앤 드롭 및 파일별 삭제
- 이미지·영상·PDF 최적화
- 해상도(4K UHD·4K·QHD·FHD·HD·SD)·화면비·맞춤 방식·직접 픽셀 지정
- 이미지·영상 2x/4x AI 업스케일(Real-ESRGAN-ncnn-vulkan 기반)
- 영상 프레임 변환과 PNG/JPG 프레임 시퀀스 추출
- 파일별 해상도·프레임을 반영한 예상 용량 범위와 변환 후 실제 용량 표시
- GitHub Releases 기반 업데이트 확인과 설치
- 단일 실행 방지 및 시스템 트레이 최소화

## 구조

```text
Content/TosunFlux          아이콘, 토순 이미지, Pretendard 폰트
Source/TosunFlux           WPF 앱
Source/TosunFluxMac        macOS용 Avalonia GUI
Source/TosunFluxBackend    변환 엔진과 CLI
Source/TosunFluxInstaller  Windows 설치기
Build                      Windows 패키징 스크립트와 중간 산출물
Tests                      변환 엔진 테스트
packaged                   로컬 패키징 결과, Git 제외
```

## 지원 변환

- 이미지: PNG, JPG, WEBP, BMP, TIFF, GIF, PDF
- PDF: PDF → PNG/JPG, PDF 내부 이미지·구조 최적화
- 데이터: CSV/TSV ↔ JSON/TXT
- 영상: MP4, WEBM, MOV, MKV, AVI, GIF
- 음성: MP3, WAV, FLAC, M4A, OGG

영상·음성은 FFmpeg, AI 업스케일은 Real-ESRGAN-ncnn-vulkan, PDF 페이지 렌더링은 Poppler, PDF 최적화는 pypdf를 사용합니다.

## 실행

WPF 앱은 다음 프로젝트를 빌드합니다.

```powershell
dotnet build .\Source\TosunFlux\TosunFlux.csproj
```

## Windows 패키징

패키징에는 Python, PyInstaller, FFmpeg, Poppler, Real-ESRGAN portable 패키지가 필요합니다. Real-ESRGAN 실행 파일과 `models` 폴더는 공식 릴리스에서 내려받습니다.

```powershell
$env:TOSUN_PYTHON = 'C:\Tools\Python\python.exe'
$env:TOSUN_FFMPEG = 'C:\Tools\ffmpeg\bin\ffmpeg.exe'
$env:TOSUN_POPPLER_BIN = 'C:\Tools\poppler\Library\bin'
$env:TOSUN_REALESRGAN_DIR = 'C:\Tools\realesrgan-ncnn-vulkan-20220424-windows'
.\Build\Package-Windows.ps1
.\Build\Make-Installer.ps1
```

공식 Windows 패키지는 다음 명령으로 받을 수 있습니다.

```powershell
.\Build\Fetch-RealEsrgan.ps1
```

앱의 `2x AI 업스케일`과 `4x AI 업스케일`은 단순 해상도 확대가 아니라 Real-ESRGAN 신경망 추론을 사용합니다. GPU와 Vulkan 드라이버가 필요하며, 처리 속도는 입력 해상도·GPU·모델에 따라 달라집니다.

4x는 VRAM 사용량을 줄이기 위해 입력 타일 크기 128로 처리합니다.

패키징 중간 결과는 `Build/Intermediate/TosunFluxPackage`, 설치 payload는 `packaged/User Install`, 설치기는 `packaged/Installer`에 생성됩니다. 이 폴더들은 저장소에 커밋하지 않으며 배포 바이너리는 GitHub Release에만 올립니다.

## macOS 패키징

macOS GUI는 기존 변환 백엔드를 그대로 사용하며 Apple Silicon과 Intel용 앱 번들을 각각 만들 수 있습니다. macOS에서 Python, PyInstaller, FFmpeg, Poppler, .NET 8 SDK를 준비한 뒤 실행합니다.

```bash
chmod +x ./Build/Package-Mac.sh
./Build/Package-Mac.sh
```

로컬 실행은 현재 Mac의 아키텍처에 맞는 앱을 만들고, GitHub Actions의 `Build macOS packages` 워크플로는 두 아키텍처를 내부적으로 빌드한 뒤 `Tosun Flux-universal.dmg` 하나로 합칩니다. 사용자에게는 universal DMG만 전달하면 됩니다. 현재 Windows 작업 환경에서는 macOS 앱 실행·서명·공증까지 직접 검증할 수 없으며, 배포 전 Apple Developer 서명과 공증을 별도로 적용해야 합니다.

## 테스트

```powershell
python -m unittest discover -s Tests -v
```

## 배포

소스 저장소와 설치 파일을 분리합니다.

- 소스: [Tosun Flux](https://github.com/Tosun0/Tosun-Flux)
- 설치 파일: [GitHub Releases](https://github.com/Tosun0/Tosun-Flux/releases)

설치 파일은 Windows x64용 단일 설치 프로그램이며, 게시자는 `Tosun Studio`입니다.
