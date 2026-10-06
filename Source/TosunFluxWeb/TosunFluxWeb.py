from __future__ import annotations

import os
import asyncio
import shutil
import subprocess
import sys
import tempfile
import zipfile
import time
import uuid
from contextlib import asynccontextmanager, suppress
from dataclasses import replace
from pathlib import Path
from urllib.parse import quote

from fastapi import Body, FastAPI, File, Form, HTTPException, UploadFile
from fastapi.concurrency import run_in_threadpool
from fastapi.responses import FileResponse, Response
from fastapi.staticfiles import StaticFiles
from starlette.background import BackgroundTask
from PIL import Image

PROJECT_ROOT = Path(__file__).resolve().parents[2]
BACKEND_ROOT = PROJECT_ROOT / "Source" / "TosunFluxBackend"
WEB_ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(BACKEND_ROOT))
sys.path.insert(0, str(WEB_ROOT))

from TosunFluxConverter import (  # noqa: E402
    AI_NATIVE_SCALE,
    ASPECTS,
    FIT_MODES,
    FRAME_RATES,
    MAX_OUTPUT_DIMENSION,
    OPTIMIZATION_MODES,
    RESOLUTIONS,
    UPSCALE_FACTORS,
    ConversionError,
    ConversionOptions,
    ai_upscaler_tool,
    common_targets,
    conversion_profile,
    convert_file,
    file_kind,
    supported_targets,
)
from TosunFluxWebVideo import VideoUpscale, MAX_FRAME_BYTES, MAX_VIDEO_INPUT_PIXELS, MAX_VIDEO_OUTPUT_PIXELS, MAX_VIDEO_SECONDS, VIDEO_IDLE_SECONDS

VERSION = "1.2.7"
MAX_FILES = 20
MAX_FILE_BYTES = int(os.environ.get("TOSUN_WEB_MAX_FILE_MB", "512")) * 1024 * 1024
MAX_REQUEST_BYTES = int(os.environ.get("TOSUN_WEB_MAX_REQUEST_MB", "1024")) * 1024 * 1024
CHUNK_SIZE = 1024 * 1024
video_jobs: dict[str, VideoUpscale | None] = {}


def _expire_videos() -> None:
    for token, job in list(video_jobs.items()):
        if job is not None and time.monotonic() - job.touched > VIDEO_IDLE_SECONDS and job.lock.acquire(blocking=False):
            try:
                if video_jobs.pop(token, None) is job:
                    job.close()
            finally:
                job.lock.release()


@asynccontextmanager
async def lifespan(app):
    async def cleanup():
        while True:
            await asyncio.sleep(30)
            await run_in_threadpool(_expire_videos)
    task = asyncio.create_task(cleanup())
    try:
        yield
    finally:
        task.cancel()
        with suppress(asyncio.CancelledError):
            await task
        for job in list(video_jobs.values()):
            if job is not None:
                job.close()
        video_jobs.clear()

app = FastAPI(title="Tosun Flux Web", version=VERSION, docs_url=None, redoc_url=None, lifespan=lifespan)
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
        "webgpu_video": {"max_input_pixels": MAX_VIDEO_INPUT_PIXELS, "max_output_pixels": MAX_VIDEO_OUTPUT_PIXELS, "max_seconds": MAX_VIDEO_SECONDS, "max_batch_bytes": MAX_REQUEST_BYTES},
        "profile": conversion_profile(),
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
    webgpu_scale: float | None = Form(None),
) -> FileResponse:
    if not 1 <= len(files) <= MAX_FILES:
        raise HTTPException(400, f"파일은 한 번에 1~{MAX_FILES}개까지 변환할 수 있습니다.")

    if webgpu_scale is not None:
        if webgpu_scale not in UPSCALE_FACTORS or webgpu_scale == 1:
            raise HTTPException(400, "지원하지 않는 WebGPU 업스케일 배율입니다.")
        if target not in supported_targets(Path("image.png")):
            raise HTTPException(400, "WebGPU 결과는 이미지 또는 PDF로 저장할 수 있습니다.")
        if scale_factor != 1 or resolution != "source" or aspect != "source" or width is not None or fps != "source":
            raise HTTPException(400, "WebGPU 결과에 업스케일 또는 해상도 설정을 중복 적용할 수 없습니다.")
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
            total_bytes = await _save_upload(upload, destination, total_bytes)
            saved.append(destination)

        if target not in common_targets(saved):
            raise HTTPException(400, f"선택한 파일을 {target} 형식으로 함께 변환할 수 없습니다.")

        # ponytail: keep jobs synchronous until concurrent users or proxy timeouts require a queue.
        converted = await run_in_threadpool(_convert_all, saved, outputs, target, options, webgpu_scale)
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


async def _save_upload(upload: UploadFile, destination: Path, total_bytes: int = 0) -> int:
    size = 0
    try:
        with destination.open("wb") as handle:
            while chunk := await upload.read(CHUNK_SIZE):
                size += len(chunk)
                total_bytes += len(chunk)
                if size > MAX_FILE_BYTES or total_bytes > MAX_REQUEST_BYTES:
                    raise HTTPException(413, f"업로드 제한: 파일당 {MAX_FILE_BYTES // CHUNK_SIZE}MB, 전체 {MAX_REQUEST_BYTES // CHUNK_SIZE}MB입니다.")
                handle.write(chunk)
        return total_bytes
    finally:
        await upload.close()


