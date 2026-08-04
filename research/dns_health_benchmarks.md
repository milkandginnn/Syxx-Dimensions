# DNS Health Benchmarks

Benchmarking DNS performance, configuration, and security is critical to separate network outages or performance degradation from active DNS tampering or hijacking.

## Performance Metrics

1. **Resolution Time (Latency)**
   - **Excellent:** <15 ms (local cache or near ISP resolver).
   - **Good:** 15–50 ms.
   - **Acceptable:** 50–120 ms.
   - **Poor:** >120 ms.
   - *Note:* Performance metrics are highly dependent on geographic region and network interface type (Wi-Fi vs Cellular vs Ethernet).

2. **Availability & Packet Loss**
   - **Target:** 100% availability.
   - **Degraded:** Any non-zero query packet loss over 5 consecutive trials indicates transport congestion or active firewall drop (e.g. state-sponsored DNS drop).

## Security Benchmarks

1. **DNSSEC Status**
   - **Secure:** Cryptographic chain of trust (DS, DNSKEY, RRSIG) successfully verified back to root.
   - **Bogus:** Signature failed validation (indicates active on-path spoofing attempt or extreme clock drift).
   - **Unsigned:** No signatures present (cannot verify origin, open to spoofing).

2. **Encryption Status**
   - **Encrypted:** DNS-over-HTTPS (DoH), DNS-over-TLS (DoT), or DNS-over-QUIC (DoQ) configured.
   - **Unencrypted:** Plaintext port 53 (Do53) used (subject to path snooping, hijacking, and ISP filtering).
