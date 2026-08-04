#!/usr/bin/env python3
"""
audit-deltas.py — extract FIXED and REJECTED deltas from DELTAS.md into
their companion files, and clean up incorrectly placed entries in
DELTAS-rejected.md.

Usage:
    python3 scripts/audit-deltas.py
"""
import re
import os
import shutil

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DELTAS_PATH = os.path.join(BASE, 'DELTAS.md')
FIXED_PATH = os.path.join(BASE, 'DELTAS-fixed.md')
REJECTED_PATH = os.path.join(BASE, 'DELTAS-rejected.md')

# --- Classification sets (authoritative, verified by human + source inspection) ---

FIXED_IDS = {
    'D118', 'D119', 'D120', 'D121', 'D124', 'D125', 'D126', 'D127',
    'C139', 'C140', 'C141', 'C151', 'C156',
    'SRV-101', 'B251', 'B254', 'E162',
}

REJECTED_IDS = {
    'C099',  # SUPERSEDED 2026-07-30 BY C151
    'D009',  # False positive — NSNumber as? Int64 works since SE-0170
    'D010',  # Basis factually wrong; proposed fix would regress
}

REMOVE_FROM_REJECTED = {'B002', 'B026'}


def extract_delta_id(line):
    """Extract delta ID from a header line, handling multiple formats."""
    # **[XXXX]** or **[XXXX] [FIXED ...]** or - **[XXXX]**
    m = re.search(r'\*\*\[([A-Z]+-?\d+)\]', line)
    if m:
        return m.group(1)
    return None


def classify_by_id(delta_id):
    """Classify purely by delta ID."""
    if delta_id in FIXED_IDS:
        return 'fixed'
    if delta_id in REJECTED_IDS:
        return 'rejected'
    return 'keep'


def format_fixed_entry(block_lines):
    """Format a delta block for DELTAS-fixed.md."""
    # Strip leading/trailing separators, blanks, and Source lines
    while block_lines and (block_lines[0].strip() in ('---', '') or
                           block_lines[0].strip().startswith('- **Source:**')):
        block_lines.pop(0)
    while block_lines and (block_lines[-1].strip() in ('---', '') or
                           block_lines[-1].strip().startswith('- **Source:**')):
        block_lines.pop()
    return '\n'.join(block_lines)


def format_rejected_entry(block_lines, delta_id):
    """Format a delta block for DELTAS-rejected.md with rejection metadata."""
    while block_lines and (block_lines[0].strip() in ('---', '') or
                           block_lines[0].strip().startswith('- **Source:**')):
        block_lines.pop(0)
    while block_lines and (block_lines[-1].strip() in ('---', '') or
                           block_lines[-1].strip().startswith('- **Source:**')):
        block_lines.pop()

    text = '\n'.join(block_lines)

    rejection_reasons = {
        'C099': ('Superseded by C151 on 2026-07-30. LAN diff MAC-keyed defect '
                 'consolidated into the C151 fix (LAN diff identity uses '
                 'interface+MAC+IP). Retained for reproduction evidence only.'),
        'D009': ('False positive. `NSNumber as? Int64` works correctly since '
                 'SE-0170 (Swift 3.0). All three peer reviewers '
                 '(syxx-researcher, Codex, Claude) independently debunked the '
                 'claim and recommended archival.'),
        'D010': ('Basis factually wrong. The license key is sent in the request '
                 'header (not query string), and the proposed fix would regress '
                 'security. Platform parity already resolved correctly — both '
                 'platforms now send the key. All three peer reviewers '
                 'recommended archival.'),
    }

    if '- **Rejection reason:**' not in text:
        reason = rejection_reasons.get(delta_id, 'See peer review comments.')
        text += f'\n- **Rejection reason:**  {reason}'
    if '- **Adjudicated by / date:**' not in text:
        text += f'\n- **Adjudicated by / date:** multi-tool consensus / 2026-07-30'

    return text


