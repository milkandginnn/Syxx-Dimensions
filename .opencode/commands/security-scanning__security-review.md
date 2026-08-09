---
description: Comprehensive AI-driven manual security code review — OWASP Top 10, auth, crypto, input validation, config, dependencies
argument-hint: "<target> [--scope files|diff|branch] [--base main]"
---

# Security Review

Deploy a security auditor agent to perform a thorough manual security review of the target code. This is NOT an automated SAST scan — it is an AI-driven audit that reads code, traces data flows, and identifies security vulnerabilities with file:line citations.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `<target>`: file path, directory, git diff range (e.g., `main...HEAD`), or PR number
   - `--scope`: files, diff, or branch (default: diff)
   - `--base`: base branch for diff comparison (default: `main`)

## Phase 1: Scope Resolution

1. Determine what to review:
   - **File/Directory**: Use as-is
   - **Git diff**: Run `git diff {base}...HEAD --name-only` to get changed files
   - **Branch**: Diff against base branch
2. Collect the full diff content for the security auditor
3. Display: "Reviewing {N} files"

## Phase 2: Security Audit

Launch the security-auditor agent with the diff and target context. The agent reviews for:

1. **OWASP Top 10**: Injection, broken auth, sensitive data exposure, XXE, broken access control, security misconfig, XSS, insecure deserialization, vulnerable components, insufficient logging
2. **Input validation**: Missing sanitization, unvalidated redirects, path traversal
3. **Authentication/authorization**: Flawed auth logic, privilege escalation, session management, JWT issues
4. **Cryptographic issues**: Weak algorithms, hardcoded secrets, improper key management, SPKI pinning gaps
5. **Dependency vulnerabilities**: Known CVEs, outdated packages
6. **Configuration security**: Debug mode, verbose errors, permissive CORS, missing security headers

For each finding, the agent provides:
- Severity (Critical / High / Medium / Low)
- File and line location
- Concrete failure scenario
- Specific remediation with code example

## Phase 3: Report

Present findings organized by severity, with an executive summary and recommended action plan.