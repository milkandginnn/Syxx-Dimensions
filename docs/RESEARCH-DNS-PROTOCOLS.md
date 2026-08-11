# Encrypted DNS Protocol Research Report

**Date:** 2026-07-26  
**Purpose:** Deep-dive analysis of existing encrypted DNS protocols, their strengths, weaknesses, and gaps, to inform the design of a new protocol.

---

## 1. DNSCrypt (v2)

### Mechanism

DNSCrypt wraps unmodified DNS wire-format traffic in a cryptographic construction using X25519 key exchange, Ed25519 signatures, and XChaCha20-Poly1305 (or XSalsa20-Poly1305) authenticated encryption. The protocol operates in two phases: (1) an initial certificate retrieval via unauthenticated DNS TXT query to obtain short-term server public keys signed by a pre-configured Ed25519 provider key, and (2) ongoing query/response encryption using ephemeral client key pairs. It runs on UDP and TCP, defaulting to port 443. A post-quantum variant (PQDNSCrypt, ES version 0x0003) uses the hybrid X-Wing KEM (ML-KEM-768 + X25519) with HKDF-SHA256 key derivation.

### Stamp Format

DNS stamps (`sdns://` URIs) encode all connection parameters as a single base64url-encoded binary blob. The binary payload begins with a protocol identifier byte (`0x01` for DNSCrypt) followed by:

- **props**: 64-bit little-endian flags (DNSSEC=1, NoLogs=2, NoBlocking=4)
- **addr**: length-prefixed server address string (IP:port)
- **pk**: length-prefixed Ed25519 provider public key (32 bytes)
- **providerName**: length-prefixed provider name string

For DoH (`0x02`), DoT (`0x03`), and DoQ (`0x04`), stamps additionally carry SHA256 certificate hashes and SNI hostnames instead of raw public keys. Anonymized relay stamps use `0x81` and contain only the address.

### Security

- Encrypts and authenticates all query/response traffic, preventing eavesdropping and MITM tampering.
- Does **not** rely on Certificate Authorities; clients explicitly trust the provider's Ed25519 signing key.
- Padding to 64-byte multiples mitigates packet-size traffic analysis.
- Anti-amplification: encrypted responses are constrained to be no larger than the corresponding encrypted query.
- Server keys rotate at least every 24 hours; clients generate ephemeral keys per query.
- Anonymized DNSCrypt extension (2019) allows relay-based operation where relays forward encrypted traffic without decrypting, hiding client IPs from resolvers.
- No known CVEs or practical attacks against the cryptographic constructions as of 2023.

### Cons

- **Port conflict:** Shares port 443 with HTTPS. While multiplexing is theoretically possible, it typically requires separate server instances.
- **TCP fallback:** Large responses over UDP require a TC-flag truncation and TCP retry, adding latency.
- **No CA-based authentication:** While this eliminates CA dependency, it also means key distribution relies on out-of-band trust (stamps, config files). No revocation mechanism beyond certificate expiry.
- **Stateless design:** Every query requires a full key exchange, preventing connection reuse optimizations (mitigated in PQDNSCrypt via session tickets).
- **Limited standardization:** Not an IETF RFC (it is an Internet-Draft, draft-denis-dprive-dnscrypt). Adoption is concentrated in the privacy-enthusiast community.
- **No connection migration:** Stateless design means no persistent session to migrate.
- **Relay trust model:** Anonymized DNSCrypt requires trusting that the relay and resolver do not collude.

### Server Requirements

- UDP and/or TCP listener on port 443 (or configured alternative).
- Ed25519 signing key pair (provider key, ideally stored in HSM).
- Short-term X25519 key pair, rotated every 24 hours.
- Certificate TXT record serving (`<major>.dnscrypt-cert.<zone>`).
- Padding implementation (ISO/IEC 7816-4: `0x80` + NUL fill).
- For Anonymized DNSCrypt: relay infrastructure.

### Traffic Analysis Resistance

- Good: fixed-block padding (64-byte multiples) obscures query content length.
- Queries padded to minimum 256 bytes over UDP to prevent amplification.
- Timing analysis still possible: query timing patterns can fingerprint browsing behavior.
- Anonymized DNSCrypt adds IP-level privacy but does not address timing correlation.

### Performance

- Low latency for UDP: single round trip (no TLS handshake required per query after certificate retrieval).
- Certificate retrieval adds initial overhead (one unauthenticated DNS query).
- TCP fallback for large responses adds significant latency.
- PQDNSCrypt's X-Wing KEM adds 1120-byte ciphertext overhead per query without session tickets.

