"""Generates src/Windows/Daemon/Assets/backthread.ico (16/32/48 px, 32bpp BGRA DIB) without third-party deps."""
import math
import struct
import sys

ACCENT = (0x25, 0x63, 0xEB)  # #2563EB
WHITE = (0xFF, 0xFF, 0xFF)


def pixel(x, y, n):
    cx = cy = (n - 1) / 2.0
    r = n / 2.0 - 0.5
    d = math.hypot(x - cx, y - cy)
    if d > r:
        return (0, 0, 0, 0)
    # Rounded accent disc with a white "thread" ring and centre dot.
    ring_outer, ring_inner = r * 0.62, r * 0.42
    if ring_inner <= d <= ring_outer and not (x > cx and abs(y - cy) < n * 0.09):
        return WHITE + (255,)
    if d <= r * 0.16:
        return WHITE + (255,)
    alpha = 255 if d < r - 0.8 else int(255 * max(0.0, r - d + 0.2))
    return ACCENT + (alpha,)


def dib(n):
    header = struct.pack('<IiiHHIIiiII', 40, n, n * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    rows = []
    for y in range(n - 1, -1, -1):  # bottom-up
        row = b''
        for x in range(n):
            r, g, b, a = pixel(x, y, n)
            row += bytes((b, g, r, a))
        rows.append(row)
    mask_row = b'\x00' * (((n + 31) // 32) * 4)
    return header + b''.join(rows) + mask_row * n


def main(out):
    sizes = [16, 32, 48]
    images = [dib(n) for n in sizes]
    data = struct.pack('<HHH', 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    for n, img in zip(sizes, images):
        data += struct.pack('<BBBBHHII', n, n, 0, 0, 1, 32, len(img), offset)
        offset += len(img)
    data += b''.join(images)
    with open(out, 'wb') as f:
        f.write(data)


if __name__ == '__main__':
    main(sys.argv[1])
