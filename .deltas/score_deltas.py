#!/usr/bin/env python3
"""Parse DELTAS.md, score all entries, write evaluation.json."""

import json
import re
import os
from datetime import datetime, timezone

DELTAS_PATH = "/Users/masterrabbit/Software/Syxx/DELTAS.md"
OUTPUT_PATH = "/Users/masterrabbit/Software/Syxx/.deltas/evaluation.json"

def read_deltas():
    with open(DELTAS_PATH, "r", encoding="utf-8") as f:
        return f.read()

def parse_entries(text):
    """Split text into individual delta blocks and parse each."""
    # Find the start of the actual ledger (after instructions)
    ledger_start_match = re.search(r'^## \*\*A\. Server Components\*\*', text, re.MULTILINE)
    ledger_start = ledger_start_match.start() if ledger_start_match else 0
    
    # Find all delta entry starts: lines matching - **[XNNN]**
    pattern = re.compile(
        r'^- \*\*\[([A-F]\d{3})\]\*\*\s+(.+?)$',
        re.MULTILINE
    )
    
    matches = list(pattern.finditer(text))
    entries = []
    seen_ids = set()
    
    for i, m in enumerate(matches):
        # Skip entries before the actual ledger starts (template examples)
        if m.start() < ledger_start:
            continue
        
        delta_id = m.group(1)
        description = m.group(2).strip()
        
        # Skip duplicate IDs (keep first occurrence in ledger)
        if delta_id in seen_ids:
            continue
        seen_ids.add(delta_id)
        
        # Get the block text: from this match to the next match (or end)
        start = m.start()
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        block = text[start:end]
        
        entry = parse_block(delta_id, description, block)
        entries.append(entry)
    
    return entries

def parse_block(delta_id, description, block):
    """Parse a single delta block into structured data."""
    # Extract criticality
    severity = extract_severity(block)
    
    # Extract location
    location = extract_field(block, "Location")
    
    # Extract cross-references
    cross_refs = extract_cross_refs(block)
    
    # Extract status
    status = extract_status(block)
    
    # Determine platform from location
    platform = determine_platform(location, delta_id, description, block)
    
    # Determine domain from ID prefix
    domain = delta_id[0]
    
    return {
        "id": delta_id,
        "description": description,
        "severity": severity,
        "status": status,
        "location": location,
        "platform": platform,
        "domain": domain,
        "cross_refs": cross_refs,
        "block": block  # keep for scoring heuristics
    }

def extract_severity(block):
    """Extract severity from Criticality field."""
    crit_match = re.search(r'\*\*Criticality:\*\*\s*(.+?)(?:\n|$)', block, re.IGNORECASE)
    if not crit_match:
        # Try alternate format
        crit_match = re.search(r'Criticality[：:]\s*(.+?)(?:\n|$)', block, re.IGNORECASE)
    
    if crit_match:
        crit_text = crit_match.group(1).lower()
        if "critical" in crit_text and "non-critical" not in crit_text and "non-" not in crit_text:
            return "Critical"
        elif "non-critical" in crit_text or "non critical" in crit_text:
            # Check for further nuance
            if "high" in crit_text:
                return "High"
            return "Medium"
        elif "high" in crit_text:
            return "High"
        elif "low" in crit_text:
            return "Low"
        else:
            return "Medium"
    
    # Check if the description text mentions critical
    if "critical" in block.lower()[:500]:
        # Look more carefully
        if "non-critical" in block.lower()[:500] or "non critical" in block.lower()[:500]:
            return "Medium"
        return "Critical"
    
    return "Medium"

def extract_field(block, field_name):
    """Extract a field value from the block."""
    pattern = re.compile(
        rf'\*\*{field_name}:\*\*\s*(.+?)(?=\n- \*\*|\n---|\Z)',
        re.DOTALL | re.IGNORECASE
    )
    m = pattern.search(block)
    if m:
        val = m.group(1).strip()
        # Clean up continuation indentation
        val = re.sub(r'\n\s{20,}', ' ', val)
        val = re.sub(r'\s+', ' ', val)
        return val.strip()
    return ""

