#!/usr/bin/env python3
"""Generate a byte-pattern signature for a function identified by a string it references.

Works on both Linux ELF shared objects (libengine2.so) and Windows PE DLLs
(engine2.dll). Auto-detects the format from the file magic.

Usage:
    sigmaker.py <binary> "<string>" [--len N] [--wild-imm]

Strategy:
  1. Find <string> bytes in a read-only data section, compute its address.
  2. Disassemble executable sections, find instructions whose RIP-relative
     memory operand resolves to that string address (the xref sites).
  3. Find the enclosing function's start:
       - ELF: .eh_frame FDE ranges (often incomplete) then a call-target fallback.
       - PE : .pdata RUNTIME_FUNCTION ranges (complete) then a call-target fallback.
     The candidate is validated by disassembling forward until it lands exactly
     on the xref, proving correct instruction alignment.
  4. Emit an IDA-style pattern, wildcarding bytes that shift between builds
     (RIP-relative displacements, branch targets, the `sub rsp` frame size),
     growing until the pattern is unique in the binary.

Requires: capstone, pyelftools (ELF), pefile (PE). Install in a venv:
    python3 -m venv venv && ./venv/bin/pip install capstone pyelftools pefile
"""
import argparse
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from capstone.x86 import (
    X86_REG_RIP, X86_OP_MEM, X86_OP_IMM, X86_OP_REG,
    X86_INS_CALL, X86_REG_RSP, X86_REG_ESP,
)

IMAGE_SCN_MEM_EXECUTE = 0x20000000


class Binary:
    """Format-agnostic view: sections + function-boundary table."""
    def __init__(self, sections, func_ranges, fmt, primary_of=None):
        self.sections = sections        # list of (name, vaddr, data, is_exec)
        self.func_ranges = func_ranges  # list of (lo, hi) virtual addresses
        self.fmt = fmt
        # PE only: maps a chunk's start vaddr -> its function's primary entry
        # (MSVC splits large functions into multiple RUNTIME_FUNCTIONs; the one
        # containing an xref is often a continuation chunk, not the real entry).
        self.primary_of = primary_of or {}


def load_elf(path):
    from elftools.elf.elffile import ELFFile
    elf = ELFFile(open(path, "rb"))
    SHF_EXECINSTR = 0x4
    sections = []
    for s in elf.iter_sections():
        h = s.header
        if h["sh_addr"] and h["sh_type"] != "SHT_NOBITS":
            sections.append((s.name, h["sh_addr"], s.data(), bool(h["sh_flags"] & SHF_EXECINSTR)))
    ranges = []
    try:
        dw = elf.get_dwarf_info()
        for e in dw.EH_CFI_entries():
            if not hasattr(e, "header"):
                continue
            h = e.header
            if "initial_location" in h and "address_range" in h:
                ranges.append((h["initial_location"], h["initial_location"] + h["address_range"]))
    except Exception:
        pass
    return Binary(sections, ranges, "elf")


def load_pe(path):
    import pefile
    pe = pefile.PE(path, fast_load=True)
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXCEPTION"]])
    base = pe.OPTIONAL_HEADER.ImageBase
    sections = []
    for s in pe.sections:
        name = s.Name.rstrip(b"\x00").decode("latin-1")
        is_exec = bool(s.Characteristics & IMAGE_SCN_MEM_EXECUTE)
        sections.append((name, base + s.VirtualAddress, s.get_data(), is_exec))

    UNW_FLAG_CHAININFO = 0x4

    def primary_entry(begin_rva, unwind_rva):
        """Follow UNWIND_INFO chain-info back to the function's real entry RVA."""
        for _ in range(16):  # guard against cycles
            info = pe.get_data(unwind_rva, 4)
            flags = info[0] >> 3
            n_codes = info[2]
            if not (flags & UNW_FLAG_CHAININFO):
                return begin_rva
            # chained RUNTIME_FUNCTION sits after the unwind-code array
            # (n_codes slots of 2 bytes each, padded to an even count)
            off = 4 + ((n_codes + 1) & ~1) * 2
            begin_rva, _end, unwind_rva = struct.unpack("<III", pe.get_data(unwind_rva + off, 12))
        return begin_rva

    ranges = []
    primary_of = {}
    for entry in getattr(pe, "DIRECTORY_ENTRY_EXCEPTION", []):
        st = entry.struct
        lo = base + st.BeginAddress
        ranges.append((lo, base + st.EndAddress))
        try:
            primary_of[lo] = base + primary_entry(st.BeginAddress, st.UnwindData)
        except Exception:
            primary_of[lo] = lo
    return Binary(sections, ranges, "pe", primary_of)


