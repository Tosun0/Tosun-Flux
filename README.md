# Tosun Flux

토순의 파일 컨버터입니다. Windows 네이티브 WPF GUI와 로컬 변환 백엔드로 동작합니다.

현재 버전: `v1.2.6`

라이선스: [MIT License](LICENSE)

저작권 표기: © 2026 Tosun Studio. All rights reserved.

- Windows Acrylic 글래스 배경과 Per-Monitor V2 DPI 대응
- macOS Avalonia GUI와 `.app`/`.dmg` 패키징 경로
- Mac·Windows 브라우저에서 사용하는 반응형 Web UI와 변환 API
- 파일 드래그 앤 드롭 및 파일별 삭제
- 이미지·영상·PDF 최적화
- 해상도(4K UHD·4K·QHD·FHD·HD·SD)·화면비·맞춤 방식·직접 픽셀 지정
- 이미지·영상 2x/4x AI 업스케일(네이티브 Real-ESRGAN, 웹 ONNX WebGPU Real-ESRGAN)
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

macOS GUI는 기존 변환 백엔드를 그대로 사용하며 Apple Silicon과 Intel용 앱 번들을 각각 만들 수 있습니다. macOS에서 Python, PyInstaller, FFmpeg, Poppler, Real-ESRGAN portable 패키지, .NET 8 SDK를 준비한 뒤 실행합니다.

```bash
chmod +x ./Build/Package-Mac.sh
export TOSUN_REALESRGAN_DIR="$HOME/Tools/realesrgan-ncnn-vulkan-20220424-macos"
./Build/Package-Mac.sh
```

Real-ESRGAN macOS portable 패키지는 공식 릴리즈의 `realesrgan-ncnn-vulkan-20220424-macos.zip`을 내려받아 압축 해제합니다. `TOSUN_REALESRGAN_DIR`는 실행 파일과 `models` 폴더를 함께 포함한 디렉터리여야 합니다.

로컬 실행은 현재 Mac의 아키텍처에 맞는 앱을 만들고, GitHub Actions의 `Build macOS packages` 워크플로는 두 아키텍처를 내부적으로 빌드한 뒤 `Tosun Flux-universal.dmg` 하나로 합칩니다. 사용자에게는 universal DMG만 전달하면 됩니다. 현재 Windows 작업 환경에서는 macOS 앱 실행·서명·공증까지 직접 검증할 수 없으며, 배포 전 Apple Developer 서명과 공증을 별도로 적용해야 합니다.

## 웹 실행과 배포

Windows와 macOS 앱도 같은 파일별 큐를 사용합니다. 여러 파일을 2x/4x AI 업스케일할 수 있고, 처리 중 파일 추가·대기 항목 삭제·실패 항목 재등록을 지원합니다. 앱의 두 UI는 `Source/TosunFluxShared/ConversionQueue.cs`의 실행 코드를 공유합니다. 앱 백엔드는 완료 이벤트만 제공하므로 처리 중에는 불확정 진행 막대, 완료 후에는 100%를 표시합니다. 웹에서는 프레임·타일 단위 진행률도 표시합니다.

웹 UI는 기존 `TosunFluxConverter.py`의 변환 프로파일을 `/api/health`에서 받아 해상도·화면비·배율·출력 형식·프레임·크기 제한을 구성합니다. 앱 엔진의 공통 기준을 바꾸고 웹을 재배포하면 웹 설정도 함께 반영됩니다. WPF 화면 배치·설치기 같은 플랫폼 전용 코드는 공유하지 않습니다.

Chrome·Edge의 이미지 2x/4x 확대 연산은 ONNX Runtime WebGPU와 Real-ESRGAN x4plus 모델로 사용자 GPU에서 처리합니다. 네이티브 4x PNG 결과를 서버에 전송한 뒤, 앱과 같은 Python 엔진이 2x Lanczos 축소·JPG 품질·WEBP 무손실·PNG 압축·파일명·ZIP 출력을 처리합니다. 따라서 WebGPU 사용 시에도 최종 저장에는 서버 연결과 이미지 전송이 필요하며, 서버의 업로드 제한이 적용됩니다. 브라우저에서 지원하지 않는 입력 디코더나 모델은 로컬 WebGPU로 처리할 수 없고, 서버 AI 엔진이 설치된 경우에만 서버 업스케일 옵션을 사용할 수 있습니다.

