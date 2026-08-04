# DNS Mismatch and Confidence Scoring

Comparing local resolving paths against remote reference baselines and calculating a statistical confidence score is necessary to implement reliable tamper detection.

## Local and Remote DNS Mismatch Detection

To detect on-path hijacking or censorship, the client compares the answer set of a domain resolved locally via the system DNS server against a remote trusted baseline (e.g. pulled from a public resolver cohort or remote VPS).

### Mismatch Classifications

1. **CDN/GeoDNS Tolerance**
   - Different IP addresses for the same domain do not automatically imply tampering.
   - **Resolution:** Verify if the resolved local IP belongs to the same Autonomous System Number (ASN) or matches CDN/geo-DNS patterns (same canonical name or domain hierarchy).
2. **DNS Hijacking**
   - Divergence in the resolved IP addresses that does not belong to a CDN or shared ASN consensus.
3. **Censorship / Redirection**
   - Resolver injecting alternative IPs (e.g., pointing to local ISP landing pages or national gateway pages).

## Confidence & Decision Scoring

In network diagnostics, confidence scoring indicates how reliable the collected signals are.

### Multiplicative Scoring Heuristics

To avoid errors where a single failed probe sets confidence to zero or subtracts linearly past logical limits:

$$
\text{Confidence} = 100 \times \prod_i (1 - p_i)
$$

Where:
- $p_i$ is the penalty score for each failure.
- $1 - p_i$ is the discount multiplier.

#### Recommended Penalty Values:
- **Probe Timeout / Unreachable:** 0.15
- **Captive Portal Detected:** 0.40
- **Leak Probe Inconclusive:** 0.10
- **Process Attribution Unknown:** 0.05
- **DNSSEC Strip suspected:** 0.30

### Decision Cutoffs & Fallback

As defined by product policy:
- **Low Confidence Gate:** If the confidence score falls below **75%**, the client must fallback to referring the user to human expert review at **r/AskNetSec** or **r/NetSecStudents**.
- **Margin Gate:** If the difference between the most probable hypothesis and the second-most probable hypothesis is less than **30 points**, the result is considered too close to call (ambiguous) and the referral is triggered.
