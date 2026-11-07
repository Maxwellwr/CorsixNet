"""Generates the RNC test fixtures in this folder.

Each fixture is a pair: NAME.bin holds the original bytes and NAME.rnc the same
bytes packed with RNC method 1. Rnc tests check that decompressing NAME.rnc
gives NAME.bin exactly.

The packer is bitplane/propack (https://github.com/bitplane/propack, WTFPL),
used only as a tool to produce these files. It is not a build dependency and
none of its code is part of CorsixNet. To regenerate:

    python -m venv .venv
    .venv/Scripts/pip install propack==0.3.1   (or .venv/bin/pip on Linux and macOS)
    .venv/Scripts/python generate.py
"""

from pathlib import Path

from propack import pack, unpack

HERE = Path(__file__).resolve().parent

# propack packs in blocks of 0x3000 bytes; "mixed" spans three of them.
BLOCK_SIZE = 0x3000


def lcg_bytes(count: int, seed: int) -> bytes:
    """Deterministic pseudo-random bytes (32-bit LCG, high byte of each state)."""
    state = seed
    out = bytearray()
    for _ in range(count):
        state = (state * 1664525 + 1013904223) & 0xFFFFFFFF
        out.append(state >> 24)
    return bytes(out)


def mixed(size: int) -> bytes:
    """Alternating incompressible and highly repetitive runs."""
    phrase = b"Patients are advised not to die in the corridors. "
    out = bytearray()
    seed = 1
    while len(out) < size:
        out += lcg_bytes(700, seed)
        out += (phrase * 20)[:900]
        seed += 1
    return bytes(out[:size])


FIXTURES = {
    # Too short to compress well: mostly literal runs.
    "short": b"Theme Hospital",
    # Long back-references, including overlapping copies of 1- and 2-byte patterns.
    "repeats": b"AB" * 300 + bytes(400) + b"xyz" * 100 + b"end",
    # Literals and matches across several packing blocks.
    "mixed": mixed(BLOCK_SIZE * 2 + 0x1800),
}


def main() -> None:
    for name, data in FIXTURES.items():
        packed = pack(data, method=1)
        if unpack(packed) != data:
            raise SystemExit(f"{name}: round trip through propack failed")
        (HERE / f"{name}.bin").write_bytes(data)
        (HERE / f"{name}.rnc").write_bytes(packed)
        print(f"{name}: {len(data)} -> {len(packed)} bytes")


if __name__ == "__main__":
    main()