영상 2x/4x WebGPU 업스케일은 앱의 영상 모델인 `realesr-animevideov3`의 [ONNX 변환본](https://huggingface.co/skillsafe-ai/realesr-animevideov3)을 사용합니다. 원본 영상을 서버에 업로드하면 FFmpeg가 지정 FPS로 프레임을 스트리밍하고, 브라우저는 한 프레임씩 GPU로 확대해 서버에 돌려보냅니다. 서버는 앱과 같은 인코딩·오디오 결합 코드를 사용해 MP4/WEBM/MOV/MKV/AVI/GIF 또는 PNG/JPG Sequence ZIP으로 저장합니다. 모델 가중치가 같아도 ONNX·NCNN의 수치 정밀도 차이로 픽셀이 완전히 일치하는 것은 아닙니다.

이미지·영상은 파일별 큐로 순차 처리합니다. 큐에 대기·처리 중·완료·실패와 파일별 진행률·프레임 상황을 표시하고, 처리 중에도 새 파일 추가와 대기 항목 삭제가 가능합니다. 한 파일이 실패해도 다음 항목을 계속 처리하며, 실패 항목은 `다시 대기`로 큐 끝에 재등록할 수 있습니다. 실행 중 설정은 시작 시 선택한 값으로 고정됩니다. 결과 다운로드는 각 완료 행에 남으며, 자동 다운로드가 막혀도 직접 누를 수 있습니다. 결과 보관 용량을 넘으면 완료 결과를 다운로드하고 큐에서 삭제한 뒤 나머지를 재등록하세요.

무료 웹 서버 보호를 위해 영상 업스케일은 입력 2,097,152픽셀(FHD급), 출력 8,388,608픽셀(4K UHD급), 길이 10분 이하이며 서버 전체에서 한 번에 한 영상만 처리합니다. 업로드·배치 결과에는 기존 용량 제한을 적용합니다. 완료·실패·페이지 이탈 시 임시 영상과 프로세스를 정리하며, 연결이 끊긴 작업은 20분 유휴 후 삭제합니다. 일반 영상 변환에는 이 업스케일 전용 제한을 적용하지 않습니다. WebGPU 지원 브라우저와 GPU 드라이버가 필요합니다.

개발 환경에서는 다음 명령으로 실행합니다.

```powershell
python -m pip install -r .\Source\TosunFluxWeb\requirements-dev.txt
python -m uvicorn Source.TosunFluxWeb.TosunFluxWeb:app --host 127.0.0.1 --port 8080
```

브라우저에서 `http://127.0.0.1:8080`을 엽니다. 배포용 컨테이너는 저장소 루트에서 빌드합니다.

```powershell
docker build -f .\Build\Dockerfile.Web -t tosun-flux-web .
docker run --rm -p 8080:8080 tosun-flux-web
```

`main`에 웹 관련 변경을 푸시하면 GitHub Actions가 `ghcr.io/tosun0/tosun-flux-web:latest` 이미지를 자동으로 게시합니다. 이 이미지를 컨테이너 호스팅에 연결하면 Windows와 macOS 모두 설치 없이 같은 웹 주소를 사용할 수 있습니다.

[Render에서 배포](https://render.com/deploy?repo=https://github.com/Tosun0/Tosun-Flux)를 누르면 저장소의 `render.yaml`로 웹 서비스를 생성할 수 있습니다.

기본 컨테이너는 FFmpeg와 Poppler를 포함합니다. 웹 이미지·영상 업스케일은 브라우저 WebGPU에서 동작하므로 서버 GPU가 필요하지 않습니다. GIF 입력 또는 브라우저 미지원 이미지의 서버 업스케일에는 Vulkan GPU와 Real-ESRGAN 실행 파일·`models` 폴더·`TOSUN_REALESRGAN_BIN` 설정이 필요합니다. 업로드 제한은 `TOSUN_WEB_MAX_FILE_MB`와 `TOSUN_WEB_MAX_REQUEST_MB`로 조정합니다.

웹 모델은 공식 Real-ESRGAN x4plus 가중치를 재현 가능하게 변환한 [SkillSafe ONNX 모델](https://huggingface.co/skillsafe-ai/realesrgan-x4plus)을 사용하며 원본과 동일한 BSD 3-Clause 라이선스를 따릅니다.

## 테스트

```powershell
python -m unittest discover -s Tests -v
node Tests/test_web_queue.cjs
dotnet run --project Tests/ConversionQueueSmoke.csproj -- <python> <backend.py> <input-dir>
```

## 배포

소스 저장소와 설치 파일을 분리합니다.

- 소스: [Tosun Flux](https://github.com/Tosun0/Tosun-Flux)
- 설치 파일: [GitHub Releases](https://github.com/Tosun0/Tosun-Flux/releases)

설치 파일은 Windows x64용 단일 설치 프로그램이며, 게시자는 `Tosun Studio`입니다.
