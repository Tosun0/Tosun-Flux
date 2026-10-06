from __future__ import annotations

import io
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path
from unittest.mock import patch

from fastapi.testclient import TestClient
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Source" / "TosunFluxWeb"))

from TosunFluxWeb import app, video_jobs, _expire_videos  # noqa: E402
from TosunFluxWebVideo import VIDEO_IDLE_SECONDS  # noqa: E402
from TosunFluxConverter import ConversionOptions, RESOLUTIONS, conversion_profile, convert_file, bundled_tool, source_metadata  # noqa: E402


class WebTests(unittest.TestCase):
    def setUp(self) -> None:
        self.client = TestClient(app)

    def test_targets_returns_common_formats(self) -> None:
        response = self.client.post("/api/targets", json=["one.png", "two.jpg"])
        self.assertEqual(response.status_code, 200)
        self.assertIn("webp", response.json()["targets"])

    def test_web_and_desktop_expose_the_same_live_profile(self) -> None:
        backend = ROOT / "Source" / "TosunFluxBackend" / "TosunFluxBackend.py"
        result = subprocess.run([sys.executable, str(backend), "health"], capture_output=True, text=True, check=True)
        backend_health = json.loads(result.stdout)
        web_health = self.client.get("/api/health").json()
        self.assertEqual(web_health["profile"], backend_health["profile"])
        self.assertEqual(web_health["version"], backend_health["version"])
        for project in ("TosunFlux", "TosunFluxMac", "TosunFluxInstaller"):
            version = ET.parse(ROOT / "Source" / project / f"{project}.csproj").findtext("PropertyGroup/Version")
            self.assertEqual(version, web_health["version"])
        with patch.dict(RESOLUTIONS, {"test-preset": (800, 600)}):
            self.assertEqual(self.client.get("/api/health").json()["profile"]["resolutions"]["test-preset"], [800, 600])

    def test_webgpu_results_use_desktop_geometry_and_encoding(self) -> None:
        image = Image.new("RGBA", (16, 12))
        image.putdata([(x * 15, y * 20, (x + y) * 9, 80 + (x * 11) % 176) for y in range(12) for x in range(16)])
        source = io.BytesIO()
        image.save(source, format="PNG")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            native = root / "native.png"
            native.write_bytes(source.getvalue())
            for scale in (2, 4):
                for target in ("png", "jpg", "webp", "bmp", "tiff"):
                    for optimize in conversion_profile()["optimizations"]:
                        with self.subTest(scale=scale, target=target, optimize=optimize):
                            response = self.client.post("/api/convert", files={"files": ("native.png", source.getvalue(), "image/png")}, data={"target": target, "optimize": optimize, "webgpu_scale": scale})
                            self.assertEqual(response.status_code, 200, response.text if response.status_code != 200 else "")
                            expected = convert_file(native, root / "expected", target, ConversionOptions(optimize=optimize, width=4 * scale, height=3 * scale, fit="stretch"))
                            self.assertEqual(response.content, expected.outputs[0].read_bytes())
                            with Image.open(io.BytesIO(response.content)) as output:
                                self.assertEqual(output.size, (4 * scale, 3 * scale))

    def test_webgpu_batch_preserves_per_file_sizes_and_duplicate_names(self) -> None:
        files = []
        for size in ((16, 12), (8, 4)):
            image = io.BytesIO()
            Image.new("RGB", size, "#8795ff").save(image, format="PNG")
            files.append(("files", ("토순.png", image.getvalue(), "image/png")))
        response = self.client.post("/api/convert", files=files, data={"target": "webp", "webgpu_scale": 2})
        self.assertEqual(response.status_code, 200)
        with zipfile.ZipFile(io.BytesIO(response.content)) as archive:
            self.assertEqual(archive.namelist(), ["토순.webp", "토순 (2).webp"])
            for name, size in zip(archive.namelist(), ((8, 6), (4, 2))):
                with Image.open(io.BytesIO(archive.read(name))) as output:
                    self.assertEqual(output.size, size)

    def test_webgpu_rejects_invalid_results_and_double_processing(self) -> None:
        source = io.BytesIO()
        Image.new("RGB", (16, 12), "white").save(source, format="PNG")
        for extra in ({"webgpu_scale": 3}, {"scale_factor": 2}, {"resolution": "fhd"}, {"width": 8, "height": 6}, {"fps": "30"}):
            response = self.client.post("/api/convert", files={"files": ("sample.png", source.getvalue(), "image/png")}, data={"target": "png", "webgpu_scale": 2, **extra})
            self.assertEqual(response.status_code, 400, response.text)
        for content in (b"not an image",):
            response = self.client.post("/api/convert", files={"files": ("sample.png", content, "image/png")}, data={"target": "png", "webgpu_scale": 2})
            self.assertEqual(response.status_code, 400)
        with patch("TosunFluxWeb.MAX_OUTPUT_DIMENSION", 7):
            response = self.client.post("/api/convert", files={"files": ("sample.png", source.getvalue(), "image/png")}, data={"target": "png", "webgpu_scale": 2})
            self.assertEqual(response.status_code, 400)

    def test_webgpu_client_assets_are_allowed(self) -> None:
        response = self.client.get("/")
        self.assertEqual(response.status_code, 200)
        self.assertIn("ort.webgpu.min.js", response.text)
        self.assertIn("https://cdn.jsdelivr.net", response.headers["content-security-policy"])
        self.assertIn("'wasm-unsafe-eval'", response.headers["content-security-policy"])
        self.assertIn("https://huggingface.co", response.headers["content-security-policy"])

    def test_image_conversion_downloads_real_jpeg(self) -> None:
        source = io.BytesIO()
        Image.new("RGB", (12, 8), "#8795ff").save(source, format="PNG")
        response = self.client.post(
            "/api/convert",
            files={"files": ("../../sample.png", source.getvalue(), "image/png")},
            data={"target": "jpg"},
        )
        self.assertEqual(response.status_code, 200, response.text)
        self.assertIn('filename="sample.jpg"', response.headers["content-disposition"])
        self.assertNotIn("01-sample", response.headers["content-disposition"])
        with Image.open(io.BytesIO(response.content)) as converted:
            self.assertEqual(converted.format, "JPEG")
            self.assertEqual(converted.size, (12, 8))

    def test_rejects_target_not_shared_by_all_files(self) -> None:
        image = io.BytesIO()
        Image.new("RGB", (4, 4), "white").save(image, format="PNG")
        response = self.client.post(
            "/api/convert",
            files=[
                ("files", ("image.png", image.getvalue(), "image/png")),
                ("files", ("notes.txt", b"hello", "text/plain")),
            ],
            data={"target": "jpg"},
        )
        self.assertEqual(response.status_code, 400)

    def test_webgpu_video_stream_keeps_fps_audio_and_sequences(self) -> None:
        tool = bundled_tool("ffmpeg")
        self.assertIsNotNone(tool, "FFmpeg is required for video verification")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "토순.mp4"
            subprocess.run([str(tool), "-y", "-f", "lavfi", "-i", "testsrc=size=32x24:rate=6:duration=1",
                            "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", str(source)], capture_output=True, check=True)
            for target, scale in (("mp4", 2), ("webm", 4), ("gif", 2), ("png-sequence", 4), ("jpg-sequence", 2)):
                with self.subTest(target=target, scale=scale):
                    start = self.client.post("/api/webgpu/video", files={"file": (source.name, source.read_bytes(), "video/mp4")}, data={"target": target, "scale": scale, "fps": "24"})
                    self.assertEqual(start.status_code, 200, start.text)
                    token = start.json()["id"]
                    job = video_jobs[token]
                    base = "/api/webgpu/video/" + token
                    count = 0
                    while True:
                        response = self.client.get(base + f"/frames/{count}")
                        self.assertIn(response.status_code, (200, 204), response.text if response.status_code != 200 else "")
                        if response.status_code == 204:
                            break
                        self.assertEqual(self.client.get(base + f"/frames/{count}").content, response.content)
                        with Image.open(io.BytesIO(response.content)) as frame:
                            native = frame.resize((frame.width * 4, frame.height * 4))
                            encoded = io.BytesIO()
                            native.save(encoded, format="PNG")
                        put = self.client.put(base + f"/frames/{count}", files={"file": ("frame.png", encoded.getvalue(), "image/png")})
                        self.assertEqual(put.status_code, 200, put.text)
                        count += 1
                    self.assertEqual(count, 24)
                    response = self.client.post(base + "/finish")
                    self.assertEqual(response.status_code, 200, response.text if response.status_code != 200 else "")
                    self.assertNotIn(token, video_jobs)
                    self.assertFalse(job.root.exists())
                    self.assertIsNotNone(job.decoder.poll())
                    if target.endswith("-sequence"):
                        with zipfile.ZipFile(io.BytesIO(response.content)) as archive:
                            self.assertEqual(len(archive.namelist()), count)
                            with Image.open(io.BytesIO(archive.read(archive.namelist()[0]))) as image:
                                self.assertEqual(image.size, (32 * scale, 24 * scale))
                    elif target == "gif":
                        with Image.open(io.BytesIO(response.content)) as image:
                            self.assertGreater(image.n_frames, 1)
                            self.assertEqual(image.size, (32 * scale, 24 * scale))
                    else:
                        output = root / ("output." + target)
                        output.write_bytes(response.content)
                        metadata = source_metadata(output)
                        self.assertEqual((metadata["width"], metadata["height"]), (32 * scale, 24 * scale))
                        self.assertEqual(metadata["fps"], "24")
                        probe = subprocess.run([str(tool), "-i", str(output)], capture_output=True, text=True)
                        self.assertIn("Audio:", probe.stderr)
                        self.assertLess(abs(float(metadata["duration"]) - 1), .15)

    def test_webgpu_video_rejects_invalid_results_and_cleans_cancelled_expired_jobs(self) -> None:
        tool = bundled_tool("ffmpeg")
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "clip.mp4"
            subprocess.run([str(tool), "-y", "-f", "lavfi", "-i", "testsrc=size=32x24:rate=6:duration=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", str(source)], capture_output=True, check=True)
            for action in ("invalid", "order", "cancel", "expired", "limit"):
                start = self.client.post("/api/webgpu/video", files={"file": (source.name, source.read_bytes())}, data={"target": "mp4", "scale": 2})
                self.assertEqual(start.status_code, 200, start.text)
                token = start.json()["id"]
                job = video_jobs[token]
                base = "/api/webgpu/video/" + token
                busy = self.client.post("/api/webgpu/video", files={"file": (source.name, source.read_bytes())}, data={"target": "mp4", "scale": 2})
                self.assertEqual(busy.status_code, 503)
                if action == "invalid":
                    self.client.get(base + "/frames/0")
                    result = self.client.put(base + "/frames/0", files={"file": ("wrong.png", b"invalid")})
                    self.assertEqual(result.status_code, 400)
                elif action == "order":
                    self.assertEqual(self.client.get(base + "/frames/1").status_code, 400)
                elif action == "limit":
                    with patch("TosunFluxWebVideo.MAX_VIDEO_INPUT_PIXELS", 1):
                        self.assertEqual(self.client.get(base + "/frames/0").status_code, 400)
                elif action == "expired":
                    job.touched -= VIDEO_IDLE_SECONDS + 1
                    _expire_videos()
                else:
                    self.assertEqual(self.client.delete(base).status_code, 200)
                self.assertNotIn(token, video_jobs)
                self.assertFalse(job.root.exists())
                self.assertIsNotNone(job.decoder.poll())
            bad = self.client.post("/api/webgpu/video", files={"file": ("bad.mp4", b"not video")}, data={"target": "mp4", "scale": 2})
            self.assertEqual(bad.status_code, 400)
            self.assertFalse(video_jobs)


if __name__ == "__main__":
    unittest.main()
