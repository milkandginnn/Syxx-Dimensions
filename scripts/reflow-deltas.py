#!/usr/bin/env python3
"""
reflow-deltas.py — align DELTAS.md entries to the house template.

Every delta field is aligned to a fixed value column (default 24): the label +
padding fill columns 0..VALUE_COL-1, and the value plus every wrapped/continuation
line begins at VALUE_COL. Prose is hard-wrapped to WIDTH; `Fix(es)` items each get
their own line; `Code Sample` blocks are indented (never word-wrapped).

Only the delta ledger is touched — everything before the first `## **A. ...` domain
header (the instructions, template, and worked example) is left byte-for-byte alone.

Usage:
    python3 scripts/reflow-deltas.py DELTAS.md            # rewrite in place (writes DELTAS.md.bak)
    python3 scripts/reflow-deltas.py DELTAS.md out.md     # read DELTAS.md, write out.md
"""
import re
import sys
import textwrap

VALUE_COL = 24   # column where every value / continuation line starts
WIDTH = 100      # hard-wrap width for prose fields (not code)

CONT = ' ' * VALUE_COL
ID_RE = re.compile(r'^- \*\*(\[[A-F]\d{3}\])\*\*\s+(.+)$')
LAB_RE = re.compile(r'^- \*\*([A-Za-z().\-/ ]+:)\*\*\s+(.+)$')
DOMAIN_RE = re.compile(r'^## \*\*[A-F]\.')


def wrap_field(prefix, value, split_fixes=False):
    pad = prefix + ' ' * max(1, VALUE_COL - len(prefix))
    avail = WIDTH - VALUE_COL
    out, first = [], True
    chunks = ([c.strip() for c in re.split(r'(?=\[Fix \d\])', value) if c.strip()]
              if split_fixes and '[Fix ' in value else [value])
    for chunk in chunks:
        for w in (textwrap.wrap(chunk, width=avail) or ['']):
            out.append((pad if first else CONT) + w)
            first = False
    return out


def reflow(lines):
    start = next((i for i, l in enumerate(lines) if DOMAIN_RE.match(l)), None)
    if start is None:
        sys.exit("reflow-deltas: no `## **A. ...` domain header found — nothing to do.")

    # The ledger ends at the first `## ` heading that is NOT a domain section
    # (e.g. `## USER's Delta of the Week`). Everything past that is left alone.
    n = next((k for k in range(start + 1, len(lines))
              if lines[k].startswith('## ') and not DOMAIN_RE.match(lines[k])), len(lines))

    out = lines[:start]
    i = start
    while i < n:
        line = lines[i]
        if line.strip() == '- **Code Sample:**':
            j = i + 1
            while j < n and lines[j].strip() == '':
                j += 1
            j += 1  # skip the opening '['
            code = []
            while j < n and lines[j].strip() != ']':
                code.append(lines[j])
                j += 1
            out.append('- **Code Sample:**' + ' ' * (VALUE_COL - len('- **Code Sample:**')) + '[')
            out.append('')
            for c in code:
                out.append('' if c.strip() == '' else CONT + (c[4:] if c.startswith('    ') else c.lstrip()))
            out.append('')
            out.append(CONT + ']')
            i = j + 1  # skip the closing ']'
            continue
        m_id = ID_RE.match(line)
        m_lab = LAB_RE.match(line)
        if m_id:
            out.extend(wrap_field('- **%s**' % m_id.group(1), m_id.group(2)))
        elif m_lab:
            label = m_lab.group(1)
            out.extend(wrap_field('- **%s**' % label, m_lab.group(2),
                                  split_fixes=label.startswith('Fix(es)')))
        else:
            out.append(line)
        i += 1
    out.extend(lines[n:])
    return out


def main():
    if len(sys.argv) not in (2, 3):
        sys.exit(__doc__)
    path = sys.argv[1]
    with open(path) as f:
        text = f.read()
    trailing_nl = text.endswith('\n')
    result = '\n'.join(reflow(text.split('\n')))
    if trailing_nl and not result.endswith('\n'):
        result += '\n'
    if len(sys.argv) == 3:
        with open(sys.argv[2], 'w') as f:
            f.write(result)
        print('reflow-deltas: wrote', sys.argv[2])
    else:
        with open(path + '.bak', 'w') as f:
            f.write(text)
        with open(path, 'w') as f:
            f.write(result)
        print('reflow-deltas: rewrote %s in place (backup at %s.bak)' % (path, path))


if __name__ == '__main__':
    main()