@app.post("/api/webgpu/video")
async def start_video(file: UploadFile = File(...), target: str = Form(...), scale: int = Form(...), optimize: str = Form("source"), fps: str = Form("source")):
    name = _safe_name(file.filename or "video.mp4")
    if file_kind(Path(name)) != "video" or target not in supported_targets(Path(name)) or scale not in (2, 4):
        raise HTTPException(400, "지원하는 영상 형식과 2x 또는 4x 배율을 선택하세요.")
    options = _options(optimize, "source", "source", "stretch", fps, 1, None, None)
    # ponytail: one streaming video fits the free host; increase only with measured RAM headroom.
    if video_jobs:
        raise HTTPException(503, "다른 영상 업스케일을 처리 중입니다. 잠시 후 다시 시도해 주세요.")
    token = uuid.uuid4().hex
    root = Path(tempfile.mkdtemp(prefix="tosunflux-web-video-"))
    video_jobs[token] = None  # reserve the slot before asynchronous upload
    source = root / name
    try:
        await _save_upload(file, source)
        job = await run_in_threadpool(VideoUpscale, root, source, target, options, scale, MAX_REQUEST_BYTES)
        video_jobs[token] = job
        return {"id": token, "frames": job.estimated_frames, "fps": job.fps}
    except Exception as error:
        video_jobs.pop(token, None)
        shutil.rmtree(root, ignore_errors=True)
        if isinstance(error, ConversionError):
            raise HTTPException(400, str(error)) from error
        raise


def _video_step(token: str, method: str, *args):
    job = video_jobs.get(token)
    if job is None:
        raise HTTPException(404, "영상 작업이 만료되었습니다. 다시 변환해 주세요.")
    with job.lock:
        job.touched = time.monotonic()
        try:
            return getattr(job, method)(*args)
        except (ConversionError, subprocess.TimeoutExpired) as error:
            video_jobs.pop(token, None)
            job.close()
            raise HTTPException(400, str(error)) from error


@app.get("/api/webgpu/video/{token}/frames/{index}")
async def video_frame(token: str, index: int):
    frame = await run_in_threadpool(_video_step, token, "next_frame", index)
    return Response(frame, media_type="image/png", headers={"Cache-Control": "no-store"}) if frame is not None else Response(status_code=204)


@app.put("/api/webgpu/video/{token}/frames/{index}")
async def video_result(token: str, index: int, file: UploadFile = File(...)):
    try:
        payload = await file.read(MAX_FRAME_BYTES + 1)
    finally:
        await file.close()
    if len(payload) > MAX_FRAME_BYTES:
        await cancel_video(token)
        raise HTTPException(413, "확대 프레임 용량 제한을 넘었습니다.")
    await run_in_threadpool(_video_step, token, "put_frame", index, payload)
    return {"ok": True}


@app.post("/api/webgpu/video/{token}/finish")
async def finish_video(token: str):
    output = await run_in_threadpool(_video_step, token, "finish")
    job = video_jobs.pop(token)
    return FileResponse(output, filename=output.name, background=BackgroundTask(job.close))


@app.delete("/api/webgpu/video/{token}")
async def cancel_video(token: str):
    job = video_jobs.pop(token, None)
    if job is not None:
        def close():
            with job.lock:
                job.close()
        await run_in_threadpool(close)
    return {"ok": True}


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
    if optimize not in OPTIMIZATION_MODES:
        raise HTTPException(400, "지원하지 않는 최적화 설정입니다.")
    if resolution not in {"source", *RESOLUTIONS}:
        raise HTTPException(400, "지원하지 않는 해상도 설정입니다.")
    if aspect not in {"source", *ASPECTS}:
        raise HTTPException(400, "지원하지 않는 화면비입니다.")
    if fit not in FIT_MODES:
        raise HTTPException(400, "지원하지 않는 화면 맞춤 설정입니다.")
    if fps not in FRAME_RATES:
        raise HTTPException(400, "지원하지 않는 프레임 설정입니다.")
    if scale_factor not in UPSCALE_FACTORS:
        raise HTTPException(400, "업스케일은 원본, 2x, 4x만 지원합니다.")
    if (width is None) != (height is None):
        raise HTTPException(400, "직접 해상도는 가로와 세로를 함께 입력하세요.")
    if width is not None and not (2 <= width <= MAX_OUTPUT_DIMENSION and 2 <= height <= MAX_OUTPUT_DIMENSION):
        raise HTTPException(400, f"직접 해상도는 가로·세로 2~{MAX_OUTPUT_DIMENSION} 범위여야 합니다.")
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
    webgpu_scale: float | None = None,
) -> list[Path]:
    outputs: list[Path] = []
    for source in sources:
        source_options = options
        if webgpu_scale is not None:
            try:
                with Image.open(source) as image:
                    if image.format != "PNG" or any(edge % AI_NATIVE_SCALE for edge in image.size):
                        raise ConversionError("WebGPU 중간 결과는 네이티브 배율의 PNG여야 합니다.")
                    width, height = (int(edge / AI_NATIVE_SCALE * webgpu_scale) for edge in image.size)
            except (OSError, ValueError) as error:
                raise ConversionError("WebGPU 중간 이미지를 읽지 못했습니다.") from error
            if not (2 <= width <= MAX_OUTPUT_DIMENSION and 2 <= height <= MAX_OUTPUT_DIMENSION):
                raise ConversionError(f"업스케일 결과는 가로·세로 2~{MAX_OUTPUT_DIMENSION} 범위여야 합니다.")
            # The same Lanczos geometry and image writer used by the desktop app.
            source_options = replace(options, width=width, height=height, fit="stretch")
        outputs.extend(convert_file(source, output_dir, target, source_options).outputs)
    return outputs
