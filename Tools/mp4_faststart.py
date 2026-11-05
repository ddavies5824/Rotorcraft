# Moves an MP4's index (its moov box) from the end of the file to the front, so a phone can start playing it as it downloads.
# iPhones in particular may refuse to load a video from a server when the index comes last. Blender writes it last, and has no
# setting for this. Nothing is re-encoded: the pictures and sound stay byte for byte the same.
#   python -I Tools\mp4_faststart.py <file.mp4> [more files]
import os
import struct
import sys

CONTAINERS = {b"trak", b"mdia", b"minf", b"stbl"}


def top_level(data):
    at = 0
    while at + 8 <= len(data):
        size, kind = struct.unpack(">I4s", data[at : at + 8])
        if size == 1:
            size = struct.unpack(">Q", data[at + 8 : at + 16])[0]
        elif size == 0:
            size = len(data) - at
        if size < 8:
            raise ValueError(f"bad box at {at}")
        yield kind, at, size
        at += size


def chunk_tables(moov, start, end):
    """Where each table of chunk offsets is inside the index."""
    at = start
    while at + 8 <= end:
        size, kind = struct.unpack(">I4s", moov[at : at + 8])
        if size < 8:
            raise ValueError(f"bad box in the index at {at}")
        if kind in CONTAINERS:
            yield from chunk_tables(moov, at + 8, at + size)
        elif kind in (b"stco", b"co64"):
            yield kind, at
        at += size


def first_chunks(data, moov_at, moov_size):
    moov = data[moov_at : moov_at + moov_size]
    out = []
    for kind, at in chunk_tables(moov, 8, moov_size):
        wide = kind == b"co64"
        offset = struct.unpack(">Q" if wide else ">I", moov[at + 16 : at + (24 if wide else 20)])[0]
        out.append(data[offset : offset + 64])
    return out


def run(path):
    data = open(path, "rb").read()
    boxes = list(top_level(data))
    moov_box = next(b for b in boxes if b[0] == b"moov")
    mdat_box = next(b for b in boxes if b[0] == b"mdat")
    if moov_box[1] < mdat_box[1]:
        print(f"{path}: the index is already at the front")
        return
    if moov_box[1] + moov_box[2] != len(data):
        raise ValueError("expected the index to be the last thing in the file")
    before = first_chunks(data, moov_box[1], moov_box[2])
    moov = bytearray(data[moov_box[1] : moov_box[1] + moov_box[2]])
    shift = len(moov)
    for kind, at in chunk_tables(moov, 8, len(moov)):
        count = struct.unpack(">I", moov[at + 12 : at + 16])[0]
        width, code = (8, ">Q") if kind == b"co64" else (4, ">I")
        for i in range(count):
            where = at + 16 + i * width
            value = struct.unpack(code, moov[where : where + width])[0] + shift
            if width == 4 and value >= 2 ** 32:
                raise ValueError("file too large for 32-bit chunk offsets")
            moov[where : where + width] = struct.pack(code, value)
    head = data[: mdat_box[1]]      # the file type box and any padding
    out = bytes(head) + bytes(moov) + data[mdat_box[1] : moov_box[1]]
    # the index must still point at exactly the same pictures and sound
    new_moov_at = len(head)
    after = first_chunks(out, new_moov_at, shift)
    if before != after or len(out) != len(data):
        raise ValueError("the moved index does not point at the same data")
    temp = path + ".faststart"
    with open(temp, "wb") as f:
        f.write(out)
    os.replace(temp, path)
    print(f"{path}: index moved to the front ({shift / 1024:.0f} KiB), {len(before)} tracks checked")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        run(p)