### Adoption

- Moderate adoption among privacy-focused users and resolvers.
- Supported by dnscrypt-proxy (popular client), Quad9, AdGuard, and various community resolvers.
- Not natively supported by major operating systems (requires third-party clients).
- Concentrated in the DNS privacy community; not mainstream.

---

## 2. DNS over HTTPS (DoH) — RFC 8484

### Mechanism

DoH maps DNS query/response pairs directly to HTTP exchanges over HTTPS. The DNS wire-format message is sent as the HTTP message body (POST) with Content-Type `application/dns-message`, or as a base64url-encoded `dns` query parameter (GET). HTTP/2 is the recommended minimum transport, providing stream multiplexing, header compression (HPACK), and reordering. Servers must implement both POST and GET methods. The DNS ID field should be set to 0 since HTTP correlates requests independently.

### Security

- Confidentiality and integrity provided by TLS (HTTPS).
- Server authentication uses standard X.509 certificate verification via the CA trust chain.
- Eliminates UDP amplification attacks (uses TCP).
- Traffic blends with standard HTTPS web traffic on port 443, making protocol-level blocking difficult.
- DNSSEC validation remains separate and independent; DoH provides transport security, not data integrity.

### Cons

- **Port 443 conflation:** DNS traffic is indistinguishable from web traffic at the network level, making selective blocking impossible without blocking all HTTPS. This is simultaneously a feature (censorship resistance) and a problem (network administrators cannot selectively manage DNS).
- **HTTP overhead:** Full HTTP stack (headers, cookies, content negotiation) adds overhead compared to raw DNS. HTTP semantics like cookies, User-Agent, and Accept-Language create correlation vectors absent in plain DNS.
- **Centralized CA trust:** Server authentication depends entirely on the Certificate Authority system, which is a known attack surface (rogue CAs, government-compelled certificates).
- **Traffic analysis via timing/size:** Despite TLS encryption, query timing and encrypted packet sizes leak information. HTTP/2 padding is available but rarely implemented at sufficient granularity.
- **Split DNS bypass:** DoH circumvents local network DNS policies (split-horizon DNS, DNS64, corporate DNS) unless specifically configured.
- **Bootstrap deadlocks:** Resolving the DoH server's hostname via DoH creates circular dependencies. TLS certificate revocation (OCSP) can also deadlock.
- **Caching complexity:** HTTP caches do not understand DNS semantics. Cache-Control max-age must be manually synchronized with DNS TTLs.
- **Server sees everything:** The DoH server has complete visibility into all client queries and can link them to client IPs. No built-in privacy partitioning.
- **Browser integration issues:** Browsers implementing DoH independently of the OS create shadow DNS paths that bypass system configuration.

### Server Requirements

- HTTPS server with HTTP/2 support.
- Valid TLS certificate from a trusted CA.
- DNS resolution backend (recursive or forwarding).
- Cache-Control header management aligned with DNS TTLs.
- OCSP stapling recommended to prevent deadlock.

### Traffic Analysis Resistance

- Good: blends with HTTPS traffic on port 443, making protocol identification difficult.
- Poor: timing and size side channels remain. Encrypted packet sizes correlate with domain name lengths. HTTP/2 padding is rarely used effectively.
- Server-side: complete visibility into query patterns per client IP.
- No built-in mechanism for hiding client IP from the resolver.

### Performance

- Connection reuse via HTTP/2 multiplexing reduces per-query overhead after initial connection.
- Initial connection: TCP handshake (1 RTT) + TLS handshake (1-2 RTTs) = 2-3 RTTs before first query.
- TLS session resumption and TCP Fast Open can reduce to 1 RTT for subsequent connections.
- HTTP/2 stream multiplexing enables parallel queries on a single connection.
- GET method enables HTTP cache integration; POST is more bandwidth-efficient.
- Latency generally competitive with DoT once connection is established.

### Adoption

- Widely adopted. Natively supported by Firefox, Chrome, Edge, Safari.
- Major providers: Cloudflare (1.1.1.1), Google (8.8.8.8), Quad9 (9.9.9.9), NextDNS.
- Android 10+ supports DoH; iOS 14+ via configuration profiles.
- Estimated significant percentage of DNS traffic from browsers uses DoH.

---

## 3. DNS over HTTPS/3 (DoH3)

### Mechanism