def extract_cross_refs(block):
    """Extract cross-reference delta IDs."""
    refs = []
    # Look for Cross-ref. Delta: field
    cr_match = re.search(
        r'\*\*Cross-ref\.\s*Delta:\*\*\s*(.+?)(?=\n- \*\*|\n---|\Z)',
        block, re.DOTALL | re.IGNORECASE
    )
    if cr_match:
        cr_text = cr_match.group(1)
        # Find all delta IDs mentioned
        ids = re.findall(r'[A-F]\d{3}', cr_text)
        refs.extend(ids)
    
    # Also look for "Related to", "Partially covered by", etc.
    related = re.findall(r'(?:Related to|covered by|see|cf\.?)\s+([A-F]\d{3})', block, re.IGNORECASE)
    refs.extend(related)
    
    # Deduplicate
    return list(dict.fromkeys(refs))

def extract_status(block):
    """Extract status."""
    status_match = re.search(r'\*\*Status:\*\*\s*\*\*(\w+)\*\*', block, re.IGNORECASE)
    if status_match:
        return status_match.group(1).upper()
    return "OPEN"

def determine_platform(location, delta_id, description, block):
    """Determine platform from location and context."""
    loc_lower = location.lower() if location else ""
    desc_lower = description.lower()
    block_lower = block.lower()[:1000]
    
    platforms = []
    if "server/" in loc_lower or "python" in loc_lower or "app.py" in loc_lower:
        platforms.append("server")
    if "macos" in loc_lower or "swift" in loc_lower or ".swift" in loc_lower:
        platforms.append("macos")
    if "windows" in loc_lower or "dotnet" in loc_lower or ".cs" in loc_lower or "c#" in loc_lower:
        platforms.append("windows")
    if "linux" in loc_lower or "ubuntu" in loc_lower or "avalonia" in loc_lower:
        platforms.append("linux")
    
    # Check description for platform hints
    if not platforms:
        if "macos" in desc_lower or "swift" in desc_lower:
            platforms.append("macos")
        if "windows" in desc_lower or "c#" in desc_lower:
            platforms.append("windows")
        if "linux" in desc_lower:
            platforms.append("linux")
        if "server" in desc_lower:
            platforms.append("server")
    
    # Check for cross-platform
    if "cross-platform" in desc_lower or "cross-platform" in block_lower[:500]:
        if len(platforms) < 2:
            platforms = ["cross-platform"]
    
    # Domain-based defaults
    if not platforms:
        domain = delta_id[0]
        if domain == "A":
            platforms = ["server"]
        elif domain == "B":
            platforms = ["cross-platform"]
        elif domain == "C":
            platforms = ["cross-platform"]
        elif domain == "D":
            platforms = ["cross-platform"]
        elif domain == "E":
            platforms = ["cross-platform"]
        elif domain == "F":
            platforms = ["cross-platform"]
    
    return ", ".join(platforms) if platforms else "unknown"

def score_entry(entry):
    """Score a delta entry using the composite formula."""
    # Severity score (weight 0.35)
    sev_map = {"Critical": 1.0, "High": 0.7, "Medium": 0.4, "Low": 0.1}
    severity_score = sev_map.get(entry["severity"], 0.4)
    
    # Exploitability score (weight 0.25)
    exploit_score = score_exploitability(entry)
    
    # Fix complexity score (weight 0.20)
    fix_score = score_fix_complexity(entry)
    
    # Blast radius score (weight 0.20)
    blast_score = score_blast_radius(entry)
    
    composite = (0.35 * severity_score + 
                 0.25 * exploit_score + 
                 0.20 * fix_score + 
                 0.20 * blast_score)
    
    composite = round(composite, 3)
    
    # Assign tier
    if composite >= 0.70:
        tier = "MUST FIX"
    elif composite >= 0.40:
        tier = "SHOULD FIX"
    elif composite >= 0.15:
        tier = "COULD FIX"
    else:
        tier = "WON'T FIX"
    
    return {
        "composite": composite,
        "tier": tier,
        "factors": {
            "severity": {"weight": 0.35, "score": severity_score},
            "exploitability": {"weight": 0.25, "score": exploit_score},
            "fix_complexity": {"weight": 0.20, "score": fix_score},
            "blast_radius": {"weight": 0.20, "score": blast_score}
        }
    }

