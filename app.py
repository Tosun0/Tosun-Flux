from __future__ import annotations

import os
import sys
import threading
import importlib
import ctypes
from ctypes import wintypes
from pathlib import Path


def _enable_windows_dpi() -> None:
    if os.name != "nt":
        return
    try:
        ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    except (AttributeError, OSError, OverflowError):
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(2)
        except (AttributeError, OSError):
            pass


def _prepare_tk_runtime() -> None:
    candidates = []
    if getattr(sys, "frozen", False):
        root = Path(getattr(sys, "_MEIPASS", Path(sys.executable).parent))
        candidates.append(root / "tcl")
        candidates.append(Path(sys.executable).parent / "tcl")
    candidates.append(Path(sys.executable).resolve().parent.parent / "tcl")
    candidates.append(Path(r"C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\ThirdParty\Python3\Win64\tcl"))
    for base in candidates:
        if (base / "tcl8.6").is_dir() and (base / "tk8.6").is_dir():
            os.environ.setdefault("TCL_LIBRARY", str(base / "tcl8.6"))
            os.environ.setdefault("TK_LIBRARY", str(base / "tk8.6"))
            return

_enable_windows_dpi()
_prepare_tk_runtime()

import tkinter as tk
filedialog = importlib.import_module("tkinter.filedialog")
messagebox = importlib.import_module("tkinter.messagebox")
ttk = importlib.import_module("tkinter.ttk")

from converter import common_targets, convert_files, file_kind


APP_NAME = "Tosun Flux"
APP_DESCRIPTION = "토순이의 파일 컨버터"
APP_VERSION = "v0.1.0"
WM_DROPFILES = 0x0233
GWL_WNDPROC = -4


def dpi_value(widget: tk.Misc, value: float) -> int:
    return max(1, round(value))


def rounded_rect(canvas: tk.Canvas, x1: float, y1: float, x2: float, y2: float, radius: float, fill: str, outline: str = "", width: int = 1, tag: str = "shape") -> None:
    radius = max(1, min(radius, (x2 - x1) / 2, (y2 - y1) / 2))
    canvas.create_rectangle(x1 + radius, y1, x2 - radius, y2, fill=fill, outline="", tags=tag)
    canvas.create_rectangle(x1, y1 + radius, x2, y2 - radius, fill=fill, outline="", tags=tag)
    for box, start in (
        ((x1, y1, x1 + radius * 2, y1 + radius * 2), 90),
        ((x2 - radius * 2, y1, x2, y1 + radius * 2), 0),
        ((x1, y2 - radius * 2, x1 + radius * 2, y2), 180),
        ((x2 - radius * 2, y2 - radius * 2, x2, y2), 270),
    ):
        canvas.create_arc(*box, start=start, extent=90, fill=fill, outline="", style="pieslice", tags=tag)
        if outline:
            canvas.create_arc(*box, start=start, extent=90, fill="", outline=outline, width=width, style="arc", tags=tag)
    if outline:
        canvas.create_line(x1 + radius, y1, x2 - radius, y1, fill=outline, width=width, tags=tag)
        canvas.create_line(x1 + radius, y2, x2 - radius, y2, fill=outline, width=width, tags=tag)
        canvas.create_line(x1, y1 + radius, x1, y2 - radius, fill=outline, width=width, tags=tag)
        canvas.create_line(x2, y1 + radius, x2, y2 - radius, fill=outline, width=width, tags=tag)


class RoundedCard(tk.Canvas):
    def __init__(self, parent: tk.Misc, fill: str, outline: str, radius: int = 24, padding: int = 20, **kwargs: object) -> None:
        super().__init__(parent, background=parent.cget("background"), highlightthickness=0, borderwidth=0, **kwargs)
        self.fill = fill
        self.outline = outline
        self.radius = dpi_value(parent, radius)
        self.padding = dpi_value(parent, padding)
        self.content = tk.Frame(self, background=fill)
        self.window_id = self.create_window(padding, padding, anchor="nw", window=self.content)
        self.bind("<Configure>", self._resize)

    def _resize(self, event: tk.Event) -> None:
        self.delete("shape")
        rounded_rect(self, 1, 1, max(2, event.width - 1), max(2, event.height - 1), self.radius, self.fill, self.outline, 1)
        self.coords(self.window_id, self.padding, self.padding)
        self.itemconfigure(self.window_id, width=max(1, event.width - self.padding * 2), height=max(1, event.height - self.padding * 2))
        self.tag_lower("shape")


