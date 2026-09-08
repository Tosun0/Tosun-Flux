import os
import sys
from pathlib import Path


bundle_root = getattr(sys, "_MEIPASS", None)
if bundle_root:
    root = Path(bundle_root)
    sys.path.insert(0, str(root))
    os.add_dll_directory(str(root))
    os.environ["PATH"] = str(root) + os.pathsep + os.environ.get("PATH", "")
