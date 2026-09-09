from __future__ import annotations

import csv
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Source" / "TosunFluxBackend"))
from PIL import Image

from TosunFluxConverter import ConversionOptions, common_targets, convert_file, supported_targets, target_dimensions


class ConverterTests(unittest.TestCase):
    def test_target_intersection(self) -> None:
        self.assertEqual(common_targets([Path("a.png"), Path("b.jpg")])[0], "png")
        self.assertIn("mp4", supported_targets(Path("clip.mov")))
        self.assertIn("png-sequence", supported_targets(Path("clip.mov")))
        self.assertIn("jpg-sequence", supported_targets(Path("clip.mov")))
        self.assertIn("pdf", supported_targets(Path("document.pdf")))

    def test_resolution_and_aspect_dimensions(self) -> None:
        options = ConversionOptions(resolution="fhd", aspect="9:16")
        self.assertEqual(target_dimensions((4000, 3000), options), (1080, 1920))

    def test_custom_dimensions_override_presets(self) -> None:
        options = ConversionOptions(resolution="fhd", aspect="16:9", width=1440, height=1080)
        self.assertEqual(target_dimensions((4000, 3000), options), (1440, 1080))

    def test_extended_resolution_presets(self) -> None:
        self.assertEqual(target_dimensions((1920, 1080), ConversionOptions(resolution="4k-uhd")), (3840, 2160))
        self.assertEqual(target_dimensions((1920, 1080), ConversionOptions(resolution="4k")), (3840, 2160))
        self.assertEqual(target_dimensions((1920, 1080), ConversionOptions(resolution="sd")), (720, 404))

    def test_image_optimization_and_resize(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "photo.png"
            Image.new("RGB", (1600, 900), "#7286ee").save(source)
            options = ConversionOptions(optimize="small", resolution="hd", aspect="1:1", fit="fill")
            result = convert_file(source, root / "out", "jpg", options)
            with Image.open(result.outputs[0]) as converted:
                self.assertEqual(converted.size, (1280, 1280))

    def test_identity_conversion_copies_original_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "photo.png"
            Image.new("RGB", (320, 240), "#7286ee").save(source)
            result = convert_file(source, root / "out", "png", ConversionOptions())
            self.assertEqual(result.outputs[0].read_bytes(), source.read_bytes())

    def test_pdf_can_be_optimized_without_rasterizing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "document.pdf"
            Image.new("RGB", (320, 240), "white").save(source, "PDF")
            result = convert_file(source, root / "out", "pdf", ConversionOptions(optimize="balanced"))
            self.assertTrue(result.outputs[0].is_file())
            self.assertGreater(result.outputs[0].stat().st_size, 0)

    def test_csv_json_roundtrip(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "data.csv"
            with source.open("w", encoding="utf-8-sig", newline="") as handle:
                writer = csv.DictWriter(handle, fieldnames=["name", "value"])
                writer.writeheader()
                writer.writerow({"name": "토순", "value": "1"})
            result = convert_file(source, root / "out", "json")
            payload = json.loads(result.outputs[0].read_text(encoding="utf-8"))
            self.assertEqual(payload[0]["name"], "토순")

    def test_never_overwrites_output(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "note.txt"
            source.write_text("hello", encoding="utf-8")
            output = root / "out"
            first = convert_file(source, output, "md").outputs[0]
            second = convert_file(source, output, "md").outputs[0]
            self.assertEqual(first.name, "note.md")
            self.assertEqual(second.name, "note (1).md")


if __name__ == "__main__":
    unittest.main()