class RoundedButton(tk.Canvas):
    def __init__(self, parent: tk.Misc, text: str, command: object, fill: str, hover: str, foreground: str, width: int = 112, height: int = 40, radius: int = 15, **kwargs: object) -> None:
        super().__init__(parent, width=dpi_value(parent, width), height=dpi_value(parent, height), background=parent.cget("background"), highlightthickness=0, borderwidth=0, **kwargs)
        self.text = text
        self.command = command
        self.fill = fill
        self.hover_fill = hover
        self.foreground = foreground
        self.radius = dpi_value(parent, radius)
        self.enabled = True
        self.hovered = False
        for sequence, handler in (("<Enter>", self._enter), ("<Leave>", self._leave), ("<Button-1>", self._click)):
            self.bind(sequence, handler)
        self.bind("<Configure>", lambda _event: self._draw())
        self._draw()

    def _enter(self, _event: tk.Event) -> None:
        self.hovered = True
        self._draw()

    def _leave(self, _event: tk.Event) -> None:
        self.hovered = False
        self._draw()

    def _click(self, _event: tk.Event) -> None:
        if self.enabled and callable(self.command):
            self.command()

    def _draw(self) -> None:
        self.delete("all")
        fill = self.hover_fill if self.hovered and self.enabled else self.fill
        foreground = self.foreground if self.enabled else "#7c89a8"
        rounded_rect(self, 1, 1, max(2, self.winfo_width() - 1), max(2, self.winfo_height() - 1), self.radius, fill)
        self.create_text(self.winfo_width() / 2, self.winfo_height() / 2, text=self.text, fill=foreground, font=("Segoe UI", 9, "bold"))

    def set_enabled(self, enabled: bool) -> None:
        self.enabled = enabled
        self.configure(cursor="hand2" if enabled else "arrow")
        self._draw()


class RoundedEntry(tk.Canvas):
    def __init__(self, parent: tk.Misc, variable: tk.StringVar, fill: str, outline: str, foreground: str, **kwargs: object) -> None:
        super().__init__(parent, height=dpi_value(parent, 40), background=parent.cget("background"), highlightthickness=0, borderwidth=0, **kwargs)
        self.fill = fill
        self.outline = outline
        self.radius = dpi_value(parent, 14)
        self.entry = tk.Entry(self, textvariable=variable, background=fill, foreground=foreground, insertbackground=foreground, relief="flat", borderwidth=0, highlightthickness=0, font=("Segoe UI", 9))
        self.window_id = self.create_window(dpi_value(parent, 14), dpi_value(parent, 20), anchor="w", window=self.entry)
        self.bind("<Configure>", self._resize)

    def _resize(self, event: tk.Event) -> None:
        self.delete("shape")
        rounded_rect(self, 1, 1, max(2, event.width - 1), max(2, event.height - 1), self.radius, self.fill, self.outline, 1)
        inset = dpi_value(self, 14)
        self.coords(self.window_id, inset, event.height / 2)
        self.itemconfigure(self.window_id, width=max(1, event.width - inset * 2), height=max(1, event.height - dpi_value(self, 8)))
        self.tag_lower("shape")


