from __future__ import annotations

import argparse
import json
from pathlib import Path

from TosunFluxConverter import (
    ASPECTS, FIT_MODES, FRAME_RATES, MAX_OUTPUT_DIMENSION, OPTIMIZATION_MODES,
    RESOLUTIONS, UPSCALE_ENGINES, UPSCALE_FACTORS, ConversionOptions,
    common_targets, conversion_profile, convert_files, source_metadata,
)


def emit(payload: dict[str, object]) -> None:
    print(json.dumps(payload, ensure_ascii=False), flush=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    targets_parser = subparsers.add_parser("targets")
    targets_parser.add_argument("files", nargs="+")

    subparsers.add_parser("health")

    convert_parser = subparsers.add_parser("convert")
    convert_parser.add_argument("--output", required=True)
    convert_parser.add_argument("--target", required=True)
    convert_parser.add_argument("--optimize", choices=OPTIMIZATION_MODES, default="source")
    convert_parser.add_argument("--resolution", choices=("source", *RESOLUTIONS), default="source")
    convert_parser.add_argument("--aspect", choices=("source", *ASPECTS), default="source")
    convert_parser.add_argument("--fit", choices=FIT_MODES, default="fit")
    convert_parser.add_argument("--width", type=int)
    convert_parser.add_argument("--height", type=int)
    convert_parser.add_argument("--fps", choices=FRAME_RATES, default="source")
    convert_parser.add_argument("--scale-factor", type=float, choices=UPSCALE_FACTORS, default=1.0)
    convert_parser.add_argument("--upscale-engine", choices=UPSCALE_ENGINES, default="resize")
    convert_parser.add_argument("--overwrite", action="store_true")
    convert_parser.add_argument("files", nargs="+")

    args = parser.parse_args()
    if args.command == "health":
        emit({"status": "ok", "product": "Tosun Flux", "version": "1.2.7", "profile": conversion_profile()})
        return 0

    files = [Path(item) for item in args.files]
    if args.command == "targets":
        emit({"targets": list(common_targets(files)), "metadata": [source_metadata(path) for path in files]})
        return 0

    def progress(index: int, total: int, result: object, error: Exception | None) -> None:
        outputs = [str(path) for path in getattr(result, "outputs", ())]
        emit(
            {
                "event": "progress",
                "index": index,
                "total": total,
                "source": str(files[index - 1]),
                "outputs": outputs,
                "error": str(error) if error else None,
            }
        )

    if (args.width is None) != (args.height is None):
        parser.error("--width와 --height는 함께 지정해야 합니다.")
    if args.width is not None and not (2 <= args.width <= MAX_OUTPUT_DIMENSION and 2 <= args.height <= MAX_OUTPUT_DIMENSION):
        parser.error(f"직접 해상도는 가로·세로 2~{MAX_OUTPUT_DIMENSION} 범위여야 합니다.")

    options = ConversionOptions(args.optimize, args.resolution, args.aspect, args.fit, args.width, args.height, args.fps, args.scale_factor, args.upscale_engine)
    results = convert_files(files, Path(args.output), args.target, progress, options, args.overwrite)
    emit({"event": "done", "success": len(results), "total": len(files)})
    return 0 if len(results) == len(files) else 1


if __name__ == "__main__":
    raise SystemExit(main())