DoH3 is DoH transported over HTTP/3, which uses QUIC (UDP-based, TLS 1.3-integrated transport) instead of TCP. The HTTP-level semantics remain identical to RFC 8484: DNS wire-format messages are exchanged as HTTP request/response pairs. The difference is purely at the transport layer: QUIC provides native stream multiplexing, 0-RTT connection establishment, and connection migration.

### Improvements Over DoH

- **0-RTT:** QUIC allows data to be sent during session resumption, reducing first-query latency to near zero for returning clients.
- **No head-of-line blocking:** Packet loss on one QUIC stream does not stall other streams, unlike TCP where a single lost packet blocks all multiplexed HTTP/2 streams.
- **Connection migration:** QUIC connection IDs allow sessions to persist across network changes (WiFi to cellular), maintaining low latency.
- **Integrated TLS:** QUIC's TLS 1.3 handshake is integrated with the transport handshake, reducing connection setup to 1 RTT (or 0 RTT for resumption) vs. 2-3 RTTs for TCP+TLS.
- **User-space implementation:** QUIC runs in user space over UDP, enabling faster iteration and deployment of transport improvements.

### Cons

- **Port 443 conflation persists:** Same as DoH: DNS traffic mixes with HTTPS web traffic, preventing selective management.
- **QUIC fingerprinting:** QUIC's Initial packets, version negotiation, and token frames create distinctive patterns that network classifiers can identify. Middleboxes can distinguish DoH3/QUIC from other traffic.
- **Centralized CA trust:** Same X.509/CA dependency as DoH.
- **UDP blocking:** Some networks block or throttle non-standard UDP traffic, particularly on port 443. QUIC on port 443 may be treated differently from TCP 443.
- **Server complexity:** QUIC server implementations are less mature and more resource-intensive than TCP+TLS stacks.
- **Privacy trade-offs with connection migration:** Long-lived QUIC sessions and connection migration create linkability risks. Session resumption tickets can be used to track clients across IP changes.
- **Limited server support:** Fewer DNS providers offer HTTP/3 endpoints compared to HTTP/2.

### Server Requirements

- QUIC/HTTP3-capable server.
- Valid TLS certificate (same as DoH).
- DNS resolution backend.
- QUIC-specific: address validation (Retry packets, NEW_TOKEN frames) to mitigate amplification.

### Traffic Analysis Resistance

- Similar to DoH: traffic blends with HTTP/3 web traffic on port 443.
- QUIC Initial packets are partially visible (connection IDs, version) but payload is encrypted.
- Timing and size analysis still applicable.
- Connection migration can increase linkability if not carefully managed.

### Performance

- Superior to DoH for initial connection: 1 RTT (QUIC+TLS integrated) vs. 2-3 RTTs (TCP+TLS).
- 0-RTT for session resumption approaches UDP-like latency.
- No head-of-line blocking improves tail latency under lossy conditions.
- Connection migration eliminates reconnection overhead on network changes.
- Cloudflare benchmarks show QUIC-based DNS consistently outperforming TCP-based alternatives.

### Adoption

- Growing but still limited compared to DoH over HTTP/2.
- Android 11+ supports DoH3 (with July 2022 update).
- Cloudflare, Google, and some other major providers serve HTTP/3 endpoints.
- Server-side adoption depends on QUIC/HTTP3 server maturity (Caddy, nginx-quic, quiche, msquic).
- Expected to grow as QUIC deployment accelerates across the web.

---

## 4. DNS over TLS (DoT) — RFC 7858

### Mechanism

DoT wraps DNS wire-format messages in a TLS session on dedicated TCP port 853. No cleartext DNS is permitted on this port, even after a failed TLS handshake. Messages use the two-octet length prefix from DNS-over-TCP (RFC 1035). The TLS handshake is immediate upon TCP connection establishment (no STARTTLS-like signaling).

### Security

- Confidentiality and integrity via TLS encryption.
- Two privacy profiles:
  - **Opportunistic:** Encrypts when possible, no server authentication. Vulnerable to active MITM.
  - **Strict/Key-Pinned:** Requires SPKI fingerprint verification. Prevents MITM and downgrade attacks.
- Key pinning via SHA-256 hash of DER-encoded SPKI (Subject Public Key Info).
- Downgrade resistance: clients track servers that fail TLS and avoid retrying for ~1 hour.
- DNSSEC remains independent; DoT provides transport privacy, not data integrity.

### Cons

