from __future__ import annotations

import io
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

from fastapi.testclient import TestClient
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Source" / "TosunFluxWeb"))

from TosunFluxWeb import app  # noqa: E402
from TosunFluxConverter import ConversionOptions, RESOLUTIONS, conversion_profile, convert_file  # noqa: E402


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
        self.assertEqual(self.client.get("/api/health").json()["profile"], json.loads(result.stdout)["profile"])
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


if __name__ == "__main__":
    unittest.main()