class RoundedSelect(tk.Canvas):
    def __init__(self, parent: tk.Misc, variable: tk.StringVar, fill: str, outline: str, foreground: str, muted: str, **kwargs: object) -> None:
        super().__init__(parent, height=dpi_value(parent, 40), background=parent.cget("background"), highlightthickness=0, borderwidth=0, **kwargs)
        self.variable = variable
        self.fill = fill
        self.outline = outline
        self.foreground = foreground
        self.muted = muted
        self.options: list[str] = []
        self.bind("<Button-1>", self._open)
        self.bind("<Configure>", lambda _event: self._draw())
        self._draw()

    def set_options(self, options: list[str]) -> None:
        self.options = options
        self._draw()

    def _draw(self) -> None:
        self.delete("all")
        rounded_rect(self, 1, 1, max(2, self.winfo_width() - 1), max(2, self.winfo_height() - 1), 14, self.fill, self.outline, 1)
        value = self.variable.get() or "형식 선택"
        color = self.foreground if self.variable.get() else self.muted
        self.create_text(14, self.winfo_height() / 2, anchor="w", text=value, fill=color, font=("Segoe UI", 9))
        self.create_text(max(20, self.winfo_width() - 18), self.winfo_height() / 2 - 1, text="⌄", fill=self.muted, font=("Segoe UI", 13, "bold"))

    def _open(self, _event: tk.Event) -> None:
        if not self.options:
            return
        popup = tk.Toplevel(self)
        popup.overrideredirect(True)
        popup.configure(background=self.outline)
        popup.geometry(f"{self.winfo_width()}x{min(260, len(self.options) * 34 + 8)}+{self.winfo_rootx()}+{self.winfo_rooty() + self.winfo_height() + 5}")
        listbox = tk.Listbox(popup, background=self.fill, foreground=self.foreground, selectbackground="#3b4e9a", selectforeground="#ffffff", borderwidth=0, highlightthickness=0, activestyle="none", font=("Segoe UI", 9), exportselection=False)
        listbox.pack(fill="both", expand=True, padx=1, pady=1)
        for option in self.options:
            listbox.insert(tk.END, option)
        if self.variable.get() in self.options:
            listbox.selection_set(self.options.index(self.variable.get()))

        def choose(_choice: tk.Event) -> None:
            selection = listbox.curselection()
            if selection:
                self.variable.set(listbox.get(selection[0]))
                self._draw()
            popup.destroy()

        listbox.bind("<ButtonRelease-1>", choose)
        popup.bind("<Escape>", lambda _event: popup.destroy())
        popup.bind("<FocusOut>", lambda _event: popup.destroy())
        popup.focus_force()


class RoundedScrollbar(tk.Canvas):
    def __init__(self, parent: tk.Misc, command: object, track: str, thumb: str, thumb_hover: str, **kwargs: object) -> None:
        super().__init__(parent, width=dpi_value(parent, 14), background=parent.cget("background"), highlightthickness=0, borderwidth=0, **kwargs)
        self.command = command
        self.track = track
        self.thumb = thumb
        self.thumb_hover = thumb_hover
        self.first = 0.0
        self.last = 1.0
        self.drag_offset = 0.0
        self.hovered = False
        self.scale = float(parent.winfo_fpixels("1i")) / 96.0
        self.bind("<Enter>", lambda _event: self._set_hover(True))
        self.bind("<Leave>", lambda _event: self._set_hover(False))
        self.bind("<Button-1>", self._press)
        self.bind("<B1-Motion>", self._drag)
        self.bind("<Configure>", lambda _event: self._draw())
        self._draw()

    def _set_hover(self, value: bool) -> None:
        self.hovered = value
        self._draw()

    def set(self, first: str, last: str) -> None:
        self.first = float(first)
        self.last = float(last)
        self._draw()

    def _thumb_bounds(self) -> tuple[float, float]:
        inset = dpi_value(self, 4)
        height = max(1, self.winfo_height() - inset * 2)
        return inset + height * self.first, inset + height * self.last

    def _draw(self) -> None:
        self.delete("all")
        inset = dpi_value(self, 4)
        side = dpi_value(self, 4)
        height = max(2, self.winfo_height() - inset * 2)
        rounded_rect(self, side, inset, self.winfo_width() - side, height + inset, dpi_value(self, 3), self.track)
        top, bottom = self._thumb_bounds()
        rounded_rect(self, dpi_value(self, 3), top, self.winfo_width() - dpi_value(self, 3), max(top + dpi_value(self, 18), bottom), dpi_value(self, 4), self.thumb_hover if self.hovered else self.thumb)

    def _press(self, event: tk.Event) -> None:
        top, bottom = self._thumb_bounds()
        if top <= event.y <= bottom:
            self.drag_offset = event.y - top
            return
        inset = dpi_value(self, 4)
        fraction = min(1.0, max(0.0, (event.y - inset) / max(1, self.winfo_height() - inset * 2)))
        if callable(self.command):
            self.command("moveto", fraction)

    def _drag(self, event: tk.Event) -> None:
        inset = dpi_value(self, 4)
        height = max(1, self.winfo_height() - inset * 2)
        fraction = (event.y - self.drag_offset - inset) / height
        if callable(self.command):
            self.command("moveto", min(1.0, max(0.0, fraction)))