def remove_from_rejected(rejected_text, ids_to_remove):
    """Remove specific delta entries from DELTAS-rejected.md."""
    lines = rejected_text.split('\n')
    result = []
    i = 0
    while i < len(lines):
        line = lines[i]
        should_remove = False
        for rid in ids_to_remove:
            if re.search(rf'\*\*\[{rid}\]', line):
                should_remove = True
                break

        if should_remove:
            # Remove preceding --- and blank lines
            while result and result[-1].strip() in ('---', ''):
                result.pop()
            # Skip forward past this entry's closing ---
            i += 1
            # Find the end of this entry (next ---\n--- or next delta header)
            depth = 0
            while i < len(lines):
                if lines[i].strip() == '---':
                    depth += 1
                    if depth >= 2:
                        i += 1
                        break
                elif re.match(r'^(- )?\*\*\[', lines[i]):
                    break
                else:
                    if lines[i].strip() != '':
                        depth = 0  # reset on non-blank, non-separator
                i += 1
            # Skip trailing blank lines
            while i < len(lines) and lines[i].strip() == '':
                i += 1
            continue

        result.append(line)
        i += 1

    return '\n'.join(result)


def clean_orphan_separators(text):
    """Remove orphaned --- lines and Source lines left after extraction."""
    lines = text.split('\n')
    cleaned = []
    i = 0
    while i < len(lines):
        line = lines[i]
        # Skip orphaned Source lines
        if re.match(r'^\s*- \*\*Source:\*\*', line):
            i += 1
            # Also skip following blank/--- lines
            while i < len(lines) and lines[i].strip() in ('', '---'):
                i += 1
            continue
        cleaned.append(line)
        i += 1

    # Collapse excessive blank lines
    result = []
    blank_count = 0
    for line in cleaned:
        if line.strip() == '':
            blank_count += 1
            if blank_count <= 2:
                result.append(line)
        else:
            blank_count = 0
            result.append(line)

    # Remove orphaned --- pairs at the end
    while len(result) >= 2 and result[-1].strip() == '---' and result[-2].strip() == '---':
        result.pop()
        result.pop()
    while result and result[-1].strip() in ('', '---'):
        result.pop()

    return '\n'.join(result) + '\n'


