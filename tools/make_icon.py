#!/usr/bin/env python3
"""
Genera el icono provisional de Zenith (Assets/zenith.ico) sin dependencias
externas: la marca es un vértice — la cima — sobre un cuadrado redondeado con
el degradado de la aplicación.

Uso:  python3 tools/make_icon.py
"""
import math
import struct
import zlib
from pathlib import Path

SIZES = [256, 128, 64, 48, 32, 16]
SUPERSAMPLE = 4

TOP = (0x7C, 0x93, 0xFF)
BOTTOM = (0x4C, 0x63, 0xD2)
MARK = (0xFF, 0xFF, 0xFF)

INSET = 0.045
RADIUS = 0.225
PEAK = [(0.255, 0.705), (0.5, 0.305), (0.745, 0.705)]
STROKE = 0.105


def rounded_rect_distance(x, y):
    """Distancia con signo a un cuadrado redondeado centrado en (0.5, 0.5)."""
    half = 0.5 - INSET - RADIUS
    dx = abs(x - 0.5) - half
    dy = abs(y - 0.5) - half
    outside = math.hypot(max(dx, 0.0), max(dy, 0.0))
    inside = min(max(dx, dy), 0.0)
    return outside + inside - RADIUS


def segment_distance(px, py, ax, ay, bx, by):
    vx, vy = bx - ax, by - ay
    wx, wy = px - ax, py - ay
    length = vx * vx + vy * vy
    t = 0.0 if length == 0 else max(0.0, min(1.0, (wx * vx + wy * vy) / length))
    return math.hypot(px - (ax + t * vx), py - (ay + t * vy))


def peak_distance(x, y):
    return min(
        segment_distance(x, y, *PEAK[0], *PEAK[1]),
        segment_distance(x, y, *PEAK[1], *PEAK[2]),
    )


def sample(x, y):
    """Devuelve (r, g, b, a) para un punto en coordenadas unitarias."""
    if rounded_rect_distance(x, y) > 0:
        return (0, 0, 0, 0)

    ratio = min(max((y - INSET) / (1 - 2 * INSET), 0.0), 1.0)
    color = tuple(round(TOP[i] + (BOTTOM[i] - TOP[i]) * ratio) for i in range(3))

    if peak_distance(x, y) <= STROKE / 2:
        color = MARK

    return (*color, 255)


def render(size):
    pixels = bytearray()
    step = 1.0 / (size * SUPERSAMPLE)

    for row in range(size):
        pixels.append(0)  # Filtro PNG "None" al inicio de cada línea.
        for column in range(size):
            r = g = b = a = 0
            for sy in range(SUPERSAMPLE):
                for sx in range(SUPERSAMPLE):
                    x = (column * SUPERSAMPLE + sx + 0.5) * step
                    y = (row * SUPERSAMPLE + sy + 0.5) * step
                    pr, pg, pb, pa = sample(x, y)
                    r += pr * pa
                    g += pg * pa
                    b += pb * pa
                    a += pa

            total = SUPERSAMPLE * SUPERSAMPLE
            if a == 0:
                pixels.extend((0, 0, 0, 0))
            else:
                pixels.extend((round(r / a), round(g / a), round(b / a), round(a / total)))

    return bytes(pixels)


def png_chunk(tag, payload):
    return (struct.pack(">I", len(payload)) + tag + payload
            + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))


def to_png(size, raw):
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + png_chunk(b"IHDR", header)
            + png_chunk(b"IDAT", zlib.compress(raw, 9))
            + png_chunk(b"IEND", b""))


def main():
    images = [(size, to_png(size, render(size))) for size in SIZES]

    directory = struct.pack("<HHH", 0, 1, len(images))
    offset = len(directory) + 16 * len(images)

    entries = b""
    for size, data in images:
        entries += struct.pack(
            "<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)

    output = Path(__file__).resolve().parent.parent / "src" / "Zenith.App" / "Assets" / "zenith.ico"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(directory + entries + b"".join(data for _, data in images))
    print(f"{output} ({output.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