class RoundedDropZone(tk.Canvas):
    def __init__(self, parent: tk.Misc, command: object, fill: str, outline: str, foreground: str, muted: str, height: int = 82, **kwargs: object) -> None:
        super().__init__(parent, height=height, background=parent.cget("background"), highlightthickness=0, borderwidth=0, **kwargs)
        self.command = command
        self.fill = fill
        self.outline = outline
        self.foreground = foreground
        self.muted = muted
        self.bind("<Button-1>", self._click)
        self.bind("<Enter>", lambda _event: self._draw(hover=True))
        self.bind("<Leave>", lambda _event: self._draw(hover=False))
        self.bind("<Configure>", lambda _event: self._draw())
        self._draw()

    def _click(self, _event: tk.Event) -> None:
        if callable(self.command):
            self.command()

    def _draw(self, hover: bool = False) -> None:
        self.delete("all")
        fill = "#d7e4ff" if hover else self.fill
        rounded_rect(self, 1, 1, max(2, self.winfo_width() - 1), max(2, self.winfo_height() - 1), 18, fill, self.outline, 1)
        self.create_text(self.winfo_width() / 2, self.winfo_height() / 2 - 13, text="파일을 끌어다 놓으세요", fill=self.foreground, font=("Segoe UI", 11, "bold"))
        self.create_text(self.winfo_width() / 2, self.winfo_height() / 2 + 12, text="또는 클릭해서 여러 파일을 선택합니다", fill=self.muted, font=("Segoe UI", 9))


class TosunConverterApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title(f"{APP_NAME} {APP_VERSION}")
        self.root.geometry("1080x740")
        self.root.minsize(900, 620)
        self.files: list[Path] = []
        self.output_dir = tk.StringVar(value=str(Path.home() / "Desktop" / "Tosun Flux-Output"))
        self.target = tk.StringVar()
        self.status = tk.StringVar(value="파일을 추가해 주세요.")
        self._drop_hwnd: int | None = None
        self._drop_old_proc: int | None = None
        self._drop_proc: object | None = None
        self._drop_user32: object | None = None
        self._build_style()
        self._build_ui()
        self._install_drop_target()

    def _build_style(self) -> None:
        self.colors = {
            "background": "#c9dafa",
            "panel": "#eff5ff",
            "panel_deep": "#e1ebff",
            "line": "#ffffff",
            "text": "#24345f",
            "muted": "#6d7fab",
            "accent": "#8191ef",
            "accent_active": "#a7b4ff",
            "selection": "#9aaaf2",
        }
        self.root.configure(background=self.colors["background"])
        style = ttk.Style(self.root)
        style.theme_use("clam")
        style.configure("TFrame", background=self.colors["background"])
        style.configure(
            "Card.TFrame",
            background=self.colors["panel"],
            bordercolor=self.colors["line"],
            lightcolor=self.colors["line"],
            darkcolor=self.colors["panel_deep"],
            borderwidth=1,
            relief="solid",
        )
        style.configure("Title.TLabel", background=self.colors["background"], foreground=self.colors["text"], font=("Segoe UI", 24, "bold"))
        style.configure("Subtitle.TLabel", background=self.colors["background"], foreground=self.colors["muted"], font=("Segoe UI", 10))
        style.configure("CardTitle.TLabel", background=self.colors["panel"], foreground=self.colors["text"], font=("Segoe UI", 12, "bold"))
        style.configure("Muted.TLabel", background=self.colors["panel"], foreground=self.colors["muted"], font=("Segoe UI", 9))
        style.configure("Glass.TEntry", fieldbackground=self.colors["panel_deep"], foreground=self.colors["text"], insertcolor=self.colors["text"], bordercolor=self.colors["line"], lightcolor=self.colors["line"], darkcolor=self.colors["panel_deep"], padding=8)
        style.configure("Glass.TCombobox", fieldbackground=self.colors["panel_deep"], foreground=self.colors["text"], bordercolor=self.colors["line"], lightcolor=self.colors["line"], darkcolor=self.colors["panel_deep"], padding=7)
        style.map("Glass.TCombobox", fieldbackground=[("readonly", self.colors["panel_deep"])], foreground=[("readonly", self.colors["text"])])
        style.configure("TButton", background="#202d4d", foreground=self.colors["text"], bordercolor=self.colors["line"], lightcolor=self.colors["line"], darkcolor="#111a31", padding=(11, 8), font=("Segoe UI", 9, "bold"))
        style.map("TButton", background=[("active", "#2d3d66"), ("disabled", "#17213a")], foreground=[("disabled", "#617092")])
        style.configure("Accent.TButton", foreground="#10162c", background=self.colors["accent"], bordercolor=self.colors["accent"], lightcolor=self.colors["accent_active"], darkcolor="#617bd8", padding=(16, 11), font=("Segoe UI", 10, "bold"))
        style.map("Accent.TButton", background=[("active", self.colors["accent_active"]), ("disabled", "#34446f")], foreground=[("disabled", "#8290b4")])
        style.configure("TScrollbar", background="#1c2948", troughcolor=self.colors["panel_deep"], bordercolor=self.colors["panel_deep"], arrowcolor=self.colors["muted"])
        style.configure("Glass.Horizontal.TProgressbar", background=self.colors["accent"], troughcolor=self.colors["panel_deep"], bordercolor=self.colors["panel_deep"], lightcolor=self.colors["accent"], darkcolor=self.colors["accent"])

    def _build_ui(self) -> None:
        main = tk.Frame(self.root, background=self.colors["background"])
        main.pack(fill="both", expand=True)
        main.columnconfigure(0, weight=1)
        main.rowconfigure(2, weight=1)
        self._tosun_image = self._load_tosun_image()
        backdrop = tk.Canvas(main, background=self.colors["background"], highlightthickness=0, borderwidth=0)
        backdrop.place(relx=0, rely=0, relwidth=1, relheight=1)

        def draw_backdrop(event: tk.Event) -> None:
            backdrop.delete("decor")
            width, height = event.width, event.height
            backdrop.create_oval(-220, -190, 420, 420, fill="#b4c8f2", outline="", tags="decor")
            backdrop.create_oval(-130, -120, 330, 340, fill="#bfd3f8", outline="", tags="decor")
            backdrop.create_oval(width - 410, height - 340, width + 190, height + 260, fill="#c9c4f4", outline="", tags="decor")
            backdrop.create_oval(width - 290, height - 250, width + 95, height + 125, fill="#d6cdf8", outline="", tags="decor")
            for offset in range(-height, width, 42):
                backdrop.create_line(offset, height, offset + height, 0, fill="#c4d4f5", width=1, tags="decor")
            backdrop.create_oval(width - 118, 46, width - 114, 50, fill="#8e8dff", outline="", tags="decor")
            backdrop.create_oval(width - 92, 80, width - 88, 84, fill="#71bfe0", outline="", tags="decor")

        backdrop.bind("<Configure>", draw_backdrop)
        header = tk.Frame(main, background=self.colors["background"])
        header.grid(row=0, column=0, sticky="ew", padx=32, pady=(28, 0))
        tk.Label(header, text=APP_NAME, background=self.colors["background"], foreground=self.colors["text"], font=("Segoe UI", 25, "bold")).pack(side="left", anchor="w")
        RoundedButton(header, text=APP_VERSION, command=None, fill="#f4f7ff", hover="#ffffff", foreground="#7181d5", width=82, height=32, radius=13).pack(side="right", anchor="e", pady=(2, 0))
        tk.Label(main, text=f"{APP_DESCRIPTION}  ·  파일을 넣고, 형식을 고르고, 변환하세요.", background=self.colors["background"], foreground=self.colors["muted"], font=("Segoe UI", 10)).grid(row=1, column=0, sticky="w", padx=34, pady=(8, 22))

        body = tk.Frame(main, background=self.colors["background"])
        body.grid(row=2, column=0, sticky="nsew", padx=32)
        body.columnconfigure(0, weight=3)
        body.columnconfigure(1, weight=2)
        body.rowconfigure(0, weight=1)

        left = RoundedCard(body, fill=self.colors["panel"], outline=self.colors["line"], radius=24, padding=28)
        left.grid(row=0, column=0, sticky="nsew", padx=(0, 12))
        left_body = left.content
        left_body.rowconfigure(3, weight=1)
        left_body.columnconfigure(0, weight=1)
        ttk.Label(left_body, text="변환할 파일", style="CardTitle.TLabel").grid(row=0, column=0, sticky="w")
        drop_zone = RoundedDropZone(left_body, command=self.add_files, fill=self.colors["panel_deep"], outline="#ffffff", foreground=self.colors["text"], muted=self.colors["muted"], height=dpi_value(left_body, 82))
        drop_zone.grid(row=1, column=0, sticky="ew", pady=(14, 12))
        actions = tk.Frame(left_body, background=self.colors["panel"])
        actions.grid(row=2, column=0, sticky="ew", pady=(0, 10))
        RoundedButton(actions, text="파일 추가", command=self.add_files, fill="#8798ed", hover="#9faeff", foreground="#ffffff", width=108).pack(side="left")
        RoundedButton(actions, text="전체 비우기", command=self.clear_files, fill="#e1e9fc", hover="#d1ddfa", foreground="#6376aa", width=108).pack(side="left", padx=8)
        self.file_list = tk.Listbox(left_body, selectmode=tk.EXTENDED, activestyle="none", borderwidth=0, highlightthickness=1, highlightbackground="#ffffff", highlightcolor=self.colors["accent"], background=self.colors["panel_deep"], foreground=self.colors["text"], selectbackground=self.colors["selection"], selectforeground="#24345f", relief="flat", exportselection=False, font=("Segoe UI", 10))
        self.file_list.grid(row=3, column=0, sticky="nsew")
        scrollbar = RoundedScrollbar(left_body, command=self.file_list.yview, track="#c7d6f5", thumb="#91a3e9", thumb_hover="#768bdd")
        scrollbar.grid(row=3, column=1, sticky="ns", padx=(8, 0))
        self.file_list.configure(yscrollcommand=scrollbar.set)
        self.file_list.bind("<<ListboxSelect>>", lambda _event: self.update_targets())
        self.file_hint = ttk.Label(left_body, text="지원 형식은 파일을 선택하면 자동으로 안내됩니다.", style="Muted.TLabel")
        self.file_hint.grid(row=4, column=0, sticky="w", pady=(10, 0))

        right = RoundedCard(body, fill=self.colors["panel"], outline=self.colors["line"], radius=24, padding=28)
        right.grid(row=0, column=1, sticky="nsew")
        right_body = right.content
        right_body.columnconfigure(0, weight=1)
        right_body.rowconfigure(6, weight=1)
        ttk.Label(right_body, text="변환 설정", style="CardTitle.TLabel").grid(row=0, column=0, sticky="w")
        ttk.Label(right_body, text="출력 형식", style="Muted.TLabel").grid(row=1, column=0, sticky="w", pady=(14, 5))
        self.target_box = RoundedSelect(right_body, variable=self.target, fill=self.colors["panel_deep"], outline=self.colors["line"], foreground=self.colors["text"], muted=self.colors["muted"])
        self.target_box.grid(row=2, column=0, sticky="ew")
        ttk.Label(right_body, text="저장 위치", style="Muted.TLabel").grid(row=3, column=0, sticky="w", pady=(16, 5))
        output_row = tk.Frame(right_body, background=self.colors["panel"])
        output_row.grid(row=4, column=0, sticky="ew")
        output_row.columnconfigure(0, weight=1)
        RoundedEntry(output_row, variable=self.output_dir, fill=self.colors["panel_deep"], outline=self.colors["line"], foreground=self.colors["text"]).grid(row=0, column=0, sticky="ew")
        RoundedButton(output_row, text="폴더 선택", command=self.choose_output_dir, fill="#8798ed", hover="#9faeff", foreground="#ffffff", width=92).grid(row=0, column=1, padx=(8, 0))
        ttk.Label(right_body, text="원본은 그대로 두고 새 파일로 저장합니다.", style="Muted.TLabel", wraplength=300).grid(row=5, column=0, sticky="w", pady=(12, 0))
        self.convert_button = RoundedButton(right_body, text="변환 시작", command=self.start_conversion, fill=self.colors["accent"], hover=self.colors["accent_active"], foreground="#ffffff", width=160, height=46, radius=17)
        self.convert_button.grid(row=7, column=0, sticky="ew", pady=(18, 0))
        self.convert_button.set_enabled(False)

        bottom = RoundedCard(main, fill=self.colors["panel"], outline=self.colors["line"], radius=20, padding=22)
        bottom.grid(row=3, column=0, sticky="ew", padx=32, pady=(16, 28))
        bottom_body = bottom.content
        bottom_body.columnconfigure(0, weight=1)
        ttk.Label(bottom_body, textvariable=self.status, style="Muted.TLabel").grid(row=0, column=0, sticky="w")
        self.progress = ttk.Progressbar(bottom_body, mode="determinate", style="Glass.Horizontal.TProgressbar")
        self.progress.grid(row=1, column=0, sticky="ew", pady=(10, 0))
        self.log = tk.Text(bottom_body, height=4, state="disabled", borderwidth=0, background=self.colors["panel_deep"], foreground="#526895", insertbackground=self.colors["text"], selectbackground=self.colors["selection"], relief="flat", font=("Consolas", 9))
        self.log.grid(row=2, column=0, sticky="ew", pady=(10, 0))
        if self._tosun_image is not None:
            self.tosun_overlay = tk.Label(main, image=self._tosun_image, background=self.colors["background"], borderwidth=0, highlightthickness=0)
            self.tosun_overlay.place(relx=1.0, x=-120, y=28, anchor="ne")

    def _resource_path(self, *parts: str) -> Path:
        root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
        return root.joinpath(*parts)

    def _load_tosun_image(self) -> tk.PhotoImage | None:
        asset = self._resource_path("assets", "tosun-floating-v1.png")
        if not asset.is_file():
            return None
        try:
            return tk.PhotoImage(file=str(asset)).subsample(9, 9)
        except tk.TclError:
            return None

    def add_files(self) -> None:
        selected = filedialog.askopenfilenames(title="변환할 파일 선택", filetypes=[("모든 파일", "*.*")])
        self.add_paths(selected)

    def add_paths(self, names: object) -> None:
        if isinstance(names, (str, bytes)):
            names = [names]
        for name in names:
            path = Path(name)
            if path not in self.files:
                self.files.append(path)
        self.render_files()
        self.update_targets()

    def add_dropped_files(self, names: list[str]) -> None:
        self.add_paths(names)
        if names:
            self.status.set(f"{len(names)}개 파일을 추가했습니다.")

    def _install_drop_target(self) -> None:
        if os.name != "nt":
            return
        hwnd = int(self.root.winfo_id())
        user32 = ctypes.windll.user32
        shell32 = ctypes.windll.shell32
        callback_type = ctypes.WINFUNCTYPE(
            ctypes.c_ssize_t,
            wintypes.HWND,
            wintypes.UINT,
            wintypes.WPARAM,
            wintypes.LPARAM,
        )

        def window_proc(window: int, message: int, wparam: int, lparam: int) -> int:
            if message == WM_DROPFILES:
                hdrop = wintypes.HANDLE(wparam)
                count = shell32.DragQueryFileW(hdrop, 0xFFFFFFFF, None, 0)
                paths: list[str] = []
                for index in range(count):
                    length = shell32.DragQueryFileW(hdrop, index, None, 0)
                    buffer = ctypes.create_unicode_buffer(length + 1)
                    shell32.DragQueryFileW(hdrop, index, buffer, length + 1)
                    paths.append(buffer.value)
                shell32.DragFinish(hdrop)
                self.root.after(0, self.add_dropped_files, paths)
                return 0
            return user32.CallWindowProcW(ctypes.c_void_p(self._drop_old_proc), window, message, wparam, lparam)

        self._drop_user32 = user32
        self._drop_hwnd = hwnd
        self._drop_proc = callback_type(window_proc)
        user32.SetWindowLongPtrW.restype = ctypes.c_void_p
        user32.SetWindowLongPtrW.argtypes = [wintypes.HWND, ctypes.c_int, ctypes.c_void_p]
        user32.CallWindowProcW.restype = ctypes.c_ssize_t
        user32.CallWindowProcW.argtypes = [ctypes.c_void_p, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
        self._drop_old_proc = int(user32.SetWindowLongPtrW(hwnd, GWL_WNDPROC, ctypes.cast(self._drop_proc, ctypes.c_void_p)))
        shell32.DragAcceptFiles(hwnd, True)
        self.root.bind("<Destroy>", self._restore_drop_target, add="+")

    def _restore_drop_target(self, event: tk.Event) -> None:
        if event.widget is not self.root or not self._drop_hwnd or not self._drop_old_proc or not self._drop_user32:
            return
        ctypes.windll.shell32.DragAcceptFiles(self._drop_hwnd, False)
        self._drop_user32.SetWindowLongPtrW(self._drop_hwnd, GWL_WNDPROC, ctypes.c_void_p(self._drop_old_proc))
        self._drop_hwnd = None

    def clear_files(self) -> None:
        self.files.clear()
        self.render_files()
        self.update_targets()

    def render_files(self) -> None:
        self.file_list.delete(0, tk.END)
        for path in self.files:
            self.file_list.insert(tk.END, f"{path.name}   ·   {file_kind(path)}")
        if self.files:
            self.file_list.selection_set(0, tk.END)

    def update_targets(self) -> None:
        targets = common_targets(self.files)
        self.target_box.set_options([f".{target}" for target in targets])
        if targets:
            if self.target.get().lstrip(".") not in targets:
                self.target.set(f".{targets[0]}")
            self.file_hint.configure(text=f"{len(self.files)}개 파일 · 공통 변환 형식 {len(targets)}개")
            self.convert_button.set_enabled(True)
            return
        self.target.set("")
        self.file_hint.configure(text="선택한 파일에 공통으로 적용할 변환 형식이 없습니다.")
        self.convert_button.set_enabled(False)

    def choose_output_dir(self) -> None:
        selected = filedialog.askdirectory(title="저장 폴더 선택")
        if selected:
            self.output_dir.set(selected)

    def write_log(self, message: str) -> None:
        self.log.configure(state="normal")
        self.log.insert(tk.END, message + "\n")
        self.log.see(tk.END)
        self.log.configure(state="disabled")

    def start_conversion(self) -> None:
        if not self.files or not self.target.get():
            return
        output_dir = Path(self.output_dir.get().strip()).expanduser()
        if not output_dir:
            messagebox.showerror("저장 폴더 필요", "저장 폴더를 지정해 주세요.")
            return
        target = self.target.get().lstrip(".")
        self.convert_button.set_enabled(False)
        self.progress.configure(value=0, maximum=len(self.files))
        self.status.set("변환 중…")
        self.write_log(f"변환 시작: {len(self.files)}개 → .{target}")

        def worker() -> None:
            def progress(index: int, total: int, result: object, error: Exception | None) -> None:
                self.root.after(0, self.on_progress, index, total, result, error)

            results = convert_files(self.files, output_dir, target, progress)
            self.root.after(0, self.finish_conversion, len(results), len(self.files))

        threading.Thread(target=worker, daemon=True).start()

    def on_progress(self, index: int, total: int, result: object, error: Exception | None) -> None:
        self.progress.configure(value=index)
        if error:
            self.write_log(f"실패: {self.files[index - 1].name} — {error}")
        else:
            outputs = getattr(result, "outputs", ())
            self.write_log(f"완료: {self.files[index - 1].name} → {', '.join(path.name for path in outputs)}")
        self.status.set(f"변환 중… {index}/{total}")

    def finish_conversion(self, success_count: int, total: int) -> None:
        self.convert_button.set_enabled(True)
        if success_count == total:
            self.status.set(f"변환 완료 · {success_count}/{total}개")
            messagebox.showinfo("변환 완료", f"{success_count}개 파일을 변환했습니다.")
        else:
            self.status.set(f"변환 종료 · 성공 {success_count}/{total}개")
            messagebox.showwarning("일부 변환 실패", f"성공 {success_count}개, 실패 {total - success_count}개입니다. 로그를 확인해 주세요.")


def main() -> None:
    root = tk.Tk()
    root.tk.call("tk", "scaling", 96 / 72)
    TosunConverterApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
