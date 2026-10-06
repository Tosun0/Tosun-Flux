"""One-frame-at-a-time bridge between FFmpeg and browser WebGPU inference."""
from __future__ import annotations

import io
import math
import shutil
import subprocess
import tempfile
import threading
import time
import zipfile
from dataclasses import replace
from pathlib import Path

from PIL import Image

from TosunFluxConverter import (
    AI_NATIVE_SCALE, ConversionError, ConversionOptions, _apply_image_geometry,
    _write_image, bundled_tool, source_metadata, video_frame_encoder_args,
)

MAX_VIDEO_INPUT_PIXELS = 2_097_152
MAX_VIDEO_OUTPUT_PIXELS = 8_388_608
MAX_VIDEO_SECONDS = 600
MAX_FRAME_BYTES = 64 * 1024 * 1024
VIDEO_IDLE_SECONDS = 1200


class VideoUpscale:
    def __init__(self, root: Path, source: Path, target: str, options: ConversionOptions, scale: int, byte_limit: int):
        self.root, self.source, self.target, self.options, self.scale = root, source, target, options, scale
        self.byte_limit = byte_limit
        self.lock = threading.Lock()
        self.touched = time.monotonic()
        self.index = 0
        self.pending: bytes | None = None
        self.size: tuple[int, int] | None = None
        self.eof = False
        self.decoder = self.encoder = self.archive = None
        metadata = source_metadata(source)
        duration = float(metadata.get("duration") or 0)
        self.fps = options.fps if options.fps != "source" else str(metadata.get("fps") or "30")
        if not math.isfinite(duration) or not 0 < duration <= MAX_VIDEO_SECONDS:
            raise ConversionError(f"웹 영상 업스케일은 길이를 확인할 수 있는 {MAX_VIDEO_SECONDS // 60}분 이하 영상만 지원합니다.")
        self.estimated_frames = max(1, round(duration * float(self.fps)))
        tool = bundled_tool("ffmpeg", "ffmpeg")
        if tool is None:
            raise ConversionError("FFmpeg를 찾을 수 없습니다.")
        self.error_log = tempfile.TemporaryFile()
        outputs = root / "outputs"
        outputs.mkdir()
        self.output = outputs / (source.stem + (".zip" if target.endswith("-sequence") else "." + target))
        # ponytail: pipes bound frame memory; use a worker queue only when multi-worker hosting is needed.
        args = [str(tool), "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe", "-threads", "1", "-i", str(source),
                "-an", "-vf", f"fps={self.fps}", "-threads", "1", "-f", "image2pipe", "-vcodec", "png", "pipe:1"]
        self.decoder = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=self.error_log)

    def _error(self, message: str) -> ConversionError:
        self.error_log.seek(0)
        detail = self.error_log.read(4096).decode("utf-8", errors="replace").strip()
        return ConversionError(message + (": " + detail if detail else ""))

    def next_frame(self, index: int) -> bytes | None:
        if index != self.index:
            raise ConversionError("프레임은 순서대로 처리해야 합니다.")
        if self.pending is not None or self.eof:
            return self.pending
        stream = self.decoder.stdout
        signature = stream.read(8)
        if not signature:
            if self.decoder.wait(timeout=30):
                raise self._error("영상 프레임을 읽지 못했습니다")
            self.eof = True
            return None
        if signature != b"\x89PNG\r\n\x1a\n":
            raise ConversionError("영상 프레임 데이터가 올바르지 않습니다.")
        frame = bytearray(signature)
        while True:
            header = stream.read(8)
            if len(header) != 8:
                raise self._error("영상 프레임이 잘렸습니다")
            size = int.from_bytes(header[:4], "big") + 4
            if len(frame) + size + 8 > MAX_FRAME_BYTES:
                raise ConversionError("영상 프레임 크기 제한을 넘었습니다.")
            payload = stream.read(size)
            if len(payload) != size:
                raise self._error("영상 프레임이 잘렸습니다")
            frame.extend(header + payload)
            if header[4:] == b"IEND":
                break
        with Image.open(io.BytesIO(frame)) as image:
            width, height = image.size
            if width * height > MAX_VIDEO_INPUT_PIXELS or width * height * self.scale ** 2 > MAX_VIDEO_OUTPUT_PIXELS:
                raise ConversionError("웹 영상 업스케일은 입력 약 2MP(FHD), 출력 약 8MP(4K UHD)까지 지원합니다.")
            if self.size is not None and image.size != self.size:
                raise ConversionError("도중에 해상도가 바뀌는 영상은 지원하지 않습니다.")
            self.size = image.size
        self.pending = bytes(frame)
        return self.pending

    def put_frame(self, index: int, payload: bytes) -> None:
        if index != self.index or self.pending is None:
            raise ConversionError("전달된 원본 프레임에 대응하는 확대 결과가 필요합니다.")
        try:
            with Image.open(io.BytesIO(payload)) as native:
                if native.format != "PNG" or native.size != tuple(edge * AI_NATIVE_SCALE for edge in self.size):
                    raise ConversionError("영상 WebGPU 결과 해상도가 원본 프레임과 맞지 않습니다.")
                options = replace(self.options, width=self.size[0] * self.scale, height=self.size[1] * self.scale, fit="stretch")
                image = _apply_image_geometry(native, "png", options)
                try:
                    if self.target.endswith("-sequence"):
                        extension = self.target.split("-")[0]
                        if self.archive is None:
                            self.archive = zipfile.ZipFile(self.output, "w", zipfile.ZIP_DEFLATED)
                        frame_path = self.root / f"frame.{extension}"
                        _write_image(image, frame_path, extension, self.options)
                        self.archive.write(frame_path, f"{self.source.stem}_{index:06d}.{extension}")
                        frame_path.unlink()
                    else:
                        if self.encoder is None:
                            args = video_frame_encoder_args(self.source, "pipe:0", self.target, self.options, self.fps)
                            args.extend(["-threads", "1", "-fs", str(self.byte_limit), str(self.output)])
                            self.encoder = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=self.error_log)
                        image.save(self.encoder.stdin, format="PNG")
                        self.encoder.stdin.flush()
                finally:
                    image.close()
        except (OSError, ValueError) as error:
            raise self._error("확대 프레임을 저장하지 못했습니다") from error
        if self.output.exists() and self.output.stat().st_size >= self.byte_limit:
            raise ConversionError("웹 영상 결과 용량 제한을 넘었습니다. 최적화 또는 배율을 낮춰 주세요.")
        self.index += 1
        self.pending = None

    def finish(self) -> Path:
        if not self.eof or self.pending is not None or self.index == 0:
            raise ConversionError("모든 영상 프레임의 업스케일이 완료되어야 저장할 수 있습니다.")
        if self.archive is not None:
            self.archive.close()
            self.archive = None
        if self.encoder is not None:
            self.encoder.stdin.close()
            if self.encoder.wait(timeout=60):
                raise self._error("영상 저장에 실패했습니다")
        if not self.output.is_file() or self.output.stat().st_size == 0 or self.output.stat().st_size >= self.byte_limit:
            raise ConversionError("영상 결과가 비어 있거나 용량 제한을 넘었습니다.")
        return self.output

    def close(self) -> None:
        for process in (self.decoder, self.encoder):
            if process is not None:
                if process.poll() is None:
                    process.kill()
                process.wait()
                for stream in (process.stdin, process.stdout):
                    if stream is not None:
                        try:
                            stream.close()
                        except OSError:
                            pass  # A killed encoder may have already closed its pipe.
        if self.archive is not None:
            self.archive.close()
        self.error_log.close()
        shutil.rmtree(self.root, ignore_errors=True)
