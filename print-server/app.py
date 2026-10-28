"""Shelfie print server: renders QR labels and prints them on a Brother P-touch.

Runs on the Raspberry Pi next to the printer, reachable only over Tailscale.

    GET  /health          printer and tape status
    POST /labels/preview  label as PNG, nothing printed
    POST /labels          render and print

Label layout: QR code on the left (if a URL is given), a bold title and an
optional subtitle on the right. The label height is the printable width of the
loaded tape, as reported by the printer.
"""

from __future__ import annotations

import io
import os
import re
import subprocess
import tempfile
import threading
from dataclasses import dataclass
from pathlib import Path

import segno
from fastapi import FastAPI, HTTPException, Query, Response
from PIL import Image, ImageDraw, ImageFont
from pydantic import BaseModel, Field

PTOUCH = os.environ.get("PTOUCH_PRINT", "ptouch-print")
FONT_BOLD = os.environ.get("LABEL_FONT_BOLD", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf")
FONT_REGULAR = os.environ.get("LABEL_FONT", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
MAX_TEXT_PX = int(os.environ.get("LABEL_MAX_TEXT_PX", "640"))  # ~90 mm at 180 dpi
DPI = 180  # P700 print head
DEFAULT_TAPE_PX = 128  # 24 mm tape on a P700, used for previews without a printer

app = FastAPI(title="Shelfie print server")
printer_lock = threading.Lock()  # one job at a time; the printer has no queue


class Label(BaseModel):
    title: str = Field(min_length=1, max_length=120, examples=["ESP32-C3 SuperMini"])
    subtitle: str | None = Field(default=None, max_length=120, examples=["Box 4"])
    url: str | None = Field(default=None, max_length=500, description="encoded in the QR code")
    copies: int = Field(default=1, ge=1, le=20)


@dataclass
class PrinterInfo:
    tape_px: int
    media_width_mm: int | None
    tape_color: str | None
    raw: str


class PrinterError(Exception):
    pass


def run_ptouch(*args: str, timeout: int = 60) -> str:
    try:
        proc = subprocess.run([PTOUCH, *args], capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired as e:
        raise PrinterError(f"ptouch-print timed out after {timeout} s") from e
    except OSError as e:
        raise PrinterError(f"Could not run {PTOUCH}: {e}") from e
    output = (proc.stdout + proc.stderr).strip()
    if proc.returncode != 0:
        raise PrinterError(output or f"ptouch-print exited with {proc.returncode}")
    return output


def printer_info() -> PrinterInfo:
    raw = run_ptouch("--info", timeout=15)
    tape = re.search(r"maximum printing width for this tape is (\d+)\s*px", raw)
    if not tape:
        raise PrinterError(f"Could not read tape width from: {raw}")
    width = re.search(r"media width = (\d+) mm", raw)
    color = re.search(r"tape color = \S+ \(([^)]*)\)", raw)
    return PrinterInfo(
        tape_px=int(tape.group(1)),
        media_width_mm=int(width.group(1)) if width else None,
        tape_color=color.group(1) if color else None,
        raw=raw,
    )


def line_height(font: ImageFont.FreeTypeFont) -> int:
    """Height from the tallest ascender to the lowest descender."""
    _, top, _, bottom = font.getbbox("Ågjy")
    return bottom - top


def fit_font(path: str, max_height: int) -> ImageFont.FreeTypeFont:
    """Largest font whose line height fits max_height."""
    size = max_height
    while size > 6:
        font = ImageFont.truetype(path, size)
        if line_height(font) <= max_height:
            return font
        size -= 1
    return ImageFont.truetype(path, 6)


def truncate(text: str, font: ImageFont.FreeTypeFont, max_px: int) -> str:
    if font.getlength(text) <= max_px:
        return text
    while text and font.getlength(text + "…") > max_px:
        text = text[:-1]
    return text.rstrip() + "…"


def render(label: Label, tape_px: int) -> Image.Image:
    """Render the label as a 1-bit image, `tape_px` high."""
    margin = max(2, tape_px // 16)
    inner = tape_px - 2 * margin

    qr_img = None
    if label.url:
        # Level L keeps the symbol small on narrow tape; segno raises the level
        # by itself when it fits in the same size.
        qr = segno.make(label.url, error="l", micro=False)
        modules = qr.symbol_size(border=0)[0]
        scale = inner // modules
        if scale < 1:
            raise HTTPException(422, f"Tape too narrow for this QR code ({modules} modules, {inner} px)")
        buf = io.BytesIO()
        qr.save(buf, kind="png", scale=scale, border=0)
        qr_img = Image.open(io.BytesIO(buf.getvalue())).convert("L")

    if label.subtitle:
        title_font = fit_font(FONT_BOLD, int(inner * 0.55))
        sub_font = fit_font(FONT_REGULAR, int(inner * 0.38))
    else:
        title_font = fit_font(FONT_BOLD, int(inner * 0.75))
        sub_font = None

    title = truncate(label.title, title_font, MAX_TEXT_PX)
    subtitle = truncate(label.subtitle, sub_font, MAX_TEXT_PX) if sub_font else None
    text_w = int(max(title_font.getlength(title), sub_font.getlength(subtitle) if sub_font else 0))

    qr_w = qr_img.width if qr_img else 0
    gap = margin * 2 if qr_img else 0
    width = margin + qr_w + gap + text_w + margin

    img = Image.new("L", (width, tape_px), 255)
    if qr_img:
        img.paste(qr_img, (margin, (tape_px - qr_img.height) // 2))

    draw = ImageDraw.Draw(img)
    x = margin + qr_w + gap
    lines = [(title, title_font)] + ([(subtitle, sub_font)] if subtitle else [])
    heights = [line_height(f) for _, f in lines]
    spacing = (inner - sum(heights)) // (len(lines) + 1)
    y = margin + spacing
    for (text, font), h in zip(lines, heights):
        draw.text((x, y - font.getbbox("Ågjy")[1]), text, font=font, fill=0)
        y += h + spacing

    return img.point(lambda v: 0 if v < 128 else 255).convert("1")


def to_ptouch_png(img: Image.Image) -> bytes:
    """2-colour palette PNG as ptouch-print expects: index 0 white, index 1 black."""
    pal = Image.new("P", img.size, 0)
    pal.putpalette([255, 255, 255, 0, 0, 0])
    pal.paste(1, mask=img.point(lambda v: 255 if v == 0 else 0).convert("1"))
    buf = io.BytesIO()
    pal.save(buf, format="PNG", bits=1)
    return buf.getvalue()


@app.get("/health")
def health():
    try:
        info = printer_info()
    except PrinterError as e:
        raise HTTPException(503, f"Printer not ready: {e}")
    return {"printer": "ready", "tape_px": info.tape_px, "media_width_mm": info.media_width_mm, "tape_color": info.tape_color}


@app.post("/labels/preview", responses={200: {"content": {"image/png": {}}}})
def preview(label: Label, tape_px: int | None = Query(None, ge=16, le=128)):
    if tape_px is None:
        try:
            tape_px = printer_info().tape_px
        except PrinterError:
            tape_px = DEFAULT_TAPE_PX
    return Response(to_ptouch_png(render(label, tape_px)), media_type="image/png")


@app.post("/labels")
def print_label(label: Label):
    with printer_lock:
        try:
            info = printer_info()
            img = render(label, info.tape_px)
            with tempfile.TemporaryDirectory() as tmp:
                path = Path(tmp) / "label.png"
                path.write_bytes(to_ptouch_png(img))
                run_ptouch("--image", str(path), "--copies", str(label.copies))
        except PrinterError as e:
            raise HTTPException(503, f"Printing failed: {e}")
    # Printed length only; the printer adds its own feed margins.
    return {"printed": label.copies, "label_mm": round(img.width / DPI * 25.4, 1)}