def main():
    with open(DELTAS_PATH) as f:
        deltas_text = f.read()
    with open(FIXED_PATH) as f:
        fixed_text = f.read()
    with open(REJECTED_PATH) as f:
        rejected_text = f.read()

    # Backup
    shutil.copy2(DELTAS_PATH, DELTAS_PATH + '.pre-audit-bak')
    shutil.copy2(FIXED_PATH, FIXED_PATH + '.pre-audit-bak')
    shutil.copy2(REJECTED_PATH, REJECTED_PATH + '.pre-audit-bak')
    print("Backups created (.pre-audit-bak)")

    lines = deltas_text.split('\n')
    delta_header_re = re.compile(r'^(- )?\*\*\[([A-Z]+-?\d+)\]')

    # === Phase 1: Find first section header ===
    first_section = None
    for i, line in enumerate(lines):
        if re.match(r'^## \*\*[A-F]\.', line):
            first_section = i
            break

    # === Phase 2: Extract top-of-file FIXED deltas (D124-D127) ===
    header_lines = lines[:first_section]
    top_fixed_blocks = []
    cleaned_header = []
    i = 0
    while i < len(header_lines):
        line = header_lines[i]
        # Match D124-D127 headers: **[D12X]**
        m = re.match(r'^\*\*\[D(12[4-7])\]\*\*', line)
        if m:
            delta_id = 'D' + m.group(1)
            block = [line]
            i += 1
            # Collect lines until next delta or non-delta content
            while i < len(header_lines):
                next_line = header_lines[i]
                if re.match(r'^\*\*\[D\d+\]', next_line):
                    break
                if (next_line.strip() == '---' and i + 1 < len(header_lines)
                        and header_lines[i + 1].strip() == '---'
                        and i + 2 < len(header_lines)
                        and not re.match(r'^\*\*\[D\d+\]', header_lines[i + 2])):
                    # Double --- followed by non-delta = end of all top-of-file deltas
                    break
                block.append(next_line)
                i += 1
            top_fixed_blocks.append(block)
            print(f"  Top-of-file FIXED: {delta_id}")
            # Skip trailing separator lines
            while i < len(header_lines) and header_lines[i].strip() in ('', '---'):
                i += 1
            continue

        cleaned_header.append(line)
        i += 1

    # Trim trailing blanks/separators from cleaned header
    while cleaned_header and cleaned_header[-1].strip() in ('', '---'):
        cleaned_header.pop()
    cleaned_header_text = '\n'.join(cleaned_header) + '\n'

    # === Phase 3: Parse section deltas ===
    fixed_blocks = list(top_fixed_blocks)  # include top-of-file FIXED deltas
    rejected_blocks = []
    kept_parts = [cleaned_header_text.rstrip('\n')]

    i = first_section
    while i < len(lines):
        line = lines[i]
        m = delta_header_re.match(line)

        if m:
            delta_id = m.group(2)
            classification = classify_by_id(delta_id)

            # Collect block: from here to the next delta header (or EOF)
            block_start = i
            i += 1
            while i < len(lines) and not delta_header_re.match(lines[i]):
                i += 1
            block = lines[block_start:i]

            if classification == 'fixed':
                fixed_blocks.append(block[:])
                print(f"  FIXED: {delta_id}")
            elif classification == 'rejected':
                rejected_blocks.append((delta_id, block[:]))
                print(f"  REJECTED: {delta_id}")
            else:
                kept_parts.append('\n'.join(block))
        else:
            # Non-delta content: collect until next delta header
            chunk_start = i
            i += 1
            while i < len(lines) and not delta_header_re.match(lines[i]):
                i += 1
            chunk = lines[chunk_start:i]

            # Keep section headers and structural content
            # But skip orphaned --- and Source lines left by removed deltas
            chunk_text = '\n'.join(chunk)
            if chunk_text.strip():
                kept_parts.append(chunk_text)

    # === Phase 4: Reconstruct DELTAS.md ===
    new_deltas = '\n'.join(kept_parts)
    new_deltas = clean_orphan_separators(new_deltas)

    # === Phase 5: Append to DELTAS-fixed.md (skip duplicates) ===
    fixed_append = '\n'
    appended_count = 0
    for block in fixed_blocks:
        formatted = format_fixed_entry(block[:])
        if not formatted.strip():
            continue
        # Extract delta ID and check for existing entry
        block_ids = set()
        for line in formatted.split('\n')[:5]:
            did = extract_delta_id(line)
            if did:
                block_ids.add(did)
        # Skip if any ID already exists in the fixed file
        already_exists = False
        for did in block_ids:
            if re.search(rf'\[{re.escape(did)}\]', fixed_text):
                already_exists = True
                print(f"  SKIP (already in fixed): {did}")
                break
        if not already_exists:
            fixed_append += '\n---\n\n' + formatted + '\n'
            appended_count += 1
    new_fixed = fixed_text.rstrip() + fixed_append + '\n'

    # === Phase 6: Append to DELTAS-rejected.md ===
    rejected_append = '\n'
    for delta_id, block in rejected_blocks:
        formatted = format_rejected_entry(block[:], delta_id)
        if formatted.strip():
            rejected_append += '\n---\n' + formatted + '\n---\n'
    new_rejected = rejected_text.rstrip() + rejected_append + '\n'

    # === Phase 7: Remove B002/B026 from DELTAS-rejected.md ===
    new_rejected = remove_from_rejected(new_rejected, REMOVE_FROM_REJECTED)
    print("Removed B002 and B026 from DELTAS-rejected.md")

    # === Phase 8: Write ===
    with open(DELTAS_PATH, 'w') as f:
        f.write(new_deltas)
    with open(FIXED_PATH, 'w') as f:
        f.write(new_fixed)
    with open(REJECTED_PATH, 'w') as f:
        f.write(new_rejected)

    orig = len(lines)
    new = len(new_deltas.split('\n'))
    print(f"\n=== Audit Complete ===")
    print(f"DELTAS.md: {orig} -> {new} lines ({orig - new} removed)")
    print(f"DELTAS-fixed.md: appended {appended_count} entries ({len(fixed_blocks) - appended_count} already present)")
    print(f"DELTAS-rejected.md: appended {len(rejected_blocks)} entries, removed 2")


if __name__ == '__main__':
    main()
