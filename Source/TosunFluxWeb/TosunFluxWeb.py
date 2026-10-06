from __future__ import annotations

import os
import shutil
import sys
import tempfile
import zipfile
from pathlib import Path
from urllib.parse import quote

from fastapi import Body, FastAPI, File, Form, HTTPException, UploadFile
from fastapi.concurrency import run_in_threadpool
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from starlette.background import BackgroundTask

PROJECT_ROOT = Path(__file__).resolve().parents[2]
BACKEND_ROOT = PROJECT_ROOT / "Source" / "TosunFluxBackend"
WEB_ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(BACKEND_ROOT))

from TosunFluxConverter import (  # noqa: E402
    RESOLUTIONS,
    ConversionError,
    ConversionOptions,
    ai_upscaler_tool,
    common_targets,
    convert_file,
)

VERSION = "1.2.6"
MAX_FILES = 20
MAX_FILE_BYTES = int(os.environ.get("TOSUN_WEB_MAX_FILE_MB", "512")) * 1024 * 1024
MAX_REQUEST_BYTES = int(os.environ.get("TOSUN_WEB_MAX_REQUEST_MB", "1024")) * 1024 * 1024
CHUNK_SIZE = 1024 * 1024

app = FastAPI(title="Tosun Flux Web", version=VERSION, docs_url=None, redoc_url=None)
app.mount("/static", StaticFiles(directory=WEB_ROOT / "static"), name="static")
app.mount("/assets", StaticFiles(directory=PROJECT_ROOT / "Content" / "TosunFlux"), name="assets")


@app.middleware("http")
async def secure_headers(request, call_next):
    response = await call_next(request)
    response.headers["X-Content-Type-Options"] = "nosniff"
    response.headers["Referrer-Policy"] = "no-referrer"
    response.headers["Content-Security-Policy"] = (
        "default-src 'self'; img-src 'self' data: blob:; style-src 'self'; "
        "script-src 'self' 'wasm-unsafe-eval' https://cdn.jsdelivr.net; "
        "connect-src 'self' https://cdn.jsdelivr.net https://huggingface.co "
        "https://*.huggingface.co https://*.hf.co https://*.xethub.hf.co; "
        "object-src 'none'; base-uri 'none'"
    )
    return response


@app.get("/", include_in_schema=False)
def index() -> FileResponse:
    return FileResponse(WEB_ROOT / "static" / "index.html")


@app.get("/api/health")
def health() -> dict[str, object]:
    return {
        "status": "ok",
        "product": "Tosun Flux Web",
        "version": VERSION,
        "ai_upscale": ai_upscaler_tool() is not None,
        "max_files": MAX_FILES,
        "max_file_mb": MAX_FILE_BYTES // (1024 * 1024),
    }


@app.post("/api/targets")
def targets(filenames: list[str] = Body(..., min_length=1, max_length=MAX_FILES)) -> dict[str, object]:
    paths = [Path(_safe_name(name)) for name in filenames]
    return {"targets": list(common_targets(paths))}


