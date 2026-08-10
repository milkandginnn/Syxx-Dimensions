# Code Review Rule

When asked to perform a `/code-review` or review code, follow this protocol.

## Effort Levels

- **quick**: Single-pass review for correctness, security, code quality, performance
- **medium**: 2 agents parallel — code reviewer + security auditor
- **high**: 4 agents parallel — code, security, architecture, testing/best practices
- **ultra**: Phased multi-agent audit with stateful output files in `.code-review/`

## Flags

| Flag | Effect |
|------|--------|
| `--effort quick\|medium\|high\|ultra` | Review depth (default: `high` for PRs, `medium` for local) |
| `--scope files\|diff\|branch` | What to review (default: `diff`) |
| `--base main` | Base branch for diff comparison |
| `--comment` | Post inline PR comments |
| `--fix` | Apply safe fixes to working tree |
| `--security-focus` | Prioritize security findings |
| `--performance-critical` | Prioritize performance analysis |
| `--strict-mode` | Halt on critical findings, require approval |
| `--framework <name>` | Framework hint for framework-specific checks |

## Review Dimensions

1. **Correctness**: Logic errors, edge cases, race conditions, null safety
2. **Security**: OWASP Top 10, injection, auth/authz, crypto, secrets, input validation
3. **Performance**: N+1 queries, unnecessary allocations, blocking calls, caching gaps
4. **Architecture**: Design patterns, component boundaries, dependency management, SOLID
5. **Code Quality**: Naming, complexity, duplication, error handling, maintainability
6. **Testing**: Coverage gaps, test quality, edge case testing, mock usage
7. **Best Practices**: Language idioms, framework patterns, deprecated APIs, CI/CD

## Output

For each finding: severity (Critical/High/Medium/Low), file:line, failure scenario, specific remediation.

Final verdict: ✅ SHIP / ⚠️ SHIP WITH FIXES / 🛑 DO NOT SHIP