- **Dedicated port 853:** Trivially blockable by firewalls and censors. Unlike DoH, DoT traffic can be identified and blocked by port alone.
- **No 0-RTT:** TLS 1.3 over TCP requires at minimum 1 RTT for session resumption (TCP handshake + TLS resumption). No equivalent to QUIC's 0-RTT data.
- **TLS fingerprinting:** TLS ClientHello parameters (cipher suites, extensions, ALPN) create fingerprints identifiable by middleboxes. JA3/JA4 fingerprinting can distinguish DoT clients.
- **No connection migration:** TCP connections are bound to the IP/port tuple. Network changes (WiFi to cellular) require full reconnection.
- **Head-of-line blocking:** TCP-based: a single lost packet stalls all multiplexed DNS queries until retransmission.
- **Resource overhead:** Per-connection TCP+TLS state (buffers, TLS session) increases server memory usage compared to stateless UDP.
- **Bootstrapping in captive portals:** Strict/key-pinned profiles may fail in captive portal environments where DNS interception is expected.
- **Limited to stub-to-recursive:** RFC 7858 scope explicitly excludes recursive-to-authoritative traffic.

### Server Requirements

- TCP listener on port 853.
- TLS certificate (for opportunistic) or SPKI pinning infrastructure (for strict).
- Connection management (idle timeouts, concurrent connection limits).
- TLS session resumption support (RFC 5077) recommended.
- TCP Fast Open (RFC 7413) recommended for latency reduction.

### Traffic Analysis Resistance

- Poor: dedicated port makes protocol identification trivial.
- Timing and size analysis still applicable despite encryption.
- EDNS(0) Padding (RFC 7830) available but noted as potentially insufficient against sophisticated classifiers.
- No IP-level privacy: server sees client IP for all queries.

### Performance

- Connection establishment: TCP handshake (1 RTT) + TLS handshake (1-2 RTTs) = 2-3 RTTs.
- Session resumption: TCP (1 RTT) + TLS resumption (1 RTT) = 2 RTTs.
- TCP Fast Open can reduce initial overhead.
- Query pipelining: clients should pipeline multiple queries over a single TLS session.
- Generally comparable to DoH once connection is established, but without HTTP/2 optimization benefits.
- Higher per-query latency than DoQ for burst workloads due to head-of-line blocking.

### Adoption

- Broadly supported by major resolvers: Cloudflare, Google, Quad9, NextDNS.
- Native OS support: Android 9+ (enabled by default), iOS 14+ (via profiles).
- Server software: BIND 9.17+, Unbound, Knot, DNSDist.
- Client software: systemd-resolved, stubby, getdns.
- Well-established but facing competition from DoH and DoQ.

---

## 5. DNS over QUIC (DoQ) — RFC 9250

### Mechanism

DoQ maps DNS directly to QUIC streams without an intermediary protocol. Each query/response uses a separate client-initiated bidirectional QUIC stream. Messages are framed with a 2-octet length field (identical to DNS-over-TCP). The DNS ID must be 0 (QUIC stream ID provides correlation). ALPN token `"doq"` negotiates support during the TLS handshake. Default port is UDP 853; port 443 may be used by mutual agreement.

### Improvements Over DoT

- **0-RTT:** Clients can send DNS queries during QUIC session resumption, approaching zero-latency for returning clients. Only replayable transactions (QUERY, NOTIFY) permitted in 0-RTT.
- **Stream multiplexing:** No head-of-line blocking. Multiple queries on one connection are delivered independently; packet loss stalls only the affected stream.
- **Integrated TLS 1.3:** QUIC handshake includes TLS, reducing setup to 1 RTT (vs. 2-3 for TCP+TLS).
- **Connection migration:** QUIC connection IDs allow sessions to persist across network changes.
- **Amplification mitigation:** QUIC address validation limits amplification to 3x via Retry packets and NEW_TOKEN frames.

### Cons

- **QUIC fingerprinting on dedicated ports:** Running QUIC on port 853 makes it trivially identifiable. Even on port 443, QUIC's distinctive Initial packet format is fingerprintable.
- **Limited deployment:** Fewer servers and clients support DoQ compared to DoH or DoT. Server implementations are less mature.
- **Padding challenges:** Must use either EDNS(0) Padding or QUIC packet-level padding. Both add implementation complexity, and QUIC packet-level padding is less standardized.
- **Server complexity:** QUIC server implementations require more CPU and memory than TCP+TLS. Flow control, stream management, and address validation add operational overhead.
- **Replay risk with 0-RTT:** Adversaries can replay 0-RTT queries and observe resolver behavior to infer query content.
- **Privacy trade-offs:** Long-lived sessions and connection migration create linkability risks. Session resumption tickets can track clients across IP changes.
- **No server-initiated transactions:** Cannot support DNS Stateful Operations (DSO) which require in-order delivery guarantees.
- **Message size cap:** 65,535 bytes (2-octet length field), regardless of QUIC's capacity for larger payloads.
- **Middlebox interference:** Some networks block or throttle non-standard UDP traffic, particularly on port 853.