def score_exploitability(entry):
    """Score exploitability based on heuristics."""
    desc = entry["description"].lower()
    loc = entry["location"].lower() if entry["location"] else ""
    block = entry["block"].lower()[:2000]
    domain = entry["domain"]
    
    # Server endpoints reachable from internet
    server_indicators = ["app.py", "server/", "endpoint", "/api/", "route", "do_post", "do_get", "do_delete"]
    is_server = any(ind in loc for ind in server_indicators) or domain == "A"
    
    # Security-related keywords suggest higher exploitability
    security_keywords = ["ssrf", "rce", "injection", "bypass", "csrf", "xss", "forge", 
                         "credential", "key", "hmac", "auth", "hijack", "rebind", "spoof",
                         "unbounded", "unvalidated", "no auth", "no csrf", "no validation"]
    has_security_issue = any(kw in desc or kw in block[:500] for kw in security_keywords)
    
    # Crash/safety issues
    crash_keywords = ["crash", "fatal", "force-unwrap", "force unwrap", "panic", "oom", "overflow"]
    has_crash = any(kw in desc or kw in block[:500] for kw in crash_keywords)
    
    if is_server and has_security_issue:
        return 1.0  # Externally reachable server security issue
    elif has_security_issue and ("cross-platform" in entry["platform"] or "server" in entry["platform"]):
        return 0.8
    elif has_security_issue:
        return 0.6  # Client-side security issue
    elif has_crash:
        return 0.6  # Crash-inducing bugs are user-triggerable
    elif is_server:
        return 0.6  # Server issues, general
    elif "cross-platform" in entry["platform"]:
        return 0.6  # Cross-platform parity issues
    elif "test" in desc or "coverage" in desc or "quarantine" in desc:
        return 0.3  # Test infrastructure issues
    elif "documentation" in desc or "comment" in desc or "naming" in desc or "typo" in desc:
        return 0.3  # Documentation/naming issues
    elif "dependency" in desc or "version" in desc or "behind" in desc or "outdated" in desc:
        return 0.6  # Dependency issues could have known exploits
    else:
        return 0.3  # Default: local/internal code path

def score_fix_complexity(entry):
    """Score fix complexity based on heuristics."""
    desc = entry["description"].lower()
    block = entry["block"].lower()[:2000]
    loc = entry["location"] or ""
    
    # Count files mentioned in location
    files = re.findall(r'[\w/]+\.(?:py|swift|cs|csproj|json|toml|md|yml|yaml|xaml|props)', loc)
    num_files = len(set(files))
    
    # Architectural indicators
    arch_keywords = ["god class", "god module", "monolith", "kitchen-sink", "refactor",
                     "architecture", "no shared protocol", "duplicated between", "circular dependency",
                     "no repository", "no viewmodel", "no mocking", "no benchmark",
                     "3,425-line", "2,354-line", "1,319-line", "1,289-line", "1,092-line",
                     "1,133-line", "1,173-line", "1,068-line", "1,698-line", "1,295-line",
                     "63 @published", "80+ files"]
    is_arch = any(kw in desc or kw in block[:500] for kw in arch_keywords)
    
    # Multi-file indicators
    multi_file_keywords = ["cross-platform", "all 3 platforms", "macos and windows", 
                           "linux and windows", "swift and c#", "7 files",
                           "across 3", "across 7", "15 files", "34 locations",
                           "6 linux", "3 separate implementations"]
    is_multi = any(kw in desc or kw in block[:500] for kw in multi_file_keywords)
    
    # Simple fixes
    simple_keywords = ["rename", "typo", "comment", "naming", "documentation", "text",
                       "config change", "sort_keys", "remove", "add documentation"]
    is_simple = any(kw in desc for kw in simple_keywords)
    
    if is_arch:
        return 1.0  # Architectural refactor
    elif is_multi or num_files >= 4:
        return 0.6  # Multi-file coordinated change
    elif num_files >= 2:
        return 0.6  # Multi-file
    elif is_simple:
        return 0.1  # Config/comment/text change
    elif num_files == 1:
        return 0.3  # Single-file code change
    else:
        return 0.3  # Default: single-file change

