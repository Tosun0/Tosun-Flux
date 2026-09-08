from __future__ import annotations

import argparse
import json
from pathlib import Path

from converter import ConversionOptions, common_targets, convert_files


def emit(payload: dict[str, object]) -> None:
    print(json.dumps(payload, ensure_ascii=False), flush=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    targets_parser = subparsers.add_parser("targets")
    targets_parser.add_argument("files", nargs="+")

    convert_parser = subparsers.add_parser("convert")
    convert_parser.add_argument("--output", required=True)
    convert_parser.add_argument("--target", required=True)
    convert_parser.add_argument("--optimize", choices=("source", "quality", "balanced", "small"), default="source")
    convert_parser.add_argument("--resolution", choices=("source", "4k", "qhd", "fhd", "hd"), default="source")
    convert_parser.add_argument("--aspect", choices=("source", "16:9", "9:16", "1:1", "4:3", "3:4"), default="source")
    convert_parser.add_argument("--fit", choices=("fit", "fill", "stretch"), default="fit")
    convert_parser.add_argument("files", nargs="+")

    args = parser.parse_args()
    files = [Path(item) for item in args.files]
    if args.command == "targets":
        emit({"targets": list(common_targets(files))})
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

    options = ConversionOptions(args.optimize, args.resolution, args.aspect, args.fit)
    results = convert_files(files, Path(args.output), args.target, progress, options)
    emit({"event": "done", "success": len(results), "total": len(files)})
    return 0 if len(results) == len(files) else 1


if __name__ == "__main__":
    raise SystemExit(main())
