from __future__ import annotations

import io
import sys
import unittest
from pathlib import Path

from fastapi.testclient import TestClient
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Source" / "TosunFluxWeb"))

from TosunFluxWeb import app  # noqa: E402


class WebTests(unittest.TestCase):
    def setUp(self) -> None:
        self.client = TestClient(app)

    def test_targets_returns_common_formats(self) -> None:
        response = self.client.post("/api/targets", json=["one.png", "two.jpg"])
        self.assertEqual(response.status_code, 200)
        self.assertIn("webp", response.json()["targets"])

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