def load(path):
    with open(path, "rb") as f:
        magic = f.read(2)
    if magic == b"\x7fE":
        return load_elf(path)
    if magic == b"MZ":
        return load_pe(path)
    raise SystemExit("Unrecognized file format (not ELF or PE): " + path)


def exec_sections(b):
    return [(n, a, d) for (n, a, d, ex) in b.sections if ex]


def find_string_vaddr(b, text):
    """Locate the string, trying both ASCII/UTF-8 and UTF-16LE encodings.
    Windows PE binaries often store wide (UTF-16) string literals."""
    variants = [("ascii", text.encode("latin-1", "ignore")),
                ("utf-16le", text.encode("utf-16-le"))]
    hits = []
    for enc, needle in variants:
        if not needle:
            continue
        for name, addr, data, ex in b.sections:
            start = 0
            while True:
                i = data.find(needle, start)
                if i < 0:
                    break
                hits.append((name, addr + i, enc))
                start = i + 1
    return hits


def find_xrefs(b, target_vaddr):
    """Find code that RIP-relative-references target_vaddr.

    Byte-scans for `REX + {lea 8D | mov 8B} + modrm(RIP) + disp32`, the forms used
    to load a data-section address. This is alignment-independent, unlike a linear
    capstone sweep which desyncs on large stripped .text and can miss the site."""
    xrefs = []
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    for name, addr, data in exec_sections(b):
        n = len(data)
        for i in range(n - 7):
            # REX.W-family prefix, 8D/8B opcode, modrm mod=00 rm=101 (RIP+disp32)
            if 0x48 <= data[i] <= 0x4F and data[i + 1] in (0x8D, 0x8B) and (data[i + 2] & 0xC7) == 0x05:
                disp = struct.unpack_from("<i", data, i + 3)[0]
                if addr + i + 7 + disp == target_vaddr:
                    va = addr + i
                    insn = next(md.disasm(data[i:i + 7], va), None)
                    txt = (insn.mnemonic + " " + insn.op_str) if insn else "lea/mov [rip]"
                    xrefs.append((va, txt))
    return xrefs


def call_targets(b):
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    targets = set()
    for name, addr, data in exec_sections(b):
        for insn in md.disasm(data, addr):
            if insn.id == X86_INS_CALL and insn.operands and insn.operands[0].type == X86_OP_IMM:
                targets.add(insn.operands[0].imm)
    return targets


def bytes_at(b, vaddr, n):
    for name, addr, data, ex in b.sections:
        if addr <= vaddr < addr + len(data):
            off = vaddr - addr
            return data[off:off + n]
    return None


def enclosing_range(ranges, xref):
    """Innermost function range containing xref (largest lo <= xref < hi)."""
    best = None
    for lo, hi in ranges:
        if lo <= xref < hi and (best is None or lo > best[0]):
            best = (lo, hi)
    return best


def find_function_start(b, targets, xref):
    """Prefer the unwind-table entry (authoritative on PE via .pdata + chain-info);
    otherwise fall back to the nearest direct-call target, validated by
    disassembling forward until it lands exactly on the xref."""
    r = enclosing_range(b.func_ranges, xref)
    if r:
        # resolve continuation chunks to the real function entry (PE); identity on ELF
        start = b.primary_of.get(r[0], r[0])
        return start, ("pdata" if b.fmt == "pe" else "eh_frame")

    md = Cs(CS_ARCH_X86, CS_MODE_64)
    for cand in sorted((t for t in targets if t <= xref), reverse=True):
        raw = bytes_at(b, cand, xref - cand + 16)
        if raw is None:
            continue
        for insn in md.disasm(raw, cand):
            if insn.address == xref:
                return cand, "call-target"
            if insn.address > xref:
                break
    return None, None


