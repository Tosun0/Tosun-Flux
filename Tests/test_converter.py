from __future__ import annotations

import csv
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Source" / "TosunFluxBackend"))
from PIL import Image, ImageSequence

from TosunFluxConverter import ConversionError, ConversionOptions, _convert_ai_video, _is_identity_conversion, _run_ai_upscale, _video_filter, bundled_tool, common_targets, convert_file, supported_targets, target_dimensions


class ConverterTests(unittest.TestCase):
    def test_target_intersection(self) -> None:
        self.assertEqual(common_targets([Path("a.png"), Path("b.jpg")])[0], "png")
        self.assertIn("mp4", supported_targets(Path("clip.mov")))
        self.assertIn("png-sequence", supported_targets(Path("clip.mov")))
        self.assertIn("jpg-sequence", supported_targets(Path("clip.mov")))
        self.assertIn("pdf", supported_targets(Path("document.pdf")))

    def test_document_extensions_are_not_supported(self) -> None:
        for suffix in (".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".hwp", ".hwpx"):
            self.assertEqual(supported_targets(Path(f"document{suffix}")), ())

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

    def test_upscale_factor_dimensions(self) -> None:
        options = ConversionOptions(scale_factor=2.0)
        self.assertEqual(target_dimensions((960, 540), options), (1920, 1080))
        self.assertIn("scale=1920:1080", _video_filter(options, (960, 540)))

    def test_upscale_factor_rejects_oversized_output(self) -> None:
        with self.assertRaises(ConversionError):
            target_dimensions((5000, 3000), ConversionOptions(scale_factor=4.0))

    def test_upscale_never_uses_identity_copy(self) -> None:
        self.assertFalse(_is_identity_conversion(Path("photo.jpg"), "jpg", ConversionOptions(scale_factor=2.0, upscale_engine="ai")))

    def test_ai_video_uses_selected_output_fps(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "clip.mp4"
            source.write_bytes(b"source")
            output_dir = root / "out"
            output_dir.mkdir()
            calls: list[list[str]] = []

            def fake_run(args: list[str], **_: object) -> subprocess.CompletedProcess[str]:
                calls.append(args)
                if "-vsync" in args:
                    pattern = Path(args[-1])
                    frame = Path(str(pattern).replace("%08d", "00000001"))
                    frame.parent.mkdir(parents=True, exist_ok=True)
                    Image.new("RGB", (4, 4), "white").save(frame)
                elif "-m" in args:
                    output = Path(args[args.index("-o") + 1])
                    if output.suffix:
                        Image.new("RGB", (8, 8), "white").save(output)
                    else:
                        output.mkdir(parents=True, exist_ok=True)
                        Image.new("RGB", (8, 8), "white").save(output / "00000001.png")
                else:
                    Path(args[-1]).write_bytes(b"encoded")
                return subprocess.CompletedProcess(args, 0, "", "")

            with patch("TosunFluxConverter.bundled_tool", return_value=Path("ffmpeg")), \
                 patch("TosunFluxConverter.ai_upscaler_tool", return_value=Path("realesrgan-ncnn-vulkan")), \
                 patch("TosunFluxConverter.source_metadata", return_value={"fps": "24", "width": 4, "height": 4}), \
                 patch("TosunFluxConverter.subprocess.run", side_effect=fake_run):
                result = _convert_ai_video(
                    source,
                    output_dir,
                    "mp4",
                    ConversionOptions(fps="60", scale_factor=2.0, upscale_engine="ai"),
                )

            self.assertTrue(result.outputs[0].is_file())
            extract_call = calls[0]
            encode_call = calls[-1]
            self.assertIn("-vf", extract_call)
            self.assertIn("fps=60", extract_call)
            self.assertEqual(encode_call[encode_call.index("-framerate") + 1], "60")

    def test_ai_2x_uses_native_4x_output_then_downsamples(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source.png"
            destination = root / "output.png"
            Image.new("RGB", (8, 6), "white").save(source)
            tool = root / "realesrgan-ncnn-vulkan.exe"
            tool.touch()
            (root / "models").mkdir()
            calls: list[list[str]] = []

            def fake_run(args: list[str], **_: object) -> subprocess.CompletedProcess[str]:
                calls.append(args)
                output = Path(args[args.index("-o") + 1])
                Image.new("RGB", (32, 24), "white").save(output)
                return subprocess.CompletedProcess(args, 0, "", "")

            with patch("TosunFluxConverter.ai_upscaler_tool", return_value=tool), \
                 patch("TosunFluxConverter.subprocess.run", side_effect=fake_run):
                _run_ai_upscale(source, destination, ConversionOptions(scale_factor=2.0, upscale_engine="ai"))

            self.assertEqual(calls[0][calls[0].index("-s") + 1], "4")
            with Image.open(destination) as converted:
                self.assertEqual(converted.size, (16, 12))

    def test_ai_frame_sequence_uses_native_animation_scale(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source"
            destination = root / "output"
            source.mkdir()
            destination.mkdir()
            Image.new("RGB", (8, 6), "white").save(source / "00000001.png")
            tool = root / "realesrgan-ncnn-vulkan.exe"
            tool.touch()
            (root / "models").mkdir()
            calls: list[list[str]] = []

            def fake_run(args: list[str], **_: object) -> subprocess.CompletedProcess[str]:
                calls.append(args)
                output = Path(args[args.index("-o") + 1])
                Image.new("RGB", (16, 12), "white").save(output / "00000001.png")
                return subprocess.CompletedProcess(args, 0, "", "")

            with patch("TosunFluxConverter.ai_upscaler_tool", return_value=tool), \
                 patch("TosunFluxConverter.subprocess.run", side_effect=fake_run):
                _run_ai_upscale(source, destination, ConversionOptions(scale_factor=2.0, upscale_engine="ai"))

            self.assertEqual(calls[0][calls[0].index("-n") + 1], "realesr-animevideov3")
            self.assertEqual(calls[0][calls[0].index("-s") + 1], "2")
            with Image.open(destination / "00000001.png") as converted:
                self.assertEqual(converted.size, (16, 12))

    def test_ai_upscale_rejects_vulkan_failure_with_zero_exit_code(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source.png"
            destination = root / "output.png"
            Image.new("RGB", (8, 6), "white").save(source)
            tool = root / "realesrgan-ncnn-vulkan.exe"
            tool.touch()
            (root / "models").mkdir()

            with patch("TosunFluxConverter.ai_upscaler_tool", return_value=tool), \
                 patch(
                     "TosunFluxConverter.subprocess.run",
                     return_value=subprocess.CompletedProcess([], 0, "", "vkQueueSubmit failed -4"),
                 ):
                with self.assertRaises(ConversionError):
                    _run_ai_upscale(source, destination, ConversionOptions(scale_factor=2.0, upscale_engine="ai"))

    def test_animated_gif_ai_upscale_preserves_animation(self) -> None:
        if bundled_tool("ffmpeg", "ffmpeg") is None:
            self.skipTest("FFmpeg is not available")

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "animated.gif"
            frames = [Image.new("RGB", (16, 12), color) for color in ("red", "green", "blue")]
            try:
                frames[0].save(source, save_all=True, append_images=frames[1:], duration=[70, 120, 90], loop=2)
            finally:
                for frame in frames:
                    frame.close()

            def fake_upscale(frame_source: Path, frame_destination: Path, _: ConversionOptions) -> Path:
                for frame_path in sorted(frame_source.glob("*.png")):
                    with Image.open(frame_path) as image:
                        image.resize((32, 24), Image.Resampling.NEAREST).save(frame_destination / frame_path.name)
                return frame_destination

            with patch("TosunFluxConverter._run_ai_upscale", side_effect=fake_upscale):
                result = convert_file(
                    source,
                    root / "out",
                    "gif",
                    ConversionOptions(scale_factor=2.0, upscale_engine="ai"),
                )

            with Image.open(result.outputs[0]) as converted:
                self.assertTrue(getattr(converted, "is_animated", False))
                self.assertEqual(converted.size, (32, 24))
                self.assertEqual(converted.n_frames, 3)
                durations = []
                colors = []
                for frame in ImageSequence.Iterator(converted):
                    durations.append(frame.info.get("duration"))
                    colors.append(frame.convert("RGB").getpixel((0, 0)))
                self.assertEqual(sum(durations), 280)
                self.assertEqual(len(set(colors)), 3)

    def test_backend_cli_accepts_extended_resolution_presets(self) -> None:
        backend = Path(__file__).resolve().parents[1] / "Source" / "TosunFluxBackend" / "TosunFluxBackend.py"
        for preset in ("4k-uhd", "sd"):
            completed = subprocess.run(
                [sys.executable, str(backend), "convert", "--output", "out", "--target", "png", "--resolution", preset, "missing.png"],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
            self.assertNotEqual(completed.returncode, 2, completed.stderr)

    def test_backend_cli_accepts_scale_factor(self) -> None:
        backend = Path(__file__).resolve().parents[1] / "Source" / "TosunFluxBackend" / "TosunFluxBackend.py"
        completed = subprocess.run(
            [sys.executable, str(backend), "convert", "--output", "out", "--target", "mp4", "--scale-factor", "2", "missing.mp4"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        self.assertNotEqual(completed.returncode, 2, completed.stderr)

    def test_backend_cli_accepts_ai_upscale_engine(self) -> None:
        backend = Path(__file__).resolve().parents[1] / "Source" / "TosunFluxBackend" / "TosunFluxBackend.py"
        completed = subprocess.run(
            [sys.executable, str(backend), "convert", "--output", "out", "--target", "mp4", "--scale-factor", "2", "--upscale-engine", "ai", "missing.mp4"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        self.assertNotEqual(completed.returncode, 2, completed.stderr)

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

    def test_animated_gif_to_gif_preserves_frames_and_timing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "animated.gif"
            frames = [Image.new("RGB", (16, 12), color) for color in ("red", "green", "blue")]
            try:
                frames[0].save(
                    source,
                    save_all=True,
                    append_images=frames[1:],
                    duration=[70, 120, 90],
                    loop=3,
                    disposal=2,
                )
            finally:
                for frame in frames:
                    frame.close()

            result = convert_file(
                source,
                root / "out",
                "gif",
                ConversionOptions(optimize="balanced", width=8, height=6),
            )
            with Image.open(result.outputs[0]) as converted:
                self.assertTrue(getattr(converted, "is_animated", False))
                self.assertEqual(converted.n_frames, 3)
                self.assertEqual(converted.size, (8, 6))
                self.assertEqual(converted.info.get("loop"), 3)
                durations = []
                colors = []
                for frame in ImageSequence.Iterator(converted):
                    durations.append(frame.info.get("duration"))
                    colors.append(frame.convert("RGB").getpixel((0, 0)))
                self.assertEqual(durations, [70, 120, 90])
                self.assertEqual(len(set(colors)), 3)

    def test_animated_gif_large_resize_uses_streaming_path(self) -> None:
        if bundled_tool("ffmpeg", "ffmpeg") is None:
            self.skipTest("FFmpeg is not available")

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "animated.gif"
            frames = [Image.new("RGB", (16, 12), color) for color in ("red", "green", "blue")]
            try:
                frames[0].save(
                    source,
                    save_all=True,
                    append_images=frames[1:],
                    duration=[70, 120, 90],
                    loop=3,
                    disposal=2,
                )
            finally:
                for frame in frames:
                    frame.close()

            with patch("TosunFluxConverter.ANIMATED_GIF_MEMORY_LIMIT", 1):
                result = convert_file(
                    source,
                    root / "out",
                    "gif",
                    ConversionOptions(optimize="balanced", width=8, height=6),
                )

            with Image.open(result.outputs[0]) as converted:
                self.assertTrue(getattr(converted, "is_animated", False))
                self.assertEqual(converted.n_frames, 3)
                self.assertEqual(converted.size, (8, 6))
                self.assertEqual(converted.info.get("loop"), 3)

    def test_jpeg_alias_preserves_original_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source.jpeg"
            Image.new("RGB", (64, 64), "cyan").save(source, "JPEG", quality=91)
            result = convert_file(source, root / "out", "jpg")
            self.assertEqual(source.read_bytes(), result.outputs[0].read_bytes())

    def test_pdf_can_be_optimized_without_rasterizing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "document.pdf"
            Image.new("RGB", (320, 240), "white").save(source, "PDF")
            result = convert_file(source, root / "out", "pdf", ConversionOptions(optimize="balanced"))
            self.assertTrue(result.outputs[0].is_file())
            self.assertGreater(result.outputs[0].stat().st_size, 0)

    @unittest.skipIf(bundled_tool("pdftoppm.exe", "pdftoppm") is None, "Poppler is unavailable")
    def test_pdf_can_render_to_jpg(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "document.pdf"
            Image.new("RGB", (320, 240), "white").save(source, "PDF")
            result = convert_file(source, root / "out", "jpg")
            self.assertTrue(result.outputs)
            with Image.open(result.outputs[0]) as converted:
                self.assertEqual(converted.format, "JPEG")

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
