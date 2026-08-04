# Interpreting DNS Query Captures for Security

DNS traffic analysis is a core pillar of modern network security and threat detection. Attackers often exploit DNS to facilitate communication, locate command-and-control servers, or exfiltrate data, since DNS traffic is almost universally permitted across firewalls.

## Key Indicators of Compromise (IoCs) in DNS captures

1. **Domain Generation Algorithms (DGA)**
   - High volume of queries to randomized, high-entropy domain names (e.g., `axz123-qwe.com`).
   - High percentage of NXDOMAIN responses, as malware attempts to contact non-existent domains until it hits an active seed.
   
2. **DNS Tunneling**
   - **Exfiltration payload:** Encoded in subdomain labels.
   - **Length:** Long labels (often >52 characters) or long total query names (up to the 255-character protocol limit).
   - **Entropy:** Encoded strings (Base64/Base32/Hex) exhibit significantly higher informational entropy than human-readable domains.
   - **Anomalous Record Types:** Abuse of `TXT`, `NULL`, or `CNAME` records to transport large arbitrary payloads in responses.

3. **Beaconing Behavior**
   - Regular, automated checks to specific resolving endpoints at fixed time intervals (e.g., periodic polling every 5 seconds, 1 minute, or 1 hour).
   - Characterized by a low coefficient of variation in query inter-arrival times.

4. **Response Code Anomalies (RCODE)**
   - Elevated rates of errors such as `REFUSED` or `SERVFAIL` indicate structural problems or policy-based redirection.

## Recommended Analysis Flow

- **Payload Analysis:** Audit query length, character distribution (Shannon entropy), and encoding structures.
- **Time-Series Analysis:** Map query frequency over time to locate automated beacon loops.
- **Origin Attestation:** Match socket states with system process IDs (e.g., using `lsof` or connection-to-pid maps) to discover which binary is issuing the query.
