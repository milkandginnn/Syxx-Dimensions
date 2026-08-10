#!/usr/bin/env python3
"""Split DELTAS.md: move fixed → DELTAS-fixed.md, rejected → DELTAS-rejected.md.

Simple approach:
  1. Find all delta ID line numbers
  2. Compute each delta's "ownership zone" (the lines it owns)
  3. Classify as fixed/rejected/pending
  4. Remove fixed+rejected zones from DELTAS.md, write them to separate files
"""

import re
from pathlib import Path

FULL_RE = re.compile(r'^- \*\*\[([A-F]\d+)\]\*\*')
COMPACT_RE = re.compile(r'^\*\*\[([A-F]\d+)\]\*\*\s')
ANY_DELTA_RE = re.compile(r'\*\*\[([A-F]\d+)\]\*\*')

REJECTIONS = {
    'A031': ('A229', 'Cross-ref: "Same issue as A229. Remove one to avoid duplicate tracking"'),
    'A239': ('A237', '"Same as A237 (duplicate page leak-test.html:315)"'),
    'A240': ('A238', '"Same as A238 (duplicate page leak-test.html:322)"'),
    'A248': ('A247', '"Same as A247 (duplicate page)"'),
    'A250': ('A249', '"Same as A249 (duplicate page)"'),
    'A273': ('A267', '"Extends A267" — A267 Fix 2 already covers valid_domain()'),
    'B306': ('B287', 'Cross-ref: "B287 (same finding)"'),
    'B315': ('D297', 'Cross-ref: "D297 (same finding)"'),
    'B316': ('B286', 'Cross-ref: "B286 (same finding)"'),
    'C213': ('C192', 'Cross-ref: "C192 (same class)" — Portal warden PASSED'),
    'D143': ('D001', 'D001 cross-ref: "D143 is the duplicate compact finding"'),
    'D283': ('B274', 'Cross-ref: "B274 (same root cause)"'),
    'D316': ('D282', 'Cross-ref: "D282 (Linux same issue)"'),
    'D339': ('D287', 'Cross-ref: "D287 (same finding)"'),
    'D359': ('D286', 'Cross-ref: "D286 (same finding)"'),
    'D360': ('C202', 'Cross-ref: "C202 (same finding)"'),
    'D378': ('D316', 'Cross-ref: "D316 (same finding)"'),
    'D379': ('D308', 'Cross-ref: "D308 (same finding)"'),
    'E247': ('D304', 'Cross-ref: "D304 (same finding)"'),
    'E250': ('D326', 'Cross-ref: "D326 (same finding)"'),
    'E251': ('E246', 'Cross-ref: "E246 (same finding)"'),
    'E253': ('E229', 'Cross-ref: "E229 (same finding)"'),
    'F067': ('F066', '"Subsumed by F066 fix"'),
    'F129': ('F095', 'Cross-ref: "F095 (same finding)"'),
    'F130': ('F104', 'Cross-ref: "F104 (same finding)"'),
}