### Server Requirements

- QUIC-capable server (e.g., quic-go, msquic, quiche).
- TLS certificate.
- ALPN negotiation for `"doq"`.
- Address validation infrastructure (Retry packets, NEW_TOKEN frames).
- Stream management and flow control.
- Specific DoQ error codes (DOQ_REQUEST_CANCELLED, DOQ_EXCESSIVE_LOAD).
- AXFR/IXFR support for zone transfers (optional).

### Traffic Analysis Resistance

- Moderate: QUIC encrypts most header fields, but Initial packets expose connection IDs and version information.
- Port 853 makes protocol identification trivial.
- EDNS(0) Padding and QUIC packet-level padding mitigate size-based analysis.
- Timing analysis remains applicable.
- 0-RTT queries may have distinctive patterns observable to network monitors.

### Performance

- Best-in-class for latency: 1 RTT initial, 0-RTT for resumption.
- No head-of-line blocking improves tail latency under lossy conditions.
- Stream multiplexing enables high-throughput parallel queries.
- Connection migration eliminates reconnection overhead.
- Cloudflare and AdGuard report DoQ performance competitive with or superior to DoT.

### Adoption

- Growing but still limited. AdGuard is the most prominent DoQ adopter (both client and server).
- RFC 9250 published May 2022; still relatively new.
- Server software: AdGuard DNS, unbound (experimental), Knot (planned).
- Client software: AdGuard Home, dnsmasq (experimental).
- Not natively supported by major operating systems yet.
- Expected to grow as QUIC ecosystem matures.

---

## 6. Cross-Protocol Gap Analysis: Common Cons Across ALL Encrypted DNS Protocols

### Centralization Pressure

- **Problem:** Very few organizations operate encrypted DNS resolvers at scale (Cloudflare, Google, Quad9, NextDNS, Comcast). The cryptographic and operational complexity of running encrypted resolvers creates a barrier to entry, concentrating DNS resolution in a handful of providers.
- **Impact:** Centralization undermines the distributed nature of DNS. A single provider outage or policy change affects millions of users. Censors can target known encrypted resolver IPs.
- **No protocol addresses this:** All five protocols assume a client-server model with no mechanism for distributed or decentralized resolver discovery.

### Traffic Analysis Attacks

- **Problem:** Even with encryption, query timing and encrypted packet sizes create fingerprints. Research demonstrates that domain names can be inferred from encrypted DNS traffic using statistical analysis of packet sizes and inter-arrival times.
- **Partial mitigations:** DNSCrypt pads to 64-byte multiples. DoH/DoT can use EDNS(0) padding. DoQ supports QUIC packet-level padding. None of these are universally deployed or sufficiently granular.
- **Timing remains exposed:** No protocol addresses timing-based fingerprinting. Constant-rate querying defeats timing analysis but introduces unacceptable latency.
- **Query volume correlation:** Patterns of query bursts correlate with user behavior (page loads, app launches) regardless of encryption.

### No Query-Level Authentication

- **Problem:** The server trusts the client by IP address only. The client trusts the server by certificate only. There is no mechanism for:
  - Per-query authentication (proving a specific user authorized a specific query).
  - Query-level access control (different users getting different answers based on policy).
  - Non-repudiation (proving a query was or was not made).
- **Impact:** Any client reaching the server gets the same service. No way to implement per-user policies at the protocol level (NextDNS works around this via hostname-embedded configuration IDs, but this is an application-layer hack, not a protocol feature).

### No Built-In Filtering/Policy Layer

- **Problem:** All protocols are pure transport: they move DNS queries and responses without any standardized mechanism for filtering, policy enforcement, or content classification.
- **Impact:** Providers implement filtering as proprietary server-side logic (NextDNS, AdGuard). There is no standard protocol for a client to request filtered vs. unfiltered responses, or to negotiate policy at connection time.
- **RPZ (Response Policy Zones) is separate:** While RPZ provides a standardized zone-file format for filtering policies, it is an Unbound/BIND feature, not a protocol-level capability.

### Multi-Server Requirements for High Availability