def make_pattern(b, func_start, max_len, wild_imm):
    raw = bytes_at(b, func_start, max_len + 16)
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    tokens = []          # (byte, is_wild)
    consumed = 0
    for insn in md.disasm(raw, func_start):
        ins_bytes = list(insn.bytes)
        wild = [False] * len(ins_bytes)
        enc = insn.encoding

        rip_rel = any(op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP
                      for op in insn.operands)
        if rip_rel and enc.disp_offset and enc.disp_size:
            for k in range(enc.disp_offset, enc.disp_offset + enc.disp_size):
                if k < len(wild):
                    wild[k] = True

        has_imm = any(op.type == X86_OP_IMM for op in insn.operands)
        touches_sp = any(op.type == X86_OP_REG and op.reg in (X86_REG_RSP, X86_REG_ESP)
                         for op in insn.operands)
        if has_imm and enc.imm_offset and enc.imm_size:
            is_branch = insn.mnemonic.startswith(("call", "jmp", "j", "loop"))
            is_frame = insn.mnemonic in ("sub", "add") and touches_sp
            if is_branch or is_frame or wild_imm:
                for k in range(enc.imm_offset, enc.imm_offset + enc.imm_size):
                    if k < len(wild):
                        wild[k] = True

        for by, w in zip(ins_bytes, wild):
            tokens.append((by, w))
        consumed += len(ins_bytes)
        if consumed >= max_len:
            break
    return tokens


def pattern_str(tokens):
    return " ".join("?" if w else "%02X" % by for by, w in tokens)


def count_matches(b, tokens):
    total = 0
    plen = len(tokens)
    for name, addr, data in exec_sections(b):
        n = len(data)
        i = 0
        while i <= n - plen:
            ok = True
            for j, (by, w) in enumerate(tokens):
                if not w and data[i + j] != by:
                    ok = False
                    break
            if ok:
                total += 1
            i += 1
    return total


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("binary")
    ap.add_argument("string")
    ap.add_argument("--len", type=int, default=32)
    ap.add_argument("--wild-imm", action="store_true",
                    help="also wildcard non-branch/non-frame immediates")
    args = ap.parse_args()

    text = args.string.encode().decode("unicode_escape")  # resolve \n, \t, ...
    b = load(args.binary)
    print("format: %s" % b.fmt.upper())

    shits = find_string_vaddr(b, text)
    if not shits:
        print("String not found:", args.string)
        return
    print("String found at:")
    for name, va, enc in shits:
        print("  %-16s 0x%x  (%s)" % (name, va, enc))
    sva = shits[0][1]

    xrefs = find_xrefs(b, sva)
    print("\nXRefs to string (%d):" % len(xrefs))
    for a, txt in xrefs:
        print("  0x%x   %s" % (a, txt))
    if not xrefs:
        print("No code xref found (string may be referenced indirectly).")
        return

    targets = call_targets(b)
    starts = {}
    for a, txt in xrefs:
        fs, how = find_function_start(b, targets, a)
        if fs is not None:
            starts.setdefault(fs, how)
    print("\nEnclosing function start(s):")
    for fs, how in starts.items():
        print("  0x%x   (via %s)" % (fs, how))

    for fs in starts:
        print("\n=== Function @ 0x%x ===" % fs)
        raw = bytes_at(b, fs, args.len)
        print("raw  : " + " ".join("%02X" % by for by in raw))

        min_toks = None
        for L in range(8, args.len + 1, 2):
            toks = make_pattern(b, fs, L, args.wild_imm)
            if count_matches(b, toks) == 1:
                min_toks = toks
                break
        full_toks = make_pattern(b, fs, args.len, args.wild_imm)
        full_n = count_matches(b, full_toks)

        if min_toks:
            print("min  : %s   (unique, %d bytes)" % (pattern_str(min_toks), len(min_toks)))
        else:
            print("min  : (not unique within %d bytes)" % args.len)
        print("full : %s   (%d bytes, %d match%s)"
              % (pattern_str(full_toks), len(full_toks), full_n, "" if full_n == 1 else "es"))

        chosen = pattern_str(full_toks if full_n == 1 else (min_toks or full_toks))
        branch = "Linux" if b.fmt == "elf" else "Windows"
        print("\n%s signature:\n    %s" % (branch, chosen))


if __name__ == "__main__":
    main()
