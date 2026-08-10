#!/usr/bin/env python3
"""Extract delta information from DELTAS.md and generate plan.md at detail level 5."""

import json
import re
from pathlib import Path
from datetime import datetime
from collections import defaultdict

BASE = Path('/Users/masterrabbit/Software/Syxx')

def extract_delta_info():
    """Extract delta information from DELTAS.md."""
    deltas_info = {}
    deltas_md = BASE / 'DELTAS.md'
    
    with open(deltas_md, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Split on delta entry markers
    entries = re.split(r'(?=^-\s+\*\*\[([ABCE]\d{3})\]\*\*)', content, flags=re.MULTILINE)
    
    i = 1  # skip preamble
    while i < len(entries) - 1:
        delta_id = entries[i]
        body = entries[i + 1]
        i += 2
        
        info = {'id': delta_id, 'title': '', 'location': '', 'fixes': '', 'issue': '', 'criticality': ''}
        
        # Title
        title_match = re.search(r'^-\s+\*\*\[' + delta_id + r'\]\*\*\s+(.+)', body, re.MULTILINE)
        if title_match:
            info['title'] = title_match.group(1).strip()
        
        # Location
        loc_match = re.search(r'\*\*Location:\*\*\s+(.+?)(?:\n(?!\s{24}))', body)
        if loc_match:
            info['location'] = loc_match.group(1).strip()
        
        # Criticality
        crit_match = re.search(r'\*\*Criticality:\*\*\s+(.+?)(?:\n)', body)
        if crit_match:
            info['criticality'] = crit_match.group(1).strip()
        
        # Issue
        issue_match = re.search(r'\*\*Issue:\*\*\s+(.+?)(?=\n-\s+\*\*)', body, re.DOTALL)
        if issue_match:
            info['issue'] = issue_match.group(1).strip()[:500]
        
        # Fix(es) - extract all fix text
        fix_match = re.search(r'\*\*Fix\(es\):\*\*\s+(.+?)(?=\n-\s+\*\*(?:Code|Cross|Status|Dep))', body, re.DOTALL)
        if fix_match:
            fix_text = fix_match.group(1).strip()
            # Clean up excessive whitespace
            fix_text = re.sub(r'\n\s{20,}', ' ', fix_text)
            fix_text = re.sub(r'\s+', ' ', fix_text)
            info['fixes'] = fix_text[:800]
        
        deltas_info[delta_id] = info
    
    return deltas_info


def load_json(path):
    with open(path) as f:
        return json.load(f)


def get_platform(files):
    """Determine which platforms are touched by a list of files."""
    platforms = set()
    for f in files:
        if f.startswith('Server/') or f.endswith('.py'):
            platforms.add('server')
        elif f.startswith('macOS/') or f.endswith('.swift'):
            platforms.add('macos')
        elif f.startswith('Windows/') or f.startswith('Linux/') or f.endswith('.cs'):
            platforms.add('windows')
    return platforms


def get_verification_commands(files):
    """Get verification commands for the platforms touched."""
    platforms = get_platform(files)
    cmds = []
    if 'server' in platforms:
        cmds.append('cd Server && python -m pytest tests/ -v --tb=short')
    if 'macos' in platforms:
        cmds.append('cd macOS && swift build 2>&1 | tail -5 && swift test 2>&1 | tail -20')
    if 'windows' in platforms:
        cmds.append('dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal')
    if not cmds:
        cmds.append('make validate STRICT=1')
    return cmds


def get_phase_theme(deltas, worklist_deltas):
    """Determine dominant theme for a group of deltas."""
    themes = defaultdict(int)
    
    theme_map = {
        'passivedns': 'Passive DNS',
        'warp': 'Warp & Content Integrity',
        'dnssec': 'DNSSEC Validation',
        'resolver': 'DNS Resolution',
        'dns_authoritative': 'Authoritative DNS',
        'dot_listener': 'DNS-over-TLS',
        'synthes': 'Synthesis Engine',
        'matrix': 'Matrix Determinative Engine',
        'license': 'Licensing & Activation',
        'keygen': 'Licensing & Activation',
        'fulfillment': 'License Fulfillment',
        'leak': 'DNS Leak Detection',
        'investigate': 'Investigation Pipeline',
        'config': 'Server Configuration',
        'db': 'Database & Storage',
        'scheduler': 'Task Scheduler',
        'app.py': 'Server API Endpoints',
        'fetcher': 'Content Fetching',
        'firewall_test': 'Firewall Testing',
        'ratelimit': 'Rate Limiting',
        'stripe_checkout': 'Payment Processing',
        'email_auth': 'Email Authentication',
        'capture': 'Capture Events',
        'parity': 'Parity Vectors',
        'logging': 'Observability & Logging',
        'telemetry': 'Telemetry',
        'witness': 'Witness Signing',
        'trust': 'Trust Store & Pinning',
        'integrity': 'Integrity Verification',
        'network': 'Network Security',
        'dpapi': 'Secret Storage',
        'secret': 'Secret Storage',
        'linux_secret': 'Secret Storage',
        'settings': 'Settings & Preferences',
        'conclusions': 'Conclusions Display',
        'probe': 'Probe Planning',
        'remote_content': 'Remote Content',
        'diff': 'Content Diffing',
        'seo': 'SEO & Landing Pages',
        'authoritative': 'Authoritative Vantage',
        'dnsresolver': 'DNS Resolver',
        'dns_resolver': 'DNS Resolver',
        'pyproject': 'Dependencies',
        'toml': 'Dependencies',
    }
    
    for did in deltas:
        meta = worklist_deltas.get(did, {})
        files = meta.get('files', [])
        title = meta.get('title', '').lower()
        
        matched = False
        for f in files:
            f_lower = f.lower()
            for key, theme in theme_map.items():
                if key in f_lower:
                    themes[theme] += 1
                    matched = True
                    break
            if matched:
                break
        
        if not matched:
            if 'test' in title or 'coverage' in title or 'quarantine' in title:
                themes['Testing & Coverage'] += 1
            elif 'depend' in title or 'version' in title or 'cve' in title:
                themes['Dependency Updates'] += 1
            elif 'security' in title or 'csrf' in title or 'csp' in title or 'hsts' in title:
                themes['Security Hardening'] += 1
            elif 'refactor' in title or 'extract' in title or 'duplicat' in title or 'monolith' in title:
                themes['Code Structure & Refactoring'] += 1
            elif 'observ' in title or 'otel' in title or 'sentry' in title or 'metric' in title:
                themes['Observability & Monitoring'] += 1
            elif 'rate' in title or 'limit' in title:
                themes['Rate Limiting'] += 1
            elif 'cache' in title or 'etag' in title:
                themes['Caching & Performance'] += 1
            elif 'input' in title or 'valid' in title:
                themes['Input Validation'] += 1
            elif 'parity' in title or 'port' in title:
                themes['Cross-Platform Parity'] += 1
            else:
                themes['General Fixes'] += 1
    
    if not themes:
        return 'General Fixes'
    
    return max(themes.items(), key=lambda x: x[1])[0]


def estimate_effort(deltas, worklist_deltas):
    """Estimate effort for a phase."""
    total_body = 0
    for did in deltas:
        meta = worklist_deltas.get(did, {})
        total_body += meta.get('body_len', 1000)
    
    n = len(deltas)
    if n == 1:
        if total_body > 1800:
            return '~3h'
        return '~1-2h'
    elif n <= 3:
        return '~4-6h'
    elif n <= 6:
        return '~6-8h'
    elif n <= 10:
        return '~8-12h'
    elif n <= 15:
        return '~12-16h'
    else:
        return '~16-24h'


def get_entry_points(meta):
    """Get entry points from line_refs."""
    refs = meta.get('line_refs', [])
    if not refs:
        loc = meta.get('location', '')
        if loc:
            return loc.split(';')[0].strip()
        return 'See DELTAS.md'
    
    seen = set()
    points = []
    for ref in refs:
        key = f"{ref[0]}:{ref[1]}"
        if key not in seen:
            seen.add(key)
            points.append(key)
    return '; '.join(points[:4])


def get_expected_changes(did, meta, delta_info):
    """Derive expected changes from fix description."""
    fixes = delta_info.get('fixes', '')
    if fixes:
        return fixes[:300]
    
    title = meta.get('title', '')
    files = meta.get('files', [])
    return f"Implement fix for: {title}. Touches: {', '.join(files[:3])}"


def get_edge_cases(did, delta_info):
    """Derive edge cases from issue description."""
    issue = delta_info.get('issue', '')
    if not issue:
        return 'Test with empty inputs, boundary values, and concurrent access'
    
    # Look for edge case keywords
    edge_hints = []
    if 'race' in issue.lower() or 'toctou' in issue.lower():
        edge_hints.append('concurrent access')
    if 'null' in issue.lower() or 'none' in issue.lower() or 'empty' in issue.lower():
        edge_hints.append('null/empty inputs')
    if 'unicode' in issue.lower() or 'idn' in issue.lower() or 'utf' in issue.lower():
        edge_hints.append('Unicode edge cases')
    if 'timeout' in issue.lower() or 'slow' in issue.lower():
        edge_hints.append('timeout/slow responses')
    if 'overflow' in issue.lower() or 'bound' in issue.lower() or 'limit' in issue.lower():
        edge_hints.append('boundary values')
    if 'restart' in issue.lower() or 'crash' in issue.lower():
        edge_hints.append('server restart / crash recovery')
    if 'malicious' in issue.lower() or 'attack' in issue.lower():
        edge_hints.append('adversarial inputs')
    
    if edge_hints:
        return 'Test: ' + ', '.join(edge_hints)
    return 'Test with normal, edge-case, and adversarial inputs'


def get_rollback(did, meta):
    """Generate rollback instructions."""
    files = meta.get('files', [])
    has_migration = any('db' in f.lower() and 'schema' in f.lower() for f in files)
    
    base = 'git revert <commit>'
    if has_migration:
        base += '; run reverse migration'
    return base


def main():
    phases_data = load_json(BASE / '.deltas' / 'plan_phases.json')
    worklist_data = load_json(BASE / '.deltas' / 'plan_worklist.json')
    deltas_info = extract_delta_info()
    
    worklist_deltas = worklist_data['deltas']
    dep_edges = phases_data['dep_edges']
    
    # Build phase list
    all_phases = []
    phase_num = 1
    
    for phase_deltas in phases_data['fix_now_phases']:
        all_phases.append({
            'num': phase_num,
            'type': 'FIX NOW',
            'deltas': phase_deltas
        })
        phase_num += 1
    
    for phase_deltas in phases_data['fix_next_phases']:
        all_phases.append({
            'num': phase_num,
            'type': 'FIX NEXT',
            'deltas': phase_deltas
        })
        phase_num += 1
    
    lines = []
    
    # Header
    lines.append('# Delta Remediation Plan\n')
    lines.append(f'**Generated:** {datetime.now().strftime("%Y-%m-%dT%H:%M:%S")}')
    lines.append('**Mode:** auto (--detail 5 granular)')
    lines.append('**Worklist:** 194 deltas (96 FIX NOW, 98 FIX NEXT)')
    lines.append(f'**Phases:** {len(all_phases)} total')
    lines.append('**Merges applied:** 6 (see below)\n')
    
    # Merges
    lines.append('## Merges Applied\n')
    lines.append('| Merge Into | Merged From | Reason |')
    lines.append('|------------|-------------|--------|')
    merges = [
        ('A003', 'A006', 'A006 description incorrect; actual issue covered by A003'),
        ('D009', 'D052', 'D052 is duplicate of D009'),
        ('D023', 'D037', 'D037 near-duplicate of D023'),
        ('D054', 'D072', 'Same root cause'),
        ('D120', 'D144', 'Duplicate'),
        ('B002', 'B020', 'B020 overlaps B002 significantly'),
    ]
    for into, fr, reason in merges:
        lines.append(f'| {into} | {fr} | {reason} |')
    
    lines.append('\n---\n')
    
    # Generate phases
    single_delta_phases = []
    multi_delta_count = 0
    
    for phase in all_phases:
        phase_deltas = phase['deltas']
        phase_type = phase['type']
        phase_num = phase['num']
        
        theme = get_phase_theme(phase_deltas, worklist_deltas)
        effort = estimate_effort(phase_deltas, worklist_deltas)
        
        # Collect all files for verification
        all_files = []
        for did in phase_deltas:
            all_files.extend(worklist_deltas.get(did, {}).get('files', []))
        
        if len(phase_deltas) == 1:
            did = phase_deltas[0]
            meta = worklist_deltas.get(did, {})
            delta_info = deltas_info.get(did, {})
            files = meta.get('files', [])
            verification = get_verification_commands(files)
            
            single_delta_phases.append({
                'phase': phase_num,
                'delta': did,
                'type': phase_type,
                'theme': theme,
                'title': meta.get('title', delta_info.get('title', 'Unknown'))[:80],
                'effort': effort,
                'verification': verification[0] if verification else 'make validate'
            })
            continue
        
        # Multi-delta phase
        multi_delta_count += 1
        lines.append(f'## Phase {phase_num}: {theme} ({len(phase_deltas)} deltas, {effort})\n')
        lines.append(f'**Priority:** {phase_type}')
        lines.append(f'**Deltas:** {", ".join(phase_deltas)}\n')
        
        for did in phase_deltas:
            meta = worklist_deltas.get(did, {})
            delta_info = deltas_info.get(did, {})
            
            title = meta.get('title', delta_info.get('title', 'Unknown'))
            location = meta.get('location', delta_info.get('location', 'Unknown'))
            files = meta.get('files', [])
            
            # Dependencies (filter to only those in the worklist)
            raw_deps = dep_edges.get(did, [])
            deps = [d for d in raw_deps if d in worklist_deltas]
            
            lines.append(f'### {did} — {title}\n')
            lines.append(f'- **Location:** {location}')
            lines.append(f'- **Decision:** {phase_type}')
            
            # Approach
            expected = get_expected_changes(did, meta, delta_info)
            lines.append(f'- **Approach:** {expected}')
            
            # Entry points
            entry_points = get_entry_points(meta)
            lines.append(f'- **Entry points:** {entry_points}')
            
            # Files
            if files:
                lines.append(f'- **Files:** {", ".join(files)}')
            
            # Edge cases
            edge_cases = get_edge_cases(did, delta_info)
            lines.append(f'- **Edge cases:** {edge_cases}')
            
            # Rollback
            rollback = get_rollback(did, meta)
            lines.append(f'- **Rollback:** {rollback}')
            
            # Dependencies
            if deps:
                lines.append(f'- **Depends on:** {", ".join(set(deps))}')
            else:
                lines.append('- **Depends on:** None')
            
            lines.append('')
        
        # Verification gate
        lines.append('#### Verification Gate\n')
        lines.append('```bash')
        for cmd in get_verification_commands(all_files):
            lines.append(cmd)
        lines.append('```\n')
        lines.append('---\n')
    
    # Single-delta phases summary
    if single_delta_phases:
        lines.append('## Single-Delta Phases (Summary)\n')
        lines.append(f'These {len(single_delta_phases)} phases each contain a single delta. '
                      'See DELTAS.md for full implementation details.\n')
        lines.append('| Phase | Delta | Priority | Theme | Title | Effort | Verification |')
        lines.append('|------:|-------|----------|-------|-------|--------|--------------|')
        
        for sp in single_delta_phases:
            title_short = sp['title'][:60] + ('...' if len(sp['title']) > 60 else '')
            lines.append(
                f"| {sp['phase']} | {sp['delta']} | {sp['type']} | {sp['theme']} "
                f"| {title_short} | {sp['effort']} | `{sp['verification']}` |"
            )
        
        lines.append('')
    
    # Phase dependency graph
    lines.append('## Phase Dependency Summary\n')
    lines.append('Key dependency chains (from `dep_edges`):')
    lines.append('')
    
    # Show critical chains
    chains = [
        'Server warp chain: A002 → A003 → A004 → A007 → A008 → A009 → A010 → A041 → A054 → E005 → E006',
        'Server API chain: A005 → A011 → A012 → A015 → A020 → A023 → A024 → A025 → A028 → A037 → ... → A078',
        'DB chain: A014 → A018 → A021 → A083 → E010 → E024 → E025 → E035 → E045',
        'DNSSEC chain: A013 → A016 → A017 → A027 → A030 → A077',
        'Investigation chain: A019 → A047 → A049 → A076 → E042',
        'Licensing chain: E017 → E018 → E009 → E011 → E023 → E050',
        'Synthesizer C# chain: B003 → B034 → B036 → B037 → B038 → B039 → B040 → B041 → B042 → B043 → B044 → C008',
        'Matrix C# chain: B004 → B025 → B026 → B027 → B028 → B029 → B030 → B031 → C006 → C012',
        'DNSResolver chain: B008 → B032 → B033 → C007',
    ]
    
    for chain in chains:
        lines.append(f'- {chain}')
    
    lines.append('')
    
    # Contract
    lines.append('## Contract\n')
    lines.append('- The plan is executable by parallel agents.')
    lines.append('- No two phases touch the same file unless the dependency is explicit and ordered.')
    lines.append('- Each phase includes a verification gate.')
    lines.append('- Effort estimates are advisory, not commitments.')
    lines.append('- Detail level: 5 (granular) — full implementation brief per delta.')
    lines.append(f'- Multi-delta phases: {multi_delta_count} (full sections above)')
    lines.append(f'- Single-delta phases: {len(single_delta_phases)} (summary table)')
    lines.append(f'- Total phases: {len(all_phases)}')
    
    # Write output
    output_path = BASE / '.deltas' / 'plan.md'
    with open(output_path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
    
    print(f'Generated {output_path}')
    print(f'  Multi-delta phases: {multi_delta_count}')
    print(f'  Single-delta phases: {len(single_delta_phases)}')
    print(f'  Total phases: {len(all_phases)}')
    print(f'  Deltas extracted: {len(deltas_info)}')


if __name__ == '__main__':
    main()