- **Problem:** All protocols assume a single client-server connection. High availability requires the client to manage multiple server endpoints, failover logic, and health checking.
- **Impact:** No protocol-level mechanism for server discovery, load balancing, or failover. Clients must implement these externally.
- **Anycast helps but isn't protocol-level:** Major providers use anycast for geographic distribution, but this is a network-layer technique, not a protocol feature.

### Certificate Management Burden

- **Problem:** DoH, DoT, DoH3, and DoQ all depend on X.509 certificates and the CA trust chain. Certificate management (issuance, renewal, revocation, pinning) is a significant operational burden.
- **DNSCrypt avoids this** by using raw Ed25519 keys, but this creates a different trust distribution problem.
- **Impact:** Certificate expiration causes outages. CA compromises undermine trust. OCSP/CRL checking adds latency and complexity. Let's Encrypt has reduced cost but not complexity.

### No Proof of Answer Integrity

- **Problem:** No protocol provides a mechanism for the server to prove it didn't lie about the answer. DNSSEC provides data origin authentication and integrity, but it is a separate protocol that must be validated independently.
- **Impact:** A malicious or compromised encrypted DNS server can return any answer, and the client has no way to verify it beyond DNSSEC validation (which many resolvers don't perform, and many domains don't sign).
- **Trust gap:** The encrypted connection protects against network-level tampering but creates a trust bubble around the resolver itself.

### Additional Cross-Protocol Gaps

- **No standard for query logging/auditing:** No protocol provides a way for clients to verify what the server logged, or to request non-logging with cryptographic assurance.
- **No query routing hints:** No protocol allows clients to suggest which upstream servers the resolver should use, or to verify which path the query took.
- **No support for multi-party computation:** No protocol enables split-trust models natively (ODoH is an extension, not a base protocol feature).
- **No post-quantum readiness:** Only DNSCrypt (PQDNSCrypt draft) addresses post-quantum key exchange. All TLS-based protocols depend on algorithms vulnerable to future quantum computers.

---

## 7. NextDNS Architecture

### Per-User Filtering Mechanism

NextDNS achieves per-user configurability through **configuration IDs embedded in DNS hostnames**. Each user creates a configuration (profile) that receives a unique hex-encoded identifier (e.g., `abc123`). This ID is embedded in the DNS endpoint:

- **DoH:** `https://dns.nextdns.io/abc123`
- **DoT:** `abc123.dns.nextdns.io` on port 853
- **DoQ:** `abc123.dns.nextdns.io` on port 853 or 784
- **DNSCrypt:** Provider name includes the config ID
- **Plain DNS:** Specific IP addresses are mapped to configurations server-side

The server parses the configuration ID from the hostname/path at connection time and applies the associated filtering rules to all queries on that connection. This is an application-layer convention built on top of standard encrypted DNS protocols.

### Configurability Model

- **Blocklists:** Curated and community-maintained lists updated in real-time. Includes threat intelligence feeds (Google Safe Browsing, DGA detection, newly registered domains).
- **Allowlists:** Unlimited custom allowlisting to override blocklists.
- **Denylists:** Unlimited custom domain blocking.
- **Rewrites:** Custom DNS response overrides per domain (similar to /etc/hosts but cloud-managed).
- **Parental controls:** Category-based blocking (porn, violence, piracy), SafeSearch enforcement, YouTube Restricted Mode, app/service-specific blocking (TikTok, Fortnite), scheduling ("Recreation Time").
- **Analytics:** Real-time query logs, tracker insights, configurable retention (1 hour to 2 years), selectable data residency (US, EU, UK, Switzerland).

### Encrypted DNS Support

- DoH, DoT, DoQ, and DNSCrypt all supported simultaneously.
- DNSSEC validation performed automatically on all answers.
- The open-source NextDNS CLI client (written in Go) acts as a local DoH proxy for routers and LAN clients, translating standard DNS queries into encrypted upstream requests.

### Single-Tenant vs. Multi-Tenant

- **Multi-tenant infrastructure:** All configurations share the same resolver infrastructure. Filtering is applied per-connection based on the configuration ID.
- **Per-tenant isolation:** Logs, analytics, and configuration are isolated per account. No cross-configuration data leakage.
- **132 edge locations:** Embedded within carrier networks in metropolitan areas to minimize latency ("Pi-hole on the cloud").

### Differentiators vs. Cloudflare 1.1.1.1 / Google 8.8.8.8

