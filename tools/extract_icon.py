"""Copies the icon out of the game's Game.exe into an .ico file for the launcher.

Usage: python extract_icon.py <path to Game.exe> <output .ico>
Only needs to be run again if you change the game's icon.
"""
import struct
import sys

RT_ICON = 3
RT_GROUP_ICON = 14


def main(exe_path, out_path):
    data = open(exe_path, "rb").read()
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    num_sections = struct.unpack_from("<H", data, pe + 6)[0]
    opt_size = struct.unpack_from("<H", data, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", data, opt)[0]
    dirs = opt + (96 if magic == 0x10B else 112)
    res_rva = struct.unpack_from("<I", data, dirs + 2 * 8)[0]

    sections = []
    sec = opt + opt_size
    for i in range(num_sections):
        vsize, vaddr, rawsize, rawptr = struct.unpack_from("<IIII", data, sec + i * 40 + 8)
        sections.append((vaddr, max(vsize, rawsize), rawptr))

    def rva_to_off(rva):
        for vaddr, size, rawptr in sections:
            if vaddr <= rva < vaddr + size:
                return rva - vaddr + rawptr
        raise ValueError("RVA %x not in any section" % rva)

    root = rva_to_off(res_rva)

    def entries(dir_off):
        named, ids = struct.unpack_from("<HH", data, dir_off + 12)
        for i in range(named + ids):
            name, target = struct.unpack_from("<II", data, dir_off + 16 + i * 8)
            yield name, target

    def first_leaf(target):
        # Walk down to the first data entry (skips the language level).
        while target & 0x80000000:
            target = next(entries(root + (target & 0x7FFFFFFF)))[1]
        rva, size = struct.unpack_from("<II", data, root + target)
        off = rva_to_off(rva)
        return data[off:off + size]

    types = dict(entries(root))
    icons = {}
    for name, target in entries(root + (types[RT_ICON] & 0x7FFFFFFF)):
        icons[name] = first_leaf(target)
    group = first_leaf(next(entries(root + (types[RT_GROUP_ICON] & 0x7FFFFFFF)))[1])

    count = struct.unpack_from("<H", group, 4)[0]
    header = struct.pack("<HHH", 0, 1, count)
    dir_bytes = b""
    images = b""
    offset = 6 + 16 * count
    for i in range(count):
        w, h, colors, res, planes, bits, size, icon_id = struct.unpack_from("<BBBBHHIH", group, 6 + i * 14)
        img = icons[icon_id]
        dir_bytes += struct.pack("<BBBBHHII", w, h, colors, res, planes, bits, len(img), offset)
        images += img
        offset += len(img)
    open(out_path, "wb").write(header + dir_bytes + images)
    print("Wrote %s with %d image(s)" % (out_path, count))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