def main():
    root = Path('/Users/masterrabbit/Software/Syxx')
    src = root / 'DELTAS.md'
    fixed_path = root / 'DELTAS-fixed.md'
    rejected_path = root / 'DELTAS-rejected.md'

    lines = src.read_text().splitlines(keepends=True)
    total = len(lines)

    # --- Step 1: Find all delta ID lines ---
    deltas = []  # (delta_id, id_line_number)
    for i, line in enumerate(lines):
        m = FULL_RE.match(line) or COMPACT_RE.match(line)
        if m:
            deltas.append((m.group(1), i))

    print(f"Found {len(deltas)} delta entries")

    # --- Step 2: Compute ownership zones ---
    # Each delta owns lines from ownership_start to ownership_end (exclusive).
    # ownership_start = earliest of: ALREADY FIXED marker line, or up to 2 --- lines before ID
    # ownership_end = next delta's ownership_start, or end of file/summary

    summary_line = None
    for i, line in enumerate(lines):
        if line.strip().startswith('## Summary'):
            summary_line = i
            break

    zones = []
    for idx, (delta_id, id_line) in enumerate(deltas):
        # Look backward for ownership start
        start = id_line
        # Check for ALREADY FIXED marker (standalone, 1-3 lines before)
        for back in range(1, 4):
            check = id_line - back
            if check < 0:
                break
            if '**[ALREADY FIXED]**' in lines[check] and not ANY_DELTA_RE.search(lines[check]):
                start = check
                break
            # Also skip blank lines and --- before the marker
            if lines[check].strip() in ('', '---'):
                continue
            break

        # Include any --- lines immediately before start
        while start > 0 and lines[start - 1].strip() == '---':
            start -= 1

        zones.append((delta_id, id_line, start))

    # Compute end for each zone = next zone's start, or summary/file end
    zone_ranges = []
    for idx, (delta_id, id_line, start) in enumerate(zones):
        if idx + 1 < len(zones):
            end = zones[idx + 1][2]  # next delta's ownership_start
        else:
            end = summary_line if summary_line else total
        zone_ranges.append((delta_id, start, end, id_line))

    # --- Step 3: Classify ---
    fixed = []
    rejected = []
    pending = []
    removed_lines = set()

    for delta_id, start, end, id_line in zone_ranges:
        block = lines[start:end]
        is_fixed = False
        # Check for ALREADY FIXED in Status field (within the block)
        for bl in block[:15]:  # Status is typically in first 15 lines
            if '**ALREADY FIXED**' in bl and '**Status:**' in bl:
                is_fixed = True
                break
            if '**[ALREADY FIXED]**' in bl and not ANY_DELTA_RE.search(bl):
                is_fixed = True
                break

        if is_fixed:
            fixed.append((delta_id, block))
            for i in range(start, end):
                removed_lines.add(i)
        elif delta_id in REJECTIONS:
            rejected.append((delta_id, block))
            for i in range(start, end):
                removed_lines.add(i)
        else:
            pending.append((delta_id, block))

    def sort_key(item):
        d = item[0]
        return (d[0], int(d[1:]))

    fixed.sort(key=sort_key)
    rejected.sort(key=sort_key)

    # --- Step 4: Write output files ---

    # DELTAS-fixed.md
    out = ['# Syxx Deltas — Already Fixed\n\n']
    out.append('These deltas have been verified as fixed in the current codebase.\n')
    out.append('Moved from DELTAS.md on 2026-08-07.\n\n')
    out.append(f'**Total fixed: {len(fixed)}**\n\n')
    for delta_id, block in fixed:
        out.append('---\n\n')
        for bl in block:
            if '**[ALREADY FIXED]**' in bl and not ANY_DELTA_RE.search(bl):
                continue
            if bl.strip() == '---':
                continue
            out.append(bl)
        if not block[-1].endswith('\n'):
            out.append('\n')
        out.append('\n---\n\n')
    fixed_path.write_text(''.join(out))

    # DELTAS-rejected.md
    out = ['# Syxx Deltas — Rejected\n\n']
    out.append('These deltas were rejected as duplicates, subsumed by other deltas,\n')
    out.append('or otherwise invalid. Moved from DELTAS.md on 2026-08-07.\n\n')
    out.append(f'**Total rejected: {len(rejected)}**\n\n')
    out.append('| Delta | Duplicate Of | Reason |\n')
    out.append('|-------|-------------|--------|\n')
    for delta_id, _ in rejected:
        dup_of, reason = REJECTIONS[delta_id]
        out.append(f'| {delta_id} | {dup_of} | {reason} |\n')
    out.append('\n---\n\n')
    for delta_id, block in rejected:
        dup_of, reason = REJECTIONS[delta_id]
        out.append(f'## Rejected: {delta_id} (duplicate of {dup_of})\n\n')
        out.append(f'**Reason:** {reason}\n\n')
        out.append('### Original Entry\n\n')
        for bl in block:
            if bl.strip() == '---':
                continue
            out.append(bl)
        if not block[-1].endswith('\n'):
            out.append('\n')
        out.append('\n---\n\n')
    rejected_path.write_text(''.join(out))

    # Rebuild DELTAS.md without removed lines
    kept = [lines[i] for i in range(total) if i not in removed_lines]

    # Clean up: collapse 3+ consecutive blank lines to 2
    cleaned = []
    blank_count = 0
    for line in kept:
        if line.strip() == '':
            blank_count += 1
            if blank_count <= 2:
                cleaned.append(line)
        else:
            blank_count = 0
            cleaned.append(line)

    result = ''.join(cleaned)

    # Update summary counts if present
    if 'Total valid (open) deltas' in result:
        result = re.sub(
            r'Total valid \(open\) deltas: \*\*\d+\*\*',
            f'Total valid (open) deltas: **{len(pending)}**',
            result
        )
        result = re.sub(
            r'\*\*Total valid \(open\) deltas: \d+\*\*',
            f'**Total valid (open) deltas: {len(pending)}**',
            result
        )

    src.write_text(result)

    # --- Verify ---
    new_text = src.read_text()
    remaining = (len(re.findall(r'^- \*\*\[([A-F]\d+)\]\*\*', new_text, re.MULTILINE)) +
                 len(re.findall(r'^\*\*\[([A-F]\d+)\]\*\*\s', new_text, re.MULTILINE)))

    # --- Report ---
    print(f"\n{'='*60}")
    print(f"DELTAS-fixed.md:    {len(fixed)} deltas")
    print(f"DELTAS-rejected.md: {len(rejected)} deltas")
    print(f"DELTAS.md pending:  {len(pending)} deltas")
    print(f"Total accounted:    {len(fixed) + len(rejected) + len(pending)}")
    print(f"{'='*60}")
    print(f"\nFixed:    {', '.join(d[0] for d in fixed)}")
    print(f"Rejected: {', '.join(d[0] for d in rejected)}")
    print(f"\nVerification: {remaining} delta IDs in updated DELTAS.md (expected {len(pending)})")
    if remaining != len(pending):
        print(f"  ⚠ Mismatch of {abs(remaining - len(pending))} (may be due to ID mentions in text)")

    domain_counts = {}
    for delta_id, _ in pending:
        d = delta_id[0]
        domain_counts[d] = domain_counts.get(d, 0) + 1
    print(f"\nPending by domain:")
    for d in sorted(domain_counts):
        print(f"  {d}: {domain_counts[d]}")


if __name__ == '__main__':
    main()