@app.post("/api/convert")
async def convert(
    files: list[UploadFile] = File(...),
    target: str = Form(...),
    optimize: str = Form("source"),
    resolution: str = Form("source"),
    aspect: str = Form("source"),
    fit: str = Form("fit"),
    fps: str = Form("source"),
    scale_factor: float = Form(1.0),
    width: int | None = Form(None),
    height: int | None = Form(None),
) -> FileResponse:
    if not 1 <= len(files) <= MAX_FILES:
        raise HTTPException(400, f"파일은 한 번에 1~{MAX_FILES}개까지 변환할 수 있습니다.")

    options = _options(optimize, resolution, aspect, fit, fps, scale_factor, width, height)
    root = Path(tempfile.mkdtemp(prefix="tosunflux-web-"))
    inputs = root / "inputs"
    outputs = root / "outputs"
    inputs.mkdir()
    outputs.mkdir()
    saved: list[Path] = []
    total_bytes = 0

    try:
        for index, upload in enumerate(files, start=1):
            name = _safe_name(upload.filename or f"file-{index}")
            destination = _unique_path(inputs, name)
            size = 0
            with destination.open("wb") as handle:
                while chunk := await upload.read(CHUNK_SIZE):
                    size += len(chunk)
                    total_bytes += len(chunk)
                    if size > MAX_FILE_BYTES:
                        raise HTTPException(413, f"{name}: 파일당 {MAX_FILE_BYTES // (1024 * 1024)}MB 제한을 넘었습니다.")
                    if total_bytes > MAX_REQUEST_BYTES:
                        raise HTTPException(413, f"전체 업로드 {MAX_REQUEST_BYTES // (1024 * 1024)}MB 제한을 넘었습니다.")
                    handle.write(chunk)
            await upload.close()
            saved.append(destination)

        if target not in common_targets(saved):
            raise HTTPException(400, f"선택한 파일을 {target} 형식으로 함께 변환할 수 없습니다.")

        # ponytail: keep jobs synchronous until concurrent users or proxy timeouts require a queue.
        converted = await run_in_threadpool(_convert_all, saved, outputs, target, options)
        if len(converted) == 1:
            output = converted[0]
            return FileResponse(
                output,
                filename=output.name,
                background=BackgroundTask(shutil.rmtree, root, True),
            )

        archive = root / "Tosun-Flux-Converted.zip"
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as zipped:
            for output in converted:
                zipped.write(output, output.name)
        return FileResponse(
            archive,
            media_type="application/zip",
            headers={"Content-Disposition": f"attachment; filename*=UTF-8''{quote(archive.name)}"},
            background=BackgroundTask(shutil.rmtree, root, True),
        )
    except HTTPException:
        shutil.rmtree(root, ignore_errors=True)
        raise
    except ConversionError as error:
        shutil.rmtree(root, ignore_errors=True)
        raise HTTPException(400, str(error)) from error
    except Exception:
        shutil.rmtree(root, ignore_errors=True)
        raise


def _safe_name(name: str) -> str:
    safe = Path(name.replace("\\", "/")).name.strip()
    if not safe or safe in {".", ".."}:
        raise HTTPException(400, "올바른 파일 이름이 필요합니다.")
    return safe[:180]


def _unique_path(directory: Path, name: str) -> Path:
    destination = directory / name
    for number in range(2, MAX_FILES + 2):
        if not destination.exists():
            return destination
        destination = directory / f"{Path(name).stem} ({number}){Path(name).suffix}"
    raise HTTPException(400, "같은 이름의 파일이 너무 많습니다.")


def _options(
    optimize: str,
    resolution: str,
    aspect: str,
    fit: str,
    fps: str,
    scale_factor: float,
    width: int | None,
    height: int | None,
) -> ConversionOptions:
    if optimize not in {"source", "quality", "balanced", "small"}:
        raise HTTPException(400, "지원하지 않는 최적화 설정입니다.")
    if resolution not in {"source", *RESOLUTIONS}:
        raise HTTPException(400, "지원하지 않는 해상도 설정입니다.")
    if aspect not in {"source", "16:9", "9:16", "1:1", "4:3", "3:4"}:
        raise HTTPException(400, "지원하지 않는 화면비입니다.")
    if fit not in {"fit", "fill", "stretch"}:
        raise HTTPException(400, "지원하지 않는 화면 맞춤 설정입니다.")
    if fps not in {"source", "23.976", "24", "25", "29.97", "30", "50", "59.94", "60"}:
        raise HTTPException(400, "지원하지 않는 프레임 설정입니다.")
    if scale_factor not in {1.0, 2.0, 4.0}:
        raise HTTPException(400, "업스케일은 원본, 2x, 4x만 지원합니다.")
    if (width is None) != (height is None):
        raise HTTPException(400, "직접 해상도는 가로와 세로를 함께 입력하세요.")
    if width is not None and not (2 <= width <= 16384 and 2 <= height <= 16384):
        raise HTTPException(400, "직접 해상도는 가로·세로 2~16384 범위여야 합니다.")
    if scale_factor != 1.0 and ai_upscaler_tool() is None:
        raise HTTPException(503, "이 서버에는 AI 업스케일 엔진이 설치되어 있지 않습니다.")
    return ConversionOptions(
        optimize,
        resolution,
        aspect,
        fit,
        width,
        height,
        fps,
        scale_factor,
        "ai" if scale_factor != 1.0 else "resize",
    )


def _convert_all(
    sources: list[Path],
    output_dir: Path,
    target: str,
    options: ConversionOptions,
) -> list[Path]:
    outputs: list[Path] = []
    for source in sources:
        outputs.extend(convert_file(source, output_dir, target, options).outputs)
    return outputs