def score_blast_radius(entry):
    """Score blast radius based on heuristics."""
    desc = entry["description"].lower()
    block = entry["block"].lower()[:2000]
    domain = entry["domain"]
    
    # Platform-wide indicators
    platform_wide_keywords = ["cross-platform", "all platforms", "platform-wide", 
                              "cross-platform parity", "3 separate implementations",
                              "macos and windows", "linux and windows",
                              "swift and c#", "across all"]
    is_platform_wide = any(kw in desc or kw in block[:500] for kw in platform_wide_keywords)
    
    # Cross-module indicators
    cross_module_keywords = ["multiple modules", "cross-module", "circular dependency",
                             "shared", "common", "affects all", "every endpoint",
                             "kitchen-sink", "80+ files", "7 files",
                             "all 6 section", "all 6 adapters", "across 3",
                             "across 7", "6 linux viewmodels", "15 files",
                             "34 locations", "all 3 platforms"]
    is_cross_module = any(kw in desc or kw in block[:500] for kw in cross_module_keywords)
    
    # Single module indicators
    single_module_keywords = ["module", "engine", "synthesizer", "resolver", "detector",
                              "viewmodel", "parser", "monolith", "god class", "god module"]
    is_single_module = any(kw in desc or kw in block[:500] for kw in single_module_keywords)
    
    # Test/infrastructure (lower blast radius for production)
    is_test_infra = any(kw in desc for kw in ["test", "coverage", "quarantine", "benchmark",
                                                "ci ", "ci/", "changelog", "generated artifact"])
    
    # Security/verdict issues are inherently high blast radius
    is_security_critical = domain in ("C", "E") and any(kw in desc for kw in [
        "verdict", "evidence", "forge", "sign", "hmac", "key", "auth", "crypto",
        "ed25519", "cve", "vulnerability", "spoof", "tamper", "bypass"
    ])
    
    if is_platform_wide or is_security_critical:
        return 1.0  # Platform-wide or security-critical
    elif is_cross_module:
        return 0.6  # Cross-module
    elif is_single_module:
        return 0.3  # Single module
    elif is_test_infra:
        return 0.3  # Test infrastructure
    else:
        return 0.1  # Single function default

def build_dependencies(entries):
    """Build dependency graph from cross-references."""
    id_set = {e["id"] for e in entries}
    
    deps = {}
    for entry in entries:
        did = entry["id"]
        refs = entry["cross_refs"]
        blocks = []
        blocked_by = []
        
        for ref in refs:
            if ref in id_set:
                # Mutual reference — determine direction
                # If ref is mentioned in this entry's cross-ref, this entry is related to ref
                # We mark both as having a relationship
                blocked_by.append(ref)
        
        # Check if other entries reference this one
        for other in entries:
            if other["id"] != did and did in other["cross_refs"]:
                if other["id"] not in blocks:
                    blocks.append(other["id"])
        
        deps[did] = {
            "blocks": blocks,
            "blocked_by": blocked_by
        }
    
    return deps

def main():
    text = read_deltas()
    raw_entries = parse_entries(text)
    
    # Score all entries
    results = []
    for entry in raw_entries:
        scoring = score_entry(entry)
        results.append({
            "id": entry["id"],
            "description": entry["description"],
            "severity": entry["severity"],
            "status": entry["status"],
            "platform": entry["platform"],
            "domain": entry["domain"],
            "location": entry["location"][:200] if entry["location"] else "",
            "cross_refs": entry["cross_refs"],
            "score": scoring["composite"],
            "tier": scoring["tier"],
            "factors": scoring["factors"]
        })
    
    # Build dependencies
    deps = build_dependencies(raw_entries)
    for r in results:
        r["dependencies"] = deps.get(r["id"], {"blocks": [], "blocked_by": []})
    
    # Summary
    tier_counts = {"MUST FIX": 0, "SHOULD FIX": 0, "COULD FIX": 0, "WON'T FIX": 0}
    for r in results:
        tier_counts[r["tier"]] = tier_counts.get(r["tier"], 0) + 1
    
    output = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "filters": {"domain": None, "platform": None, "branch": None},
        "deltas": results,
        "summary": {
            "total": len(results),
            "must_fix": tier_counts["MUST FIX"],
            "should_fix": tier_counts["SHOULD FIX"],
            "could_fix": tier_counts["COULD FIX"],
            "wont_fix": tier_counts["WON'T FIX"]
        }
    }
    
    os.makedirs(os.path.dirname(OUTPUT_PATH), exist_ok=True)
    with open(OUTPUT_PATH, "w", encoding="utf-8") as f:
        json.dump(output, f, indent=2, ensure_ascii=False)
    
    print(f"Wrote {len(results)} deltas to {OUTPUT_PATH}")
    print(f"Summary: {tier_counts}")
    print(f"Total: {len(results)}")

if __name__ == "__main__":
    main()
