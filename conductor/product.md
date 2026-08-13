# Product Vision

## Product Overview

**Name:** Syxx

**Tagline:** DNS-integrity detection platform

**Description:**
Syxx is a multi-platform DNS-integrity detection platform. A Python server performs multi-vantage DNSSEC validation and tamper consensus; native desktop clients on macOS (SwiftUI), Windows (WinUI 3), and Linux (Avalonia) probe local DNS resolution and report integrity verdicts back to the server.

## Problem Statement

### The Problem

Users cannot easily tell whether their DNS responses are being tampered with — via rebinding, spoofing, or compromised resolvers. Syxx gives them multi-vantage DNSSEC validation and consensus verdicts so tampering is visible.

### Current Solutions

- Single-vantage DNS lookups and dig-style tools that only check one resolver path.
- Commercial DNS filtering/monitoring products without transparent, verifiable validation.

### Why They Fall Short

They lack cross-vantage consensus, cannot detect edge-vs-authoritative divergence reliably, and do not surface tamper verdicts to end users in a friendly, cross-platform way.

## Target Users

### Primary Users

Security-conscious end users

- **Who:** Individuals and professionals who care about DNS integrity on macOS, Windows, or Linux.
- **Goals:** Know their resolver is honest; get clear, trustworthy verdicts.
- **Pain Points:** Opaque DNS tooling, hard-to-compare multi-resolver results, distrust of resolver behavior.
- **Technical Proficiency:** Comfortable with installers and basic security concepts.

### Secondary Users

Enterprise IT / SecOps teams

- **Who:** Organizations monitoring DNS health on fleet desktops.
- **Goals:** Fleet-wide integrity visibility and incident signal.
- **Relationship to Primary:** Deploy the same clients at scale and rely on the server as ground truth.

## Core Value Proposition

### Key Benefits

1. Multi-vantage DNSSEC validation detects tampering that single-view tools miss.
2. Tamper consensus across resolvers distinguishes isolated glitches from systemic compromise.
3. Native, approachable clients on macOS, Windows, and Linux with consistent behavior.

### Differentiators

- Signed, replay-bound update channel (Ed25519 manifest → SHA-256/size verification before install).
- Cross-platform parity gates enforced by CI for all three desktop clients plus the server.

### Value Statement

> A security product with a disciplined architecture whose verdicts are accurate, verifiable, and trustworthy — with quality gates that are actually enforced.

## Success Metrics

### Key Performance Indicators

| Metric       | Target                         | Measurement Method      |
| ------------ | ------------------------------ | ----------------------- |
| CI gate      | Green & merge-blocking         | `.github/workflows/ci.yml` |
| Server coverage | ≥ 80% on security core     | pytest + `.coverage-honesty.json` |
| Parity        | 4-runtime matrix green         | CI matrix               |
| DNSSEC core   | ≥ 70% module coverage          | per-module coverage     |

### North Star Metric

Trustworthy integrity verdicts — DNSSEC/consensus core fully tested and CI gates binding.

### Leading Indicators

- New tracks include tests before implementation.
- Coverage on `dnssec_validator` / `investigate` rising.

### Lagging Indicators

- Number of failed CI runs on `main`.
- Support issues about wrong verdicts.

## Out of Scope

### Explicitly Not Included

- Mobile clients (iOS/Android).
- A managed cloud offering / multi-tenant SaaS control plane.
- Replacing the user's DNS server or acting as a recursive resolver.

### Future Considerations

- Homebrew / apt / snap distribution polish.
- Fleet reporting dashboards.

### Non-Goals

- Being an authoritative DNS provider.
- Ad-blocking or content-filtering features.