| Feature | Cloudflare 1.1.1.1 | Google 8.8.8.8 | NextDNS |
|---|---|---|---|
| Per-user filtering | No (1.1.1.1 for Families only) | No | Yes, unlimited configs |
| Custom blocklists | No | No | Yes |
| Analytics/logs | No-logs policy | Logs (privacy policy) | Configurable retention |
| Parental controls | Basic (1.1.1.1 for Families) | No | Advanced (categories, scheduling) |
| Rewrites | No | No | Yes |
| Data residency | N/A (no logs) | US | US, EU, UK, CH |
| Protocol support | DoH, DoT, DoQ | DoH, DoT | DoH, DoT, DoQ, DNSCrypt |
| Open source client | cloudflared | N/A | Yes (Go-based CLI) |

---

## 8. Unbound Elements Worth Adopting

### Architecture Overview

Unbound is a validating, recursive, caching DNS resolver from NLnet Labs, designed for security, privacy, and open standards compliance. It runs on Linux, BSD, and macOS, and is included in base BSD systems and standard Linux repositories.

### Key Features Worth Incorporating

#### DNSSEC Validation with Chain-of-Trust

- **What:** Full DNSSEC validation from root trust anchors through the entire chain (root → TLD → domain). Trust anchors managed via `unbound-anchor(8)` utility which automates root key rollover.
- **Why it matters:** Proves the authenticity and integrity of DNS answers. The gold standard for "did the server lie?" verification.
- **Design implication:** A new protocol should either integrate DNSSEC validation or provide a mechanism for the resolver to prove answer authenticity without requiring the client to perform separate DNSSEC validation.

#### Aggressive NSEC/NSEC3 (RFC 8198)

- **What:** Uses DNSSEC NSEC/NSEC3 records to prove the non-existence of names not just for the queried name, but for all names covered by the NSEC interval. Enables authoritative "this doesn't exist" answers from cache.
- **Why it matters:** Reduces query volume and improves privacy (fewer queries needed). Enables negative caching with cryptographic proof.
- **Design implication:** Protocol should support negative answers with cryptographic proof of non-existence.

#### Response Policy Zones (RPZ)

- **What:** Standardized, vendor-agnostic filtering using DNS zone files. Supports multiple trigger types (QNAME, Client IP, Response IP, NSDNAME, NSIP) and actions (NXDOMAIN, NODATA, DROP, TCP-Only, PASSTHRU, Local Data).
- **Why it matters:** Provides a standard mechanism for policy distribution. Zones can be fetched via AXFR/IXFR or HTTP for automatic updates. Multi-zone layering with precedence rules.
- **Design implication:** A new protocol could incorporate RPZ-like policy negotiation at connection time, allowing clients to request specific policy profiles.

#### QNAME Minimisation (RFC 9156)

- **What:** Sends only the minimum amount of information in each query to authoritative servers. Instead of sending the full QNAME to every server in the resolution chain, only the labels needed by each server are sent.
- **Why it matters:** Reduces information leakage to authoritative servers. The root server learns only the TLD; the TLD server learns only the second-level domain.
- **Design implication:** Protocol design should consider what information is leaked at each stage of resolution and minimize it.

#### Python Module Interface

- **What:** Allows custom Python code to hook into the resolver pipeline at various stages (query processing, response handling, cache operations). Enables custom filtering, logging, analytics, and policy logic.
- **Why it matters:** Extensibility without modifying core resolver code. Enables rapid prototyping of new features.
- **Design implication:** A new protocol implementation should support a plugin/module system for extensibility.

#### Authority Zones (RFC 7706)

- **What:** Allows loading complete zone copies (e.g., root zone) locally, eliminating the need to query upstream for those zones.
- **Why it matters:** Reduces latency, improves privacy (no upstream queries for root/TLD), and provides resilience against upstream failures.
- **Design implication:** Protocol could support zone pre-loading or pre-warming mechanisms.

#### Modular Design

- **What:** Processing pipeline composed of interchangeable modules (iterator, validator, respip, python, DNS64, ECS, cache DB). Module order determines processing flow.
- **Why it matters:** Clean separation of concerns. Each module can be independently enabled, disabled, or replaced.
- **Design implication:** Protocol implementation should follow a modular pipeline architecture.

---

## 9. Emerging Protocols and Research

### Oblivious DNS over HTTPS (ODoH) — RFC 9230

- **Architecture:** Three-party model (Client → Proxy → Target). Client encrypts queries with the Target's public key (via HPKE). Proxy sees client IP but not query content. Target sees query content but only proxy IP.
- **Privacy guarantee:** No single entity possesses both client identity and query content. Requires non-collusion of proxy and target.
- **Performance:** Additional encryption overhead <1ms at p99 (Cloudflare measurement). Median latency ~228ms vs. ~146ms for standard DoH.
- **Deployment:** Apple (Private Relay), Cloudflare (1.1.1.1), with proxy partners PCCW, SURF, Equinix.
- **Limitation:** Still built on DoH, inheriting all its cons. Adds latency for the privacy benefit. Requires separate proxy and target infrastructure.

### Post-Quantum DNSCrypt (PQDNSCrypt)

- **Key exchange:** Hybrid X-Wing KEM (ML-KEM-768 + X25519), providing both classical and post-quantum security.
- **Cost:** 1120-byte ciphertext per query without session tickets. Stateless resumption via encrypted tickets mitigates this.
- **Significance:** Only encrypted DNS protocol with post-quantum readiness.

---

## 10. Deployment Statistics and Trends

### Current State (as of 2024-2025)

- Encrypted DNS adoption is concentrated among major providers: Cloudflare, Google, Quad9, NextDNS, Comcast.
- DoH is the most widely deployed protocol, driven by browser integration (Firefox, Chrome, Edge).
- Android 9+ has native DoT support; Android 10+ has DoH; Android 11+ has DoH3.
- iOS 14+ supports DoH and DoT via configuration profiles.
- The majority of DNS traffic globally remains unencrypted (plain UDP port 53).
- Adoption is highest in Western countries and among technically sophisticated users.

### Performance Comparisons (from published research)

| Protocol | Initial Setup | Subsequent Queries | Head-of-Line Blocking |
|---|---|---|---|
| Plain DNS (UDP) | 0 RTT | 1 RTT | No |
| DoT | 2-3 RTT | 1 RTT (pipelined) | Yes (TCP) |
| DoH (HTTP/2) | 2-3 RTT | 1 RTT (multiplexed) | Yes (TCP) |
| DoH3 (HTTP/3) | 1 RTT | 0-1 RTT | No (QUIC) |
| DoQ | 1 RTT | 0-1 RTT | No (QUIC) |
| DNSCrypt (UDP) | 1 RTT (cert) | 1 RTT | No |
| ODoH | 2-4 RTT | 2 RTT | Depends on transport |

### Known Attacks and Vulnerabilities

- **Traffic analysis:** Multiple research papers demonstrate domain inference from encrypted DNS traffic using packet size and timing analysis. No protocol fully mitigates this.
- **ESNI/ECH blocking:** China's Great Firewall blocks encrypted SNI, which can affect DoH connection establishment.
- **DoH browser fingerprinting:** HTTP-level headers (User-Agent, Accept-Language, cookies) create correlation vectors.
- **QUIC fingerprinting:** QUIC Initial packets and version negotiation are observable and can identify DoH3/DoQ traffic.
- **No major CVEs** against the core cryptographic constructions of any protocol. Vulnerabilities have been in implementations, not specifications.

---

## 11. Summary: What a New Protocol Should Address

Based on this analysis, a new encrypted DNS protocol should address the following gaps:

1. **Built-in policy negotiation:** Allow clients to request specific filtering profiles at the protocol level, not just via application-layer hacks (like NextDNS's config IDs).

2. **Answer integrity proof:** Integrate DNSSEC validation or provide an equivalent mechanism for the server to cryptographically prove it returned the authentic answer.

3. **Decentralized trust:** Reduce dependence on centralized CAs (like DNSCrypt's raw key model) while maintaining a practical trust distribution mechanism.

4. **Traffic analysis resistance:** Mandatory, standardized padding (not optional). Consider constant-size queries or query batching to mitigate size-based analysis. Address timing analysis through rate smoothing or cover traffic.

5. **Post-quantum readiness:** Native support for hybrid key exchange (classical + post-quantum) from day one.

6. **Per-user authentication:** Protocol-level support for client authentication that enables per-user policies without relying on IP addresses or hostname hacks.

7. **Split-trust model:** Native support for proxy/relay architectures (like ODoH/Anonymized DNSCrypt) as a protocol feature, not an extension.

8. **Minimal metadata leakage:** Learn from QNAME minimisation: minimize information exposed at every stage of the resolution process.

9. **Connection migration:** QUIC-like session persistence across network changes, but with privacy safeguards (single-use tokens, unlinkable connection IDs).

10. **Server discovery and failover:** Protocol-level mechanism for discovering and switching between resolvers without external configuration.

11. **Auditability:** Mechanism for clients to verify server logging policies, or for servers to provide cryptographic proof of non-logging.

12. **Extensibility:** Plugin/module architecture (like Unbound's) for adding new features without protocol version bumps.

---

*End of research report.*
