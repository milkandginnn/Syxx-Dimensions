# Syxx Cipher Protocol Specification

**Version:** 0.1.0-draft
**Date:** 2026-07-26
**Status:** Design Specification (Pre-Implementation)
**Authors:** Syxx Project

---

## Table of Contents

1. [Protocol Overview](#1-protocol-overview)
2. [Cryptographic Design](#2-cryptographic-design)
3. [Wire Protocol](#3-wire-protocol)
4. [Stamp Format](#4-stamp-format)
5. [Per-User Policy Engine](#5-per-user-policy-engine)
6. [Answer Integrity](#6-answer-integrity)
7. [Traffic Analysis Resistance](#7-traffic-analysis-resistance)
8. [Server Architecture](#8-server-architecture)
9. [Security Analysis](#9-security-analysis)
10. [Implementation Plan](#10-implementation-plan)
11. [Appendix](#11-appendix)

---

## 1. Protocol Overview

### 1.1 Introduction

Syxx Cipher is an encrypted DNS protocol designed for single-server deployment with
per-user policy enforcement, traffic analysis resistance, and cryptographically verifiable
answer integrity. It builds on the DNSCrypt v2 cryptographic foundation while incorporating
modern handshake patterns from the Noise Protocol Framework, lightweight stream multiplexing,
and protocol-level policy signaling.

The protocol targets three use cases that existing encrypted DNS protocols (DoH, DoT, DoQ,
DNSCrypt) do not adequately address in combination:

1. **Self-hosted encrypted DNS** with per-user filtering and policy, without Certificate
   Authority infrastructure or multi-tenant cloud dependency.
2. **Tamper-evident DNS resolution** where the server provides cryptographic evidence that
   it honestly resolved the query, independent of DNSSEC coverage.
3. **Traffic analysis resistant DNS** where an observer cannot determine which domains are
   being queried through packet size, timing, or frequency analysis.

### 1.2 Design Goals

| ID | Goal | Priority |
|----|------|----------|
| G1 | Confidentiality: all query and response content encrypted in transit | Mandatory |
| G2 | Integrity: all messages authenticated via AEAD; responses additionally signed | Mandatory |
| G3 | Per-user identity: server identifies users cryptographically with zero network leakage | Mandatory |
| G4 | Forward secrecy: compromise of long-term keys does not expose past sessions | Mandatory |
| G5 | Low latency: 1-RTT session establishment; 0-RTT for subsequent queries | Mandatory |
| G6 | Traffic analysis resistance: fixed-size messages, constant-time responses, cover traffic | Mandatory |
| G7 | Answer integrity: DNSSEC pass-through, resolution receipts, signed attestations | Mandatory |
| G8 | Policy enforcement: structured block responses with signed, machine-readable reasons | Mandatory |
| G9 | No PKI dependency: trust established via stamps (out-of-band key distribution) | Mandatory |
| G10 | Post-quantum readiness: hybrid KEM migration path (X-Wing: ML-KEM-768 + X25519) | Desired |
| G11 | Stream multiplexing: parallel queries without head-of-line blocking | Desired |
| G12 | Optional relay: IP-level privacy via anonymized forwarding | Desired |
| G13 | Single-server deployment: operable by an individual on commodity hardware | Mandatory |
| G14 | Integration: compatible with Syxx Dimensions server for leak detection and investigation | Desired |

### 1.3 Non-Goals

| ID | Non-Goal | Rationale |
|----|----------|-----------|
| NG1 | Browser-native support | Syxx Cipher targets dedicated clients (SyxxKit/Syxx.Kit), not web browsers. DoH exists for that use case. |
| NG2 | Multi-tenant cloud operation | The protocol is designed for single-server, single-operator deployment. Multi-tenancy is out of scope. |
| NG3 | Middlebox compatibility | The protocol does not attempt to appear as HTTPS or other traffic. It uses a custom wire format. |
| NG4 | Standard DNS port operation | Syxx Cipher uses a dedicated port (default UDP/TCP 5443) to avoid conflicts with existing services. |
| NG5 | Connection migration | Sessions are bound to the transport endpoint. Clients SHOULD establish new sessions on network changes to prevent linkability. |
| NG6 | Authoritative DNS serving | The server is a recursive resolver. Authoritative zone serving is out of scope. |

### 1.4 Threat Model

**Attacks the protocol prevents:**

| Attack | Mitigation |
|--------|-----------|
| Passive eavesdropping on DNS queries | AEAD encryption (XChaCha20-Poly1305) of all query/response traffic |
| Man-in-the-middle tampering | AEAD authentication + Ed25519 response signatures |
| Server impersonation | Ed25519 provider key pinned in stamp; ephemeral server keys signed by provider key |
| Replay attacks | Client nonce tracking (duplicate rejection); 0-RTT replay window |
| Query content fingerprinting (size) | Fixed-size padding (512B queries, 1024B responses) |
| Query content fingerprinting (timing) | Constant-time response normalization; optional cover traffic |
| Per-user correlation by network observers | Per-user PSK authentication; no user identity in network traffic |
| DNS cache poisoning | AEAD integrity; DNSSEC validation; resolution receipts |
| Selective resolver dishonesty | Resolution receipts with signed delegation chains; multi-path cross-reference |
| Policy bypass by MITM | Ed25519-signed block decisions (client verifies block came from policy engine) |

**Attacks the protocol does NOT prevent:**

| Attack | Limitation |
|--------|-----------|
| Compromised server | A compromised server can return any answer. DNSSEC validation and resolution receipts limit the damage but cannot prevent it for unsigned zones. |
| Global passive adversary | An adversary observing all traffic can perform correlation attacks even with cover traffic. |
| Endpoint compromise | Malware on the client device can read queries before encryption. |
| Colluding relay + server | If both relay and server are controlled by the same adversary, IP-level privacy is lost. |
| DNSSEC-unsigned zone spoofing by server | For zones without DNSSEC, the client must trust the server's answer. Resolution receipts provide an audit trail but not real-time prevention. |
| Denial of service | The protocol does not include specific DoS mitigation beyond rate limiting. |

### 1.5 Comparison with Existing Protocols

| Property | Syxx Cipher | DNSCrypt v2 | DoH (RFC 8484) | DoT (RFC 7858) | DoQ (RFC 9250) |
|----------|-------------|-------------|-----------------|-----------------|-----------------|
| Transport | UDP primary, TCP fallback | UDP, TCP | TCP (HTTP/2) | TCP | UDP (QUIC) |
| Encryption | XChaCha20-Poly1305 | XChaCha20-Poly1305 | TLS 1.2/1.3 | TLS 1.2/1.3 | TLS 1.3 (QUIC) |
| Key exchange | Noise_IK (X25519) | X25519 per-query | TLS ECDHE | TLS ECDHE | TLS ECDHE |
| Server auth | Ed25519 pinned key | Ed25519 pinned key | X.509 CA chain | X.509 CA chain / SPKI pin | X.509 CA chain |
| Client auth | Per-user PSK | None | None | None | None |
| First query latency | 1 RTT | 1 RTT (+cert fetch) | 2-3 RTT | 2-3 RTT | 1 RTT |
| Subsequent query latency | 0 RTT (in-session) | 1 RTT (per-query KE) | 1 RTT | 1 RTT | 0-1 RTT |
| Head-of-line blocking | No (stream mux) | N/A (stateless) | Yes (TCP) | Yes (TCP) | No (QUIC streams) |
| Per-user policy | Protocol-level | None | Application-layer hack | None | None |
| Block signaling | Structured + signed | None | None | None | None |
| Padding | Fixed-size (512B/1024B) | 64-byte multiples | Optional EDNS(0) | Optional EDNS(0) | Optional EDNS(0) / QUIC |
| Constant-time responses | Yes (configurable) | No | No | No | No |
| Cover traffic | Optional | No | No | No | No |
| Answer integrity proof | Receipts + signatures | MAC only | None (DNSSEC separate) | None (DNSSEC separate) | None (DNSSEC separate) |
| Relay support | Protocol-native | Extension | ODoH (separate protocol) | None | None |
| Post-quantum | X-Wing KEM path | PQDNSCrypt (X-Wing) | TLS PQ migration | TLS PQ migration | TLS PQ migration |
| Certificate infrastructure | None (stamps) | None (stamps) | X.509 / CA | X.509 / CA or SPKI | X.509 / CA |
| Port | 5443 (default) | 443 | 443 | 853 | 853 |
| Standardization | None (private) | Internet-Draft | RFC 8484 | RFC 7858 | RFC 9250 |

### 1.6 Architecture Overview

```
                         SYXX CIPHER ARCHITECTURE
                         ========================

  ┌──────────────────────────────────────────────────────────────────┐
  │                        CLIENT (SyxxKit)                          │
  │                                                                   │
  │  ┌──────────┐  ┌────────────┐  ┌───────────┐  ┌──────────────┐  │
  │  │ Stamp    │  │ Noise_IK   │  │ Query     │  │ Response     │  │
  │  │ Parser   │──│ Handshake  │──│ Encrypt   │──│ Decrypt +    │  │
  │  │          │  │ Engine     │  │ + Mux     │  │ Verify       │  │
  │  └──────────┘  └────────────┘  └───────────┘  └──────────────┘  │
  │       │              │               │               │            │
  │       │         ┌────┴────┐     ┌────┴────┐    ┌────┴────┐      │
  │       │         │ Session │     │ Stream  │    │ Receipt │      │
  │       │         │ Key     │     │ Manager │    │ Verifier│      │
  │       │         │ Store   │     │         │    │         │      │
  │       │         └─────────┘     └─────────┘    └─────────┘      │
  │       │              │               │               │            │
  └───────┼──────────────┼───────────────┼───────────────┼────────────┘
          │              │               │               │
          │     ┌────────┴───────────────┴───────────────┘
          │     │        UDP (port 5443) / TCP fallback
          │     │
  ┌───────┼─────┼────────────────────────────────────────────────────┐
  │       │     ▼          SERVER (syxx-cipherd)                      │
  │       │  ┌─────────┐  ┌────────────┐  ┌───────────┐             │
  │       │  │ Stamp   │  │ Noise_IK   │  │ Query     │             │
  │       └──│ Issuer  │  │ Handshake  │──│ Decrypt   │             │
  │          │ (PSK    │  │ + PSK      │  │ + Mux     │             │
  │          │  mgmt)  │  │ Auth       │  │           │             │
  │          └─────────┘  └────────────┘  └─────┬─────┘             │
  │                                              │                    │
  │          ┌───────────────────────────────────┤                    │
  │          │              │                    │                    │
  │    ┌─────▼──────┐ ┌────▼─────┐  ┌───────────▼──────────┐        │
  │    │ Policy     │ │ Recursive│  │ Answer Integrity     │        │
  │    │ Engine     │ │ Resolver │  │ Engine                │        │
  │    │ (RPZ,      │ │ (QNAME   │  │ (DNSSEC validation,  │        │
  │    │  blocklist,│ │  min,    │  │  resolution receipts, │        │
  │    │  allowlist)│ │  cache,  │  │  Ed25519 signing,    │        │
  │    │            │ │  aggr.   │  │  multi-path xref)    │        │
  │    └────────────┘ │  NSEC)   │  └──────────────────────┘        │
  │                   └──────────┘                                    │
  │                                                                   │
  │  ┌───────────────────────────────────────────────────────────┐   │
  │  │ Traffic Analysis Resistance Layer                         │   │
  │  │ (fixed-size padding, constant-time normalization,          │   │
  │  │  cover traffic generation)                                 │   │
  │  └───────────────────────────────────────────────────────────┘   │
  │                                                                   │
  │  ┌───────────────────────────────────────────────────────────┐   │
  │  │ Optional: Relay Layer (Anonymized Cipher)                 │   │
  │  │ Relay forwards encrypted packets without decrypting.      │   │
  │  │ Server sees relay IP, not client IP.                      │   │
  │  └───────────────────────────────────────────────────────────┘   │
  │                                                                   │
  └───────────────────────────────────────────────────────────────────┘
```

---

## 2. Cryptographic Design

### 2.1 Cryptographic Primitives

All cryptographic algorithm selections are fixed at the protocol level. Algorithm agility
is deliberately excluded: there is no negotiation of ciphersuites. If a primitive is
found broken, the protocol version is incremented.

| Component | Algorithm | Size | Reference |
|-----------|-----------|------|-----------|
| Key Exchange (classical) | X25519 | 32 bytes public, 32 bytes private | RFC 7748 |
| Key Exchange (post-quantum, Phase 2+) | X-Wing (ML-KEM-768 + X25519) | 1120 bytes ciphertext | draft-connolly-tls-xwing |
| Handshake Protocol | Noise_IK | — | Noise Protocol Framework Rev. 34 |
| AEAD Cipher | XChaCha20-Poly1305 (DJB variant) | 32-byte key, 24-byte nonce, 16-byte tag | draft-irtf-cfrg-xchacha |
| Digital Signatures | Ed25519 | 32 bytes public, 64 bytes signature | RFC 8032 |
| Key Derivation | HKDF-SHA256 | — | RFC 5869 |
| Hash (Noise internal) | BLAKE2s-256 | 32 bytes | RFC 7693 |
| PSK | Random bytes (generated per user) | 32 bytes | — |

**XChaCha20-Poly1305 DJB variant:** Syxx Cipher uses the DJB variant of ChaCha20
(as used in DNSCrypt v2), not the IETF/RFC 8439 variant. The DJB variant uses a
64-bit counter and 64-bit nonce internally, whereas the IETF variant uses a 32-bit
counter and 96-bit nonce. The XChaCha20 construction extends the nonce to 24 bytes
via HChaCha20, providing sufficient nonce space to avoid collisions with random nonce
generation.

### 2.2 Key Hierarchy

```
                         KEY HIERARCHY
                         =============

    ┌─────────────────────────────────────────────────┐
    │          PROVIDER KEY (Ed25519)                  │
    │          Long-term identity key.                 │
    │          Generated once. Stored in HSM or        │
    │          offline. Public key distributed via     │
    │          stamp. NEVER transmitted.               │
    │                                                   │
    │          Purpose: signs server certificates      │
    │          Rotation: manual (years)                │
    └────────────────────┬────────────────────────────┘
                         │ signs
    ┌────────────────────▼────────────────────────────┐
    │          SERVER KEY (X25519 ephemeral)           │
    │          Short-term encryption key.              │
    │          Rotated every 24 hours (configurable).  │
    │          Signed certificate served to clients.   │
    │                                                   │
    │          Purpose: key exchange in handshake      │
    │          Rotation: automatic (24h default)       │
    └────────────────────┬────────────────────────────┘
                         │ used in
    ┌────────────────────▼────────────────────────────┐
    │          SESSION KEYS (derived via Noise_IK)     │
    │          Per-connection symmetric keys.          │
    │          Derived from X25519 DH + HKDF.         │
    │          Two keys: send_key, recv_key.          │
    │                                                   │
    │          Purpose: AEAD encryption of queries/    │
    │          responses within a session.             │
    │          Lifetime: session duration (max 24h)    │
    └────────────────────┬────────────────────────────┘
                         │ encrypts
    ┌────────────────────▼────────────────────────────┐
    │          TRANSPORT TRAFFIC                       │
    │          Encrypted queries and responses.        │
    │          Each message uses a unique nonce        │
    │          derived from a 64-bit counter.          │
    └─────────────────────────────────────────────────┘

    ┌─────────────────────────────────────────────────┐
    │          PER-USER PSK (32 bytes)                 │
    │          Generated per user by server.           │
    │          Embedded in user's stamp.               │
    │          Used during Noise_IK handshake as PSK.  │
    │                                                   │
    │          Purpose: user authentication +          │
    │          identity (server identifies user by     │
    │          which PSK authenticated the session).   │
    │          Rotation: per-user, independent.        │
    └─────────────────────────────────────────────────┘
```

### 2.3 Noise_IK Handshake Adapted for DNS

Syxx Cipher uses the Noise_IK handshake pattern (Initiator's static key Immediately
transmitted, Responder's static key Known to initiator). This pattern is chosen because:

- The responder's (server's) static public key is known to the initiator (client) via the stamp.
- The initiator's (client's) static public key is transmitted in the first message for per-user identification.
- 1-RTT session establishment: the first message carries encrypted data (the first query).
- Forward secrecy: ephemeral keys from both parties contribute to the session keys.

**Protocol name string:**
```
Noise_IK_25519_XChaCha20Poly1305_BLAKE2s
```

**Pre-shared key integration:** The per-user PSK is mixed into the handshake via the
Noise PSK mechanism. The PSK is placed at position 0 (mixed into the chaining key
before any DH operations), binding the handshake to the user's identity.

**Modified protocol name with PSK:**
```
Noise_IKpsk0_25519_XChaCha20Poly1305_BLAKE2s
```

#### Handshake Flow

```
    Client (Initiator)                              Server (Responder)
    ==================                              ==================

    Known: server_static_pk (from stamp)            Known: server_static_sk
    Known: user_psk (from stamp)                    Known: all user PSKs
    Generates: client_ephemeral_sk/pk               Generates: server_ephemeral_sk/pk
    Has: client_static_sk/pk (from stamp)

    ── Message 1 (client → server) ──────────────────────────────────►

    e                           (client ephemeral public key, 32 bytes)
    es = DH(e, server_static)
    s                           (client static public key, 32 bytes, encrypted)
    ss = DH(s, server_static)
    psk                         (mix user PSK into chaining key)
    encrypted_payload           (first query, encrypted with derived key)

    The server:
    1. Performs DH with its static key and the client's ephemeral key (es).
    2. Decrypts the client's static public key using the intermediate cipher state.
    3. Performs DH with its static key and the client's static key (ss).
    4. Looks up the user's PSK by attempting decryption with each known PSK
       (see Section 2.3.1 for the PSK lookup strategy).
    5. Mixes the PSK into the chaining key.
    6. Decrypts the first query from encrypted_payload.

    ◄── Message 2 (server → client) ──────────────────────────────────

    e                           (server ephemeral public key, 32 bytes)
    ee = DH(client_ephemeral, e)
    se = DH(client_static, e)
    encrypted_payload           (first response, encrypted with final session keys)

    The client:
    1. Performs DH with its ephemeral key and the server's ephemeral key (ee).
    2. Performs DH with its static key and the server's ephemeral key (se).
    3. Derives the final session keys (send_key, recv_key) from the chaining key.
    4. Decrypts the first response.

    Session established. Subsequent queries use transport keys directly.
```

#### 2.3.1 PSK Lookup Strategy

The server must determine which user's PSK to use without the user's identity being
visible in plaintext. The lookup proceeds as follows:

1. The server maintains a hash table mapping `H(PSK) → (user_id, PSK)` for all provisioned users.
2. During the handshake, after processing `e, es, s, ss`, the server reaches the PSK
   mixing step. At this point, the handshake state depends on the correct PSK.
3. The server attempts to decrypt `encrypted_payload` with each candidate PSK, trying
   the most recently active users first (LRU ordering).
4. The first PSK that produces a valid AEAD tag is the correct user.
5. If no PSK produces a valid tag within the configured maximum attempts (default: 256),
   the server silently drops the message (no error response, preventing enumeration).

**Scalability note:** For deployments with more than 256 users, the server MAY implement
a sharded PSK lookup using a bloom filter on `H(PSK)` to narrow candidates. The bloom
filter is updated on user provisioning and revocation.

### 2.4 AEAD Construction for Query/Response Encryption

After the Noise_IK handshake completes, both parties hold two 32-byte transport keys:

- `send_key`: used by the sender to encrypt messages
- `recv_key`: used by the receiver to decrypt messages

(The client's `send_key` is the server's `recv_key` and vice versa.)

Each transport message is encrypted as follows:

```
nonce = 0x00 (8 bytes fixed prefix) || counter (8 bytes, little-endian, 64-bit)
ciphertext = XChaCha20-Poly1305(key, nonce, plaintext, associated_data)
```

Where:
- `key` is the 32-byte transport key
- `counter` is a monotonically increasing 64-bit counter per direction, starting at 0
- `associated_data` is the message header (see Section 3.2), which is authenticated but not encrypted
- `plaintext` is the DNS query or response payload
- Output is `ciphertext || tag` where `tag` is the 16-byte Poly1305 MAC

**Nonce safety:** The 24-byte XChaCha20 nonce is constructed as `0x0000000000000000 || counter`.
The 64-bit counter space provides 2^64 unique nonces per direction per session. Given DNS
query rates (even at 10,000 queries/second), counter exhaustion would take over 58 million
years. Counter reuse is a catastrophic failure: implementations MUST terminate the session
if a counter would wrap.

### 2.5 Padding Scheme

All encrypted payloads are padded to a fixed block size before encryption. This eliminates
size-based traffic analysis entirely: every query is the same size on the wire, and every
response is the same size on the wire.

| Message Type | Block Size | Rationale |
|-------------|-----------|-----------|
| Query | 512 bytes | Accommodates typical DNS queries (avg ~40-60 bytes) plus EDNS(0) options with headroom. QNAME lengths up to 253 bytes. |
| Response | 1024 bytes | Accommodates most DNS responses including DNSSEC RRSIG records. Large responses use TCP fallback. |
| Handshake Message 1 | 768 bytes | Accommodates Noise message + first query. |
| Handshake Message 2 | 1280 bytes | Accommodates Noise message + first response. |

**Padding algorithm (ISO/IEC 7816-4 compatible):**

```
padded_message = plaintext || 0x80 || 0x00 * N

where N = block_size - len(plaintext) - 1
```

The receiver strips padding by scanning backward from the end of the decrypted plaintext
for the `0x80` byte, removing it and all trailing `0x00` bytes. If no `0x80` byte is found,
the message is rejected as malformed.

**Oversize messages:** If the plaintext exceeds the block size (minus 1 byte for the
padding marker), the message MUST be sent over TCP. The client sets the `TCP_REQUIRED`
flag in the query header (see Section 3.2) to signal the server.

### 2.6 Forward Secrecy Guarantees

The Noise_IK pattern provides the following forward secrecy properties:

| Key Compromised | Past Sessions | Future Sessions | Current Session |
|----------------|---------------|-----------------|-----------------|
| Client static key | Protected (ephemeral keys contribute) | Compromised (identity impersonation) | Compromised |
| Server static key | Protected (ephemeral keys contribute) | Compromised (impersonation) | Compromised |
| Client ephemeral key | N/A (single use) | N/A (single use) | Compromised |
| Server ephemeral key | N/A (single use) | N/A (single use) | Compromised |
| User PSK | Protected (PSK is not the only DH contribution) | Compromised (user impersonation) | Compromised |
| Session transport keys | N/A | N/A | Compromised |

**Key property:** Compromise of long-term static keys (client or server) does not expose
past session content, because each session includes ephemeral DH contributions from both
parties (the `ee` and `se` operations). This matches the forward secrecy guarantee of
TLS 1.3 with ephemeral key exchange.

**PSK contribution:** The PSK is mixed into the handshake at position 0 (before DH
operations). This means the PSK authenticates the session but the DH operations provide
forward secrecy independently. If the PSK is compromised, the attacker can impersonate
the user in new sessions but cannot decrypt past sessions (assuming ephemeral keys were
properly destroyed).

### 2.7 Post-Quantum Migration Path

Syxx Cipher includes a defined migration path to post-quantum cryptography via the
X-Wing hybrid KEM, which combines ML-KEM-768 (CRYSTALS-Kyber, NIST standardized)
with X25519.

**Migration trigger:** The server signals post-quantum readiness via a capability flag
in its certificate. Clients that support PQ key exchange include a PQ key share in
the handshake.

**PQ handshake variant:**

```
Noise_IKpsk0_XWing_XChaCha20Poly1305_BLAKE2s
```

Where `XWing` replaces the X25519 key exchange with:

```
XWing_Encaps(server_pk) → (ciphertext, shared_secret_pq)
X25519(client_sk, server_pk) → shared_secret_classical
combined_secret = HKDF-SHA256(shared_secret_classical || shared_secret_pq, "syxx-cipher-pq-v1")
```

**Performance impact:** X-Wing adds approximately 1120 bytes of ciphertext to the first
handshake message. For subsequent queries (which use established session keys), there is
no PQ overhead.

**Backward compatibility:** Servers that do not support PQ simply ignore the PQ key share
in the handshake. Clients MUST be prepared for the server to fall back to classical X25519.
The handshake version field indicates which KEM was used.

---

## 3. Wire Protocol

### 3.1 Transport Selection

| Transport | Use Case | Port |
|-----------|----------|------|
| UDP | Primary transport. Used for all queries that fit within the fixed block size. | 5443 |
| TCP | Fallback for oversize queries/responses, handshake when UDP is blocked, stream multiplexing over persistent connections. | 5443 |

**UDP behavior:** Each UDP datagram contains exactly one protocol message (handshake
or transport). The datagram size is always the fixed block size for the message type.
No fragmentation: if a message would exceed the path MTU, fall back to TCP.

**TCP behavior:** Messages are framed with a 2-byte length prefix (big-endian), identical
to DNS-over-TCP framing. Multiple messages can be pipelined on a single TCP connection.

### 3.2 Message Header Format

All messages (both handshake and transport) share a common header:

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|    Magic      |   Version     |     Flags     |   Msg Type    |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|         Stream ID             |        Payload Length         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

**Field definitions:**

| Field | Offset | Size | Description |
|-------|--------|------|-------------|
| Magic | 0 | 1 byte | Protocol magic byte: `0xC6` (Syxx Cipher identifier) |
| Version | 1 | 1 byte | Protocol version: `0x01` for this specification |
| Flags | 2 | 1 byte | Flag bits (see below) |
| Msg Type | 3 | 1 byte | Message type identifier (see below) |
| Stream ID | 4 | 2 bytes | Stream identifier for multiplexing (big-endian). 0 for handshake messages. |
| Payload Length | 6 | 2 bytes | Length of the encrypted payload following this header (big-endian). |

**Flag bits (Flags byte):**

| Bit | Name | Description |
|-----|------|-------------|
| 0 | `TCP_REQUIRED` | Set by client when the query requires TCP (oversize). |
| 1 | `COVER_TRAFFIC` | Set on dummy queries (server discards after decryption). |
| 2 | `RECEIPT_REQUESTED` | Client requests a resolution receipt in the response. |
| 3 | `MULTIPATH_REQUESTED` | Client requests multi-path cross-reference. |
| 4 | `PQ_KEY_SHARE` | Handshake message includes post-quantum key material. |
| 5 | `SESSION_RESUME` | Client is attempting 0-RTT session resumption. |
| 6-7 | Reserved | MUST be zero. |

**Message types (Msg Type byte):**

| Value | Name | Description |
|-------|------|-------------|
| `0x01` | `HANDSHAKE_INIT` | Handshake Message 1 (client → server) |
| `0x02` | `HANDSHAKE_RESP` | Handshake Message 2 (server → client) |
| `0x03` | `QUERY` | Encrypted DNS query |
| `0x04` | `RESPONSE` | Encrypted DNS response |
| `0x05` | `SESSION_TICKET` | Server-issued session resumption ticket |
| `0x06` | `SESSION_CLOSE` | Graceful session termination |
| `0x07` | `KEEPALIVE` | Session keepalive (no payload) |
| `0x08` | `ERROR` | Unencrypted error notification (see Section 3.8) |

### 3.3 Handshake Messages

#### 3.3.1 Handshake Message 1 (HANDSHAKE_INIT)

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|  Magic (0xC6) | Version (0x01)|    Flags      | Type (0x01)   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|        Stream ID (0)          |      Payload Length (760)      |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|              Client Ephemeral Public Key (32 bytes)            |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Encrypted: Client Static Public Key (32 bytes)          |
|              + AEAD Tag (16 bytes)                            |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Encrypted: First Query Payload (variable, padded)       |
|              + AEAD Tag (16 bytes)                            |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Padding to fixed size (768 bytes total)                 |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

Total wire size: 8 (header) + 760 (payload) = 768 bytes.

The encrypted fields use the intermediate cipher state derived from the Noise handshake
operations up to that point. The first query payload is encrypted with a preliminary
transport key that will be finalized in Message 2.

#### 3.3.2 Handshake Message 2 (HANDSHAKE_RESP)

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|  Magic (0xC6) | Version (0x01)|    Flags      | Type (0x02)   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|        Stream ID (0)          |      Payload Length (1272)     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|              Server Ephemeral Public Key (32 bytes)            |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Encrypted: First Response Payload (variable, padded)    |
|              + AEAD Tag (16 bytes)                            |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Session Ticket (encrypted, 64 bytes)                    |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Padding to fixed size (1280 bytes total)                |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

Total wire size: 8 (header) + 1272 (payload) = 1280 bytes.

The session ticket is an encrypted blob that the client stores and presents for 0-RTT
session resumption (see Section 3.6).

### 3.4 Query Message Structure (QUERY)

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|  Magic (0xC6) | Version (0x01)|    Flags      | Type (0x03)   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Stream ID (1-65535)     |      Payload Length (504)      |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|              Client Nonce (12 bytes)                           |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Encrypted: DNS Query Wire Format (variable)             |
|              + AEAD Tag (16 bytes)                            |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Padding to fixed size (512 bytes total)                 |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

Total wire size: 8 (header) + 504 (payload) = 512 bytes.

**Client Nonce:** 12 bytes, randomly generated per query. The client MUST NOT reuse a
nonce within the same session. The server echoes this nonce in the response to bind the
response to the query. The client tracks outstanding nonces and rejects responses with
unknown, expired (older than 30 seconds), or duplicate nonces.

**Encrypted payload:** Standard DNS query wire format (RFC 1035 Section 4.1.2), padded
to fill the remaining space in the 512-byte block. The DNS transaction ID field is set
to 0 (stream ID provides correlation). The associated data for AEAD is the 8-byte
message header.

### 3.5 Response Message Structure (RESPONSE)

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|  Magic (0xC6) | Version (0x01)|    Flags      | Type (0x04)   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Stream ID (matches Q)   |      Payload Length (1016)     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|              Client Nonce (12 bytes, echoed from query)        |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|              Server Nonce (12 bytes, random)                   |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Encrypted: Response Payload (variable)                  |
|              + AEAD Tag (16 bytes)                            |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Padding to fixed size (1024 bytes total)                |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

Total wire size: 8 (header) + 1016 (payload) = 1024 bytes.

**Server Nonce:** 12 bytes, randomly generated per response. Combined with the client
nonce, this forms the 24-byte XChaCha20 nonce for the response:
`response_nonce = client_nonce || server_nonce`. This ensures unique key/nonce pairs
even if the transport key is reused across multiple messages.

**Encrypted response payload structure:**

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|  Resp Type    |  DNSSEC Stat  |     Block Code (if blocked)   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                      Response Timestamp                        |
|                     (8 bytes, Unix ms)                         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|              DNS Response Wire Format (variable)               |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Optional: Resolution Receipt (if RECEIPT_REQUESTED)     |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Optional: Ed25519 Signature (64 bytes, if requested)    |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|       Padding (ISO 7816-4)                                    |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

**Response payload fields:**

| Field | Size | Description |
|-------|------|-------------|
| Resp Type | 1 byte | `0x00` = normal response, `0x01` = blocked, `0x02` = error |
| DNSSEC Status | 1 byte | `0x00` = unchecked, `0x01` = validated (secure), `0x02` = insecure (no DNSSEC), `0x03` = bogus (validation failed) |
| Block Code | 2 bytes | Present only when Resp Type is `0x01`. See Section 5.4 for code definitions. `0x0000` otherwise. |
| Response Timestamp | 8 bytes | Unix timestamp in milliseconds (big-endian, 64-bit). Used for receipt verification and anti-replay. |
| DNS Response | Variable | Standard DNS response wire format (RFC 1035 Section 4.1.1). Transaction ID set to 0. |
| Resolution Receipt | Variable | Present only when `RECEIPT_REQUESTED` flag was set in query. See Section 6.2. |
| Ed25519 Signature | 64 bytes | Present when response includes a receipt. Signs `response_timestamp || H(dns_response)`. |

### 3.6 Session Establishment Flow (1-RTT)

```
    Client                                          Server
    ======                                          ======

    1. Load stamp (server address, server_static_pk, user_psk)
    2. Generate client_ephemeral keypair
    3. Construct Handshake Message 1:
       - Include client_ephemeral_pk
       - Encrypt client_static_pk
       - Encrypt first DNS query
    4. Pad to 768 bytes

    ────── UDP datagram (768 bytes) ──────────────►

                                                    5. Extract client_ephemeral_pk
                                                    6. Perform Noise_IK handshake:
                                                       es, s, ss, psk
                                                    7. Identify user via PSK lookup
                                                    8. Decrypt first query
                                                    9. Resolve query
                                                    10. Construct Handshake Message 2:
                                                        - Include server_ephemeral_pk
                                                        - Encrypt first response
                                                        - Generate session ticket
                                                    11. Pad to 1280 bytes

    ◄────── UDP datagram (1280 bytes) ──────────────

    12. Process server_ephemeral_pk
    13. Complete Noise_IK: ee, se
    14. Derive transport keys (send_key, recv_key)
    15. Decrypt first response
    16. Store session ticket for resumption

    Session established.
    Latency: 1 round trip.
```

### 3.7 0-RTT Query Flow (Session Resumption)

For subsequent connections to the same server (within the session ticket's validity
period), the client can resume a session in 0-RTT:

```
    Client                                          Server
    ======                                          ======

    1. Load cached session ticket + transport keys
    2. Generate client_ephemeral keypair
    3. Construct Handshake Message 1:
       - Set SESSION_RESUME flag
       - Include session ticket in encrypted payload
       - Include first DNS query
    4. Encrypt using cached send_key (0-RTT key)

    ────── UDP datagram (768 bytes) ──────────────►

                                                    5. Detect SESSION_RESUME flag
                                                    6. Decrypt session ticket with
                                                       server's ticket key
                                                    7. Verify ticket validity and age
                                                    8. Derive new transport keys from
                                                       ticket + new ephemeral DH
                                                    9. Decrypt and resolve query
                                                    10. Send Handshake Message 2 with
                                                        new ephemeral key + response

    ◄────── UDP datagram (1280 bytes) ──────────────

    11. Complete new handshake (ee, se with new ephemerals)
    12. Derive new transport keys
    13. Continue session with new keys

    Session resumed + forward secrecy refreshed.
    Latency: 1 round trip for resumption (but first query is sent in the same message).
```

**0-RTT replay protection:** The server tracks recently used session tickets in a
time-bounded cache (default: 5 minutes). Replayed tickets are rejected. DNS queries
are idempotent (read-only), so a replayed query has no side effects beyond information
disclosure (the attacker learns that the same query was made). The server MAY silently
accept replayed tickets but not process the enclosed query.

### 3.8 Stream Multiplexing

Syxx Cipher supports parallel queries over a single session using a lightweight stream
multiplexing scheme inspired by QUIC streams but without QUIC's full transport complexity.

**Stream ID allocation:**
- Stream IDs are 16-bit unsigned integers (1 to 65535).
- Client-initiated streams use odd IDs: 1, 3, 5, ...
- Server-initiated streams (for push notifications, future use) use even IDs: 2, 4, 6, ...
- Stream ID 0 is reserved for control messages (handshake, keepalive, session close).

**Stream lifecycle:**

```
    Client                                          Server
    ======                                          ======

    ─── QUERY (stream_id=1) ──────────────────────►
    ─── QUERY (stream_id=3) ──────────────────────►  (parallel, no ordering dependency)
    ─── QUERY (stream_id=5) ──────────────────────►

    ◄── RESPONSE (stream_id=3) ──────────────────   (response for stream 3 first)
    ◄── RESPONSE (stream_id=1) ──────────────────   (response for stream 1 second)
    ◄── RESPONSE (stream_id=5) ──────────────────   (response for stream 5 last)
```

**No head-of-line blocking:** Each query/response pair is independently associated with
a stream ID. The server processes queries in any order and returns responses as they
become available. A slow query on stream 1 does not delay responses on streams 3 and 5.

**UDP multiplexing:** Over UDP, each message is a separate datagram. Multiple datagrams
can be in flight simultaneously. Stream ID correlation replaces DNS transaction ID.

**TCP multiplexing:** Over TCP, messages are framed with 2-byte length prefixes and can
be pipelined on a single connection. The stream ID in the header provides correlation.

**Concurrency limit:** The server MAY enforce a maximum number of concurrent in-flight
streams per session (default: 64). Queries exceeding the limit receive an `ERROR`
response with code `0x03` (TOO_MANY_STREAMS).

### 3.9 Error Messages

Unencrypted `ERROR` messages (type `0x08`) are sent only for protocol-level errors that
prevent decryption:

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|  Magic (0xC6) | Version (0x01)|  Flags (0x00) | Type (0x08)   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|        Stream ID              |     Payload Length (4)         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|        Error Code             |        Reserved (0x0000)      |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

**Error codes:**

| Code | Name | Description |
|------|------|-------------|
| `0x01` | `BAD_VERSION` | Unsupported protocol version |
| `0x02` | `BAD_HANDSHAKE` | Handshake decryption failed (invalid PSK, malformed) |
| `0x03` | `TOO_MANY_STREAMS` | Concurrent stream limit exceeded |
| `0x04` | `SESSION_EXPIRED` | Session ticket expired |
| `0x05` | `RATE_LIMITED` | Rate limit exceeded |
| `0x06` | `SERVER_SHUTDOWN` | Server is shutting down |
| `0x07` | `INTERNAL_ERROR` | Server internal error |

**Security note:** Error messages reveal minimal information. `BAD_HANDSHAKE` does not
distinguish between "wrong PSK" and "malformed message," preventing PSK enumeration.
For errors within an established session, the server sends encrypted error responses
(using the `RESPONSE` type with `Resp Type = 0x02`) rather than unencrypted errors.

### 3.10 Session Keepalive and Close

**Keepalive (type `0x07`):** Either party may send a keepalive to prevent session timeout.
The payload is empty (Payload Length = 0). The receiver acknowledges with its own keepalive.
Default keepalive interval: 30 seconds.

**Session Close (type `0x06`):** Either party may gracefully close the session. The payload
contains a 2-byte close code:

| Code | Reason |
|------|--------|
| `0x00` | Normal closure |
| `0x01` | Client-initiated shutdown |
| `0x02` | Server-initiated shutdown |
| `0x03` | Session key rotation required |
| `0x04` | User PSK revoked |

After sending a close message, the sender discards all session state. The receiver MAY
send a close acknowledgment but is not required to.

---

## 4. Stamp Format

### 4.1 URI Scheme

Syxx Cipher uses the `cipher://` URI scheme for server connection parameters:

```
cipher://<base64url-encoded-binary-payload>
```

The binary payload is base64url-encoded (RFC 4648 Section 5) with padding omitted.

### 4.2 Binary Payload Format

The binary payload extends the DNS stamp format with additional fields for Syxx Cipher:

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| Protocol ID   |                                               |
|  (0xC6)       |         Properties (8 bytes, LE)              |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|                    Properties (continued)                      |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| Addr Length   |  Server Address (variable, "IP:port")         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| PK Length     |  Provider Ed25519 Public Key (32 bytes)       |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|              Provider Public Key (continued)                   |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| PSK Length    |  Per-User PSK (32 bytes)                      |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|                    Per-User PSK (continued)                    |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| Name Length   |  Server Name (variable, human-readable)       |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

**Field definitions:**

| Field | Size | Description |
|-------|------|-------------|
| Protocol ID | 1 byte | `0xC6` (Syxx Cipher identifier) |
| Properties | 8 bytes | 64-bit little-endian capability flags |
| Addr Length | 1 byte | Length of the server address string |
| Server Address | Variable | ASCII string in `IP:port` format (e.g., `203.0.113.1:5443`) |
| PK Length | 1 byte | Length of the provider public key (always 32) |
| Provider Public Key | 32 bytes | Ed25519 public key for certificate verification |
| PSK Length | 1 byte | Length of the per-user PSK (always 32) |
| Per-User PSK | 32 bytes | User-specific pre-shared key for authentication |
| Name Length | 1 byte | Length of the server name string |
| Server Name | Variable | Human-readable server name (e.g., "my-syxx-server") |

### 4.3 Properties Flags

The 64-bit properties field encodes server capabilities and policy flags:

| Bit | Name | Description |
|-----|------|-------------|
| 0 | `DNSSEC` | Server performs DNSSEC validation |
| 1 | `NO_LOGS` | Server does not log queries |
| 2 | `NO_FILTERING` | Server does not apply any filtering |
| 3 | `FILTERING` | Server supports per-user filtering (blocklists, allowlists) |
| 4 | `PARENTAL_CONTROLS` | Server supports category-based parental controls |
| 5 | `RECEIPTS` | Server supports resolution receipts |
| 6 | `MULTIPATH` | Server supports multi-path cross-reference |
| 7 | `RELAY` | Server supports relay (Anonymized Cipher) |
| 8 | `COVER_TRAFFIC` | Server supports cover traffic |
| 9 | `POST_QUANTUM` | Server supports post-quantum key exchange (X-Wing) |
| 10 | `CONSTANT_TIME` | Server implements constant-time response normalization |
| 11 | `ANALYTICS` | Server provides per-user analytics (opt-in) |
| 12-63 | Reserved | MUST be zero |

### 4.4 Per-User PSK Encoding

The PSK is a 32-byte random value generated by the server during user provisioning.
It is included in the stamp in plaintext (the stamp itself is the secure distribution
channel). The PSK is never transmitted over the network; it is mixed into the Noise
handshake as a pre-shared key.

**PSK generation requirements:**
- MUST be generated from a cryptographically secure random number generator (CSPRNG)
- MUST be exactly 32 bytes
- MUST be unique per user per server
- MUST NOT be derived from any user-identifiable information (username, email, etc.)

**PSK rotation:** The server MAY issue a new PSK for a user at any time. The new PSK
is distributed via a new stamp. The old PSK remains valid until the server revokes it
(configurable grace period, default: 24 hours). During the grace period, both old and
new PSKs authenticate the same user.

### 4.5 Example Stamps

**Example 1: Full-featured server with per-user filtering**

```
Server: 203.0.113.1:5443
Provider key: d4b1c8a7f2e39d04a5c6b7e8f1a2d3c4e5f6a7b8c9d0e1f2a3b4c5d6e7f8091a
User PSK: 1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f1a2b
Properties: DNSSEC + FILTERING + RECEIPTS + COVER_TRAFFIC + CONSTANT_TIME
  = 0x0000000000000345
Name: my-syxx-server
```

Binary payload (hex):
```
C6                                              # Protocol ID
4503000000000000                                # Properties (LE)
11                                              # Addr length (17)
3230332E302E3131332E313A35343433               # "203.0.113.1:5443"
20                                              # PK length (32)
d4b1c8a7f2e39d04a5c6b7e8f1a2d3c4e5f6a7b8c9d0e1f2a3b4c5d6e7f8091a
20                                              # PSK length (32)
1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f1a2b
10                                              # Name length (16)
6D792D737978782D736572766572                   # "my-syxx-server"
```

Stamp:
```
cipher://zAUAAAAAAAAAAAAAEzIwMy4wLjExMy4xOjU0NDMg1LHIp_LjnQSlxrf
o8aLTxOX2p7jJ0OHyo7TF1ueQCZogGiy7TNZebouccQ0uL3KpCxyjWtA1IzXlV
thpYsRQEG15LXN5eHgtc2VydmVy
```

**Example 2: Minimal server (no filtering, no logs)**

```
Server: 198.51.100.42:5443
Provider key: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f80910111213141516171819202122
User PSK: deadbeefcafebabe0102030405060708091011121314151617181920212223
Properties: DNSSEC + NO_LOGS = 0x0000000000000003
Name: private-resolver
```

Stamp:
```
cipher://zAMAAAAAAAAAAAAAEzE5OC41MS4xMDAuNDI6NTQ0MyChss
vU5fanuMnQ4fKjtMX2CJERERMUFRUTFhcYGSAhICDtvu78
rj66vAECAwQFBgcICQkKCwwNDg8QERITFBUWFxgZICE
iI6Rwcml2YXRlLXJlc29sdmVy
```

**Example 3: Relay stamp (for Anonymized Cipher)**

Relay stamps contain only the relay address (no keys, no PSK):

```
Relay: 192.0.2.100:5443
```

The relay stamp uses Protocol ID `0x86` (relay variant):

```
cipher://hqwAAAAAAAAAAAw5Mi4wLjIuMTAwOjU0NDM
```

---

## 5. Per-User Policy Engine

### 5.1 User Identity Model

Syxx Cipher identifies users cryptographically, with no user identity visible in network
traffic. The identity model is:

1. **Provisioning:** The server administrator creates a user account and generates a
   unique 32-byte PSK. The PSK is embedded in the user's stamp.

2. **Authentication:** During the Noise_IK handshake, the client's PSK is mixed into the
   cryptographic state. The server identifies the user by which PSK successfully
   authenticates the handshake (Section 2.3.1).

3. **Session binding:** Once authenticated, the session is bound to the user. All queries
   on that session inherit the user's policy. No per-query authentication is needed.

4. **Zero leakage:** The user's PSK is never transmitted in plaintext. The client's static
   public key is encrypted during the handshake. Network observers see only encrypted
   traffic with no user-identifying fields.

5. **Independent rotation:** Each user's PSK can be rotated independently without affecting
   other users or requiring server restart.

### 5.2 Policy Configuration

Each user has a policy configuration stored server-side. The configuration is a structured
document with the following sections:

```
UserPolicy {
    user_id: string,                    // Server-assigned unique identifier
    
    blocklists: [BlocklistRef],         // References to blocklist sources
    allowlists: [string],               // Explicitly allowed domains (override blocklists)
    denylists: [string],                // Explicitly blocked domains (override allowlists)
    rewrites: {string: string},         // Domain → IP rewrites (hosts-file style)
    
    categories: {
        malware: PolicyAction,          // Block, Allow, or Log-only
        phishing: PolicyAction,
        ads: PolicyAction,
        trackers: PolicyAction,
        adult: PolicyAction,
        gambling: PolicyAction,
        piracy: PolicyAction,
        social_media: PolicyAction,
        custom: [{name: string, action: PolicyAction, domains: [string]}]
    },
    
    parental_controls: {
        enabled: bool,
        safe_search: bool,              // Force SafeSearch on search engines
        youtube_restricted: bool,       // YouTube Restricted Mode
        blocked_apps: [string],         // App/service-specific blocking
        schedule: {                     // Time-based policy
            weekday_start: string,      // "HH:MM"
            weekday_end: string,
            weekend_start: string,
            weekend_end: string
        }
    },
    
    security: {
        newly_registered_domains: PolicyAction,  // Block domains registered < N days
        dga_detection: bool,                      // Block DGA-like domains
        typosquatting: bool,                      // Block common typosquat domains
        threat_intelligence_feeds: [string]       // External threat feed references
    },
    
    analytics: {
        enabled: bool,                  // Opt-in query logging
        retention_days: int,            // 1 to 730 (configurable)
        data_residency: string          // "US", "EU", "local" (server-local only)
    }
}
```

**PolicyAction enum:**

| Value | Name | Behavior |
|-------|------|----------|
| `0x00` | `ALLOW` | Query resolved normally |
| `0x01` | `BLOCK` | Query blocked; structured block response returned |
| `0x02` | `SINKHOLE` | Query resolved to configured sinkhole IP (default: `0.0.0.0`) |
| `0x03` | `LOG_ONLY` | Query resolved normally but logged for analytics |
| `0x04` | `REDIRECT` | Query resolved to a custom IP address (from rewrites) |

### 5.3 Query Evaluation Pipeline

The server evaluates each query through a pipeline of policy stages, inspired by Unbound's
modular architecture and RPZ (Response Policy Zones). Evaluation is ordered: the first
matching rule determines the action.

```
                    QUERY EVALUATION PIPELINE
                    =========================

    ┌─────────────────────────────────────────┐
    │ Stage 1: Allowlist Check                │
    │ Does the QNAME match the user's         │
    │ allowlist? If yes → ALLOW, skip to      │
    │ resolution.                             │
    └──────────────────┬──────────────────────┘
                       │ no match
    ┌──────────────────▼──────────────────────┐
    │ Stage 2: Denylist Check                 │
    │ Does the QNAME match the user's         │
    │ denylist? If yes → BLOCK with reason    │
    │ "Custom denylist".                      │
    └──────────────────┬──────────────────────┘
                       │ no match
    ┌──────────────────▼──────────────────────┐
    │ Stage 3: RPZ Trigger Matching           │
    │ Evaluate query against Response Policy  │
    │ Zones using 5 trigger types:            │
    │  - QNAME (domain name)                  │
    │  - Client IP (user's source IP)         │
    │  - Response IP (resolved IP, post-res)  │
    │  - NSDNAME (nameserver domain)          │
    │  - NSIP (nameserver IP)                │
    │ If match → apply RPZ action.           │
    └──────────────────┬──────────────────────┘
                       │ no match
    ┌──────────────────▼──────────────────────┐
    │ Stage 4: Category Matching              │
    │ Does the QNAME match any configured     │
    │ category (malware, ads, adult, etc.)?   │
    │ Uses domain classification database.    │
    │ If match → apply category action.       │
    └──────────────────┬──────────────────────┘
                       │ no match
    ┌──────────────────▼──────────────────────┐
    │ Stage 5: Security Checks                │
    │ - Newly registered domain check         │
    │ - DGA detection (entropy analysis)      │
    │ - Typosquatting detection               │
    │ - Threat intelligence feed match        │
    │ If match → BLOCK with reason.           │
    └──────────────────┬──────────────────────┘
                       │ no match
    ┌──────────────────▼──────────────────────┐
    │ Stage 6: Rewrite Check                  │
    │ Does the QNAME match a rewrite rule?    │
    │ If yes → return custom response.        │
    └──────────────────┬──────────────────────┘
                       │ no match
    ┌──────────────────▼──────────────────────┐
    │ Stage 7: Normal Resolution              │
    │ Query resolved through recursive        │
    │ resolver with DNSSEC validation.        │
    └──────────────────┬──────────────────────┘
                       │
    ┌──────────────────▼──────────────────────┐
    │ Stage 8: Post-Resolution Checks         │
    │ - Response IP trigger (RPZ)             │
    │ - NSDNAME/NSIP trigger (RPZ)            │
    │ - Sinkhole IP substitution              │
    │ If match → override response.           │
    └─────────────────────────────────────────┘
```

### 5.4 Structured Block Response Format

When a query is blocked, the server returns a structured block response within the
encrypted response payload. This replaces the traditional approach of returning NXDOMAIN
(indistinguishable from a real non-existent domain) or a sinkhole IP (requires HTTP for
explanation).

**Block response structure (within encrypted response payload):**

```
BlockResponse {
    resp_type: 0x01,                    // BLOCKED marker
    
    block_code: u16,                    // Machine-readable block reason
    category: string,                   // Hierarchical category path
    reason_text: string,                // Human-readable explanation
    source: string,                     // Which list/rule triggered the block
    
    override_allowed: bool,             // Can the client request an override?
    override_token: [16 bytes],         // Token for override request (if allowed)
    
    log_id: u64,                        // Server-side log entry ID for audit
    
    signature: [64 bytes],              // Ed25519 signature over:
                                        //   block_code || category || reason_text ||
                                        //   source || response_timestamp
}
```

**Block codes:**

| Code | Name | Description |
|------|------|-------------|
| `0x0001` | `BLOCKLIST_MATCH` | Domain matched a blocklist |
| `0x0002` | `DENYLIST_MATCH` | Domain matched user's custom denylist |
| `0x0003` | `CATEGORY_MALWARE` | Classified as malware domain |
| `0x0004` | `CATEGORY_PHISHING` | Classified as phishing domain |
| `0x0005` | `CATEGORY_ADS` | Classified as advertising domain |
| `0x0006` | `CATEGORY_TRACKERS` | Classified as tracking domain |
| `0x0007` | `CATEGORY_ADULT` | Classified as adult content |
| `0x0008` | `CATEGORY_GAMBLING` | Classified as gambling domain |
| `0x0009` | `CATEGORY_PIRACY` | Classified as piracy-related domain |
| `0x000A` | `CATEGORY_SOCIAL` | Classified as social media |
| `0x000B` | `NEWLY_REGISTERED` | Domain registered within threshold period |
| `0x000C` | `DGA_DETECTED` | Domain exhibits DGA-like characteristics |
| `0x000D` | `TYPOSQUAT` | Domain identified as typosquatting |
| `0x000E` | `THREAT_INTEL` | Matched threat intelligence feed |
| `0x000F` | `PARENTAL_CONTROL` | Blocked by parental control policy |
| `0x0010` | `SCHEDULE_BLOCK` | Blocked by time-based schedule |
| `0x0011` | `RPZ_QNAME` | Blocked by RPZ QNAME trigger |
| `0x0012` | `RPZ_CLIENT_IP` | Blocked by RPZ Client IP trigger |
| `0x0013` | `RPZ_RESPONSE_IP` | Blocked by RPZ Response IP trigger |
| `0x0014` | `RPZ_NSDNAME` | Blocked by RPZ NSDNAME trigger |
| `0x0015` | `RPZ_NSIP` | Blocked by RPZ NSIP trigger |
| `0x0016` | `CUSTOM_CATEGORY` | Blocked by user-defined custom category |
| `0x00FF` | `ADMIN_BLOCK` | Blocked by server administrator policy |

**Encoding:** Strings within the block response are encoded as length-prefixed UTF-8:
`[2 bytes: length, big-endian] [UTF-8 data]`. The entire block response structure is
serialized in a compact binary format (not JSON) to minimize size within the fixed
1024-byte response block.

**Signature verification:** The client verifies the block response signature using the
server's Ed25519 public key (from the stamp). A valid signature proves:
1. The block decision came from the server's policy engine (not injected by a MITM).
2. The block reason and category are authentic.
3. The server is accountable for the block decision (non-repudiation).

### 5.5 Analytics and Logging

Analytics are opt-in and user-controlled. When enabled, the server logs query metadata
for the user's dashboard:

**Logged fields (when analytics enabled):**
- Timestamp (server time)
- QNAME (queried domain)
- QTYPE (query type)
- Action taken (allowed, blocked, sinkholed, rewritten)
- Block code (if blocked)
- Category (if categorized)
- Response time (ms)
- DNSSEC status

**NOT logged:**
- Client IP address
- Client PSK or identity
- Full DNS response content
- Query payload beyond QNAME/QTYPE

**Retention:** Configurable per user (1 day to 2 years). Data is automatically purged
after the retention period. The `data_residency` field controls where analytics data is
stored (server-local, or a designated region for multi-region deployments).

**Encryption at rest:** Analytics data is encrypted at rest using AES-256-GCM with a
per-user data encryption key. The key is derived from the user's PSK via HKDF:

```
analytics_key = HKDF-SHA256(user_psk, "syxx-cipher-analytics-v1", 32)
```

### 5.6 NextDNS-Equivalent Feature Matrix

| Feature | NextDNS | Syxx Cipher | Notes |
|---------|---------|-------------|-------|
| Per-user filtering | Yes (config ID in hostname) | Yes (PSK-based, zero leakage) | Syxx Cipher provides better privacy |
| Custom blocklists | Yes (curated + community) | Yes (same model) | Blocklist format compatible |
| Custom allowlists | Yes | Yes | Identical functionality |
| Custom denylists | Yes | Yes | Identical functionality |
| Rewrites | Yes | Yes | Hosts-file style domain rewrites |
| Category blocking | Yes (porn, violence, piracy) | Yes (malware, phishing, ads, trackers, adult, gambling, piracy, social, custom) | Syxx Cipher has more categories |
| Parental controls | Yes (SafeSearch, YouTube, app blocking, scheduling) | Yes (same features) | Identical functionality |
| SafeSearch | Yes | Yes | Enforced via CNAME rewriting |
| YouTube Restricted Mode | Yes | Yes | Enforced via DNS response |
| App/service blocking | Yes (TikTok, Fortnite, etc.) | Yes (configurable) | Domain-based, not deep packet inspection |
| Schedule-based policies | Yes ("Recreation Time") | Yes | Time-based allow/block windows |
| Analytics/logs | Yes (configurable retention) | Yes (opt-in, encrypted at rest, configurable retention) | Syxx Cipher encrypts analytics at rest |
| Threat intelligence | Yes (Google Safe Browsing, DGA) | Yes (pluggable threat feeds) | Extensible feed system |
| Newly registered domains | Yes | Yes | Configurable age threshold |
| Data residency | Yes (US, EU, UK, CH) | Yes (configurable) | Server-local option available |
| Block page | Yes (HTTP) | Yes (structured response in protocol) | Syxx Cipher does not require HTTP |
| Block reason visibility | Partial (dashboard only) | Yes (protocol-level, machine-readable) | Client renders UI from structured data |
| Self-hosted | No (cloud only) | Yes (single-server) | Core differentiator |

---

## 6. Answer Integrity

### 6.1 DNSSEC Validation Pipeline

The Syxx Cipher server performs full DNSSEC validation using a chain-of-trust model
derived from Unbound's architecture:

**Trust anchor management:**
- Root zone KSK (Key Signing Key) is the trust anchor.
- Trust anchors are managed via automated root key rollover (following RFC 5011).
- The server ships with the current root KSK embedded and updates via the standard
  DNSKEY/DS rollover protocol.

**Validation pipeline:**

```
    Query: example.com A
    
    1. Resolve: root → .com → example.com
    2. For each delegation:
       a. Fetch DS record from parent zone
       b. Fetch DNSKEY record from child zone
       c. Verify DS → DNSKEY match (digest algorithm)
       d. Verify RRSIG on DNSKEY using KSK
       e. Verify RRSIG on answer using ZSK
    3. DNSSEC Status:
       - All signatures valid + chain complete → VALIDATED (0x01)
       - Zone not signed (no DS, no DNSKEY) → INSECURE (0x02)
       - Any signature invalid or chain broken → BOGUS (0x03)
       - Validation not performed (error/timeout) → UNCHECKED (0x00)
```

**BOGUS handling:** When DNSSEC validation returns BOGUS, the server:
1. Sets `DNSSEC Status = 0x03` in the response.
2. Includes an Extended DNS Error (EDE, RFC 8914) code explaining the failure.
3. Returns the answer anyway (with the BOGUS status) so the client can decide whether
   to accept it. The client is informed that integrity cannot be guaranteed.

### 6.2 Resolution Receipt Format

A resolution receipt is a cryptographic proof that the server honestly resolved the query
through the standard DNS hierarchy. The receipt contains the delegation chain with
signatures at each step.

**Receipt structure (serialized within the encrypted response):**

```
ResolutionReceipt {
    version: u8,                        // Receipt format version (0x01)
    query_hash: [32 bytes],             // BLAKE2s-256 hash of the original DNS query
    response_hash: [32 bytes],          // BLAKE2s-256 hash of the DNS response
    timestamp: u64,                     // Unix timestamp (milliseconds)
    
    chain_length: u8,                   // Number of delegation steps
    
    chain: [DelegationStep],            // Array of delegation steps
    
    receipt_signature: [64 bytes],      // Ed25519 signature over:
                                        //   version || query_hash || response_hash ||
                                        //   timestamp || chain_length || H(chain)
}

DelegationStep {
    zone: string,                       // Zone name (e.g., "com.", "example.com.")
    qname: string,                      // QNAME sent to this zone's nameserver
    qtype: u16,                         // Query type sent
    nameserver_ip: [16 bytes],          // IPv6 or IPv4-mapped-IPv6 of nameserver queried
    response_hash: [32 bytes],          // BLAKE2s-256 of the response from this step
    rrsig_present: bool,                // Whether RRSIG records were in the response
    rrsig_data: [variable],             // RRSIG record data (if present)
    ds_data: [variable],                // DS record data from parent (if present)
}
```

**Client verification:**
1. Verify `receipt_signature` using the server's Ed25519 public key.
2. Verify `query_hash` matches the hash of the original query.
3. Verify `response_hash` matches the hash of the received response.
4. For each delegation step, verify the chain forms a valid delegation path from root
   to the answer.
5. If DNSSEC was available at each step, verify the RRSIG and DS data independently.
6. If any step is inconsistent, the server provided a fraudulent receipt (evidence of
   dishonest resolution).

### 6.3 Ed25519 Response Attestation

Every response that includes a receipt is additionally signed with the server's Ed25519
signing key (derived from the provider key or a separate attestation key).

**What is signed:**
```
signature_input = response_timestamp || BLAKE2s-256(dns_response_wire_format)
signature = Ed25519_Sign(attestation_private_key, signature_input)
```

**What this proves:**
- The response was generated by the holder of the attestation private key.
- The response content has not been modified since signing.
- The timestamp is authentic (prevents replay of old responses).
- Third parties can verify the signature using the attestation public key (distributed
  in the stamp or separately).

**Non-repudiation:** Unlike AEAD authentication (which uses a shared key and thus either
party could have generated the tag), Ed25519 signatures provide non-repudiation. The
server cannot deny having generated the response. This enables the client to present the
signed response as evidence of the server's behavior.

### 6.4 Multi-Path Cross-Reference (Optional)

When the client sets the `MULTIPATH_REQUESTED` flag, the server queries additional
upstream resolvers and compares results:

**Cross-reference process:**
1. Server resolves the query through its normal recursive path.
2. Server additionally queries 2-3 configured cross-reference resolvers (e.g., Cloudflare
   1.1.1.1, Google 8.8.8.8, Quad9 9.9.9.9) via DoH or DoT.
3. Server compares all results.

**Cross-reference result (included in response):**

```
CrossReferenceResult {
    num_paths: u8,                      // Number of paths queried (including primary)
    consensus: u8,                      // 0x00=UNANIMOUS, 0x01=MAJORITY, 0x02=CONFLICT
    primary_match: u8,                  // Index of paths matching the primary result
    path_results: [PathResult],         // Per-path results
}

PathResult {
    resolver_name: string,              // e.g., "cloudflare-1.1.1.1"
    resolver_ip: [16 bytes],            // IP address of the cross-reference resolver
    answers: [DNSRecord],               // Answer records from this path
    dnssec_status: u8,                  // DNSSEC status from this path
    response_time_ms: u16,             // Time to resolve via this path
}
```

**Client interpretation:**
- `UNANIMOUS`: All paths returned identical answers. High confidence in answer integrity.
- `MAJORITY`: Most paths agree. The primary result matches the majority. Moderate confidence.
- `CONFLICT`: Paths disagree. Possible tampering on one or more paths. The client should
  flag this for investigation.

### 6.5 Aggressive NSEC Synthesis

The server implements Aggressive NSEC (RFC 8198) to synthesize negative answers from
cached NSEC/NSEC3 records:

1. When a query results in NXDOMAIN or NODATA, the authoritative server returns NSEC or
   NSEC3 records proving the non-existence of the queried name.
2. The server caches these NSEC/NSEC3 records along with the negative response.
3. For subsequent queries that fall within the NSEC interval of a cached negative proof,
   the server synthesizes a negative response from cache without querying upstream.
4. The synthesized response includes the cached NSEC/NSEC3 records as proof.

**Benefit:** Reduces upstream query volume, improves latency for negative answers, and
reduces information leakage (fewer queries to authoritative servers).

### 6.6 QNAME Minimisation

The server implements QNAME Minimisation (RFC 9156) during recursive resolution:

| Resolution Step | QNAME Sent | QTYPE Sent |
|----------------|------------|------------|
| Root query | TLD only (e.g., `com.`) | NS |
| TLD query | SLD only (e.g., `example.com.`) | NS |
| Authoritative query | Full QNAME (e.g., `www.example.com.`) | Original QTYPE |

**Benefit:** Each authoritative server in the delegation chain learns only the minimum
information necessary to provide its referral. The root server does not learn the
full domain name being queried.

---

## 7. Traffic Analysis Resistance

### 7.1 Fixed-Size Padding

All protocol messages are padded to fixed sizes, eliminating size-based traffic analysis:

| Message | Wire Size | Notes |
|---------|-----------|-------|
| Handshake Message 1 | 768 bytes | Fixed |
| Handshake Message 2 | 1280 bytes | Fixed |
| Query (UDP) | 512 bytes | Fixed. All queries identical size. |
| Response (UDP) | 1024 bytes | Fixed. All responses identical size. |
| Keepalive | 8 bytes | Header only. Distinguishable but content-free. |
| Session Close | 10 bytes | Header + 2-byte close code. |

**Wire size invariance:** An observer watching the encrypted traffic sees only:
- Handshake: always 768B → 1280B
- Queries: always 512B
- Responses: always 1024B
- Keepalives: always 8B

The observer cannot determine the domain name length, response complexity, or query type
from packet sizes alone. This is a significant improvement over all existing encrypted
DNS protocols, which leak size information.

**Trade-off:** Fixed-size padding increases bandwidth compared to variable-size messages.
A typical DNS query is 40-60 bytes; padding to 512 bytes is an 8-12x increase. For a
user generating 1000 queries/day, this is approximately 512 KB/day of query traffic
(plus 1024 KB/day of response traffic), totaling approximately 1.5 MB/day. This is
acceptable for modern network connections.

### 7.2 Constant-Time Response Normalization

The server normalizes response times to defeat cache-timing attacks. Without this, an
observer can distinguish cached responses (fast, ~1-5ms) from recursive lookups (slow,
~50-300ms), revealing which domains are popular.

**Normalization algorithm:**

```
target_response_time = config.constant_time_ms  (default: 200ms)

actual_resolve_time = time_to_resolve(query)
delay = max(0, target_response_time - actual_resolve_time)
sleep(delay)
send_response()
```

**Configuration options:**

| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| `constant_time_ms` | 200 | 50-1000 | Target response time in milliseconds |
| `constant_time_enabled` | true | true/false | Enable/disable constant-time mode |
| `constant_time_jitter_ms` | 10 | 0-50 | Random jitter added to target (prevents exact-timing fingerprinting) |

**Jitter:** A small random jitter (uniform distribution, 0 to `constant_time_jitter_ms`)
is added to the target time to prevent exact-timing analysis. This means response times
are uniformly distributed in `[target, target + jitter]`.

**Performance impact:** Cached responses incur artificial delay up to the target time.
For the default 200ms target, cached responses that would normally take 5ms are delayed
by 195ms. This is the fundamental trade-off: traffic analysis resistance increases
perceived latency for cached queries.

**Client awareness:** The client knows the server's `constant_time_ms` from the handshake
(or stamp properties) and can adjust timeout expectations accordingly.

### 7.3 Cover Traffic Protocol

Cover traffic defeats timing-based traffic analysis by masking real query patterns with
dummy queries.

**Cover traffic operation:**

1. The client generates dummy DNS queries at randomized intervals.
2. Dummy queries use real-looking but non-existent domain names (e.g., randomly generated
   subdomains of a configured cover domain: `<random>.cover.syxx.local`).
3. The server recognizes cover traffic via the `COVER_TRAFFIC` flag in the query header
   and returns a synthetic response (NXDOMAIN) without performing actual resolution.
4. The server processes cover queries identically to real queries (same padding, same
   timing normalization) to prevent timing-based distinction.

**Cover traffic parameters:**

| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| `cover_enabled` | false | true/false | Enable cover traffic |
| `cover_rate` | 0.5 | 0.1-5.0 | Average cover queries per real query |
| `cover_interval_min_ms` | 1000 | 100-60000 | Minimum interval between cover queries |
| `cover_interval_max_ms` | 5000 | 1000-120000 | Maximum interval between cover queries |
| `cover_domain` | "cover.syxx.local" | any | Base domain for cover queries |

**Interval distribution:** Cover query intervals follow an exponential distribution with
mean `cover_interval_max_ms / 2`, truncated to `[cover_interval_min_ms, cover_interval_max_ms]`.
This prevents periodic patterns that would be detectable by Fourier analysis.

**Server processing:** Cover queries are:
- Decrypted normally (to verify they are legitimate client traffic)
- Flagged as cover traffic via the `COVER_TRAFFIC` header flag
- Returned with a synthetic NXDOMAIN response
- NOT logged in analytics
- Subject to the same constant-time normalization as real queries

### 7.4 Optional Relay Extension (Anonymized Cipher)

For users who need IP-level privacy (hiding their IP address from the resolver), Syxx
Cipher supports an optional relay mode inspired by Anonymized DNSCrypt.

**Architecture:**

```
    Client ──────encrypted query──────► Relay ──────forward──────► Server
    Client ◄──────encrypted response──── Relay ◄─────forward────── Server
    
    Relay sees: client IP, encrypted payload (cannot decrypt)
    Server sees: relay IP, encrypted payload (can decrypt)
    
    Privacy guarantee: if relay and server don't collude, no single party
    has both the client's IP and the query content.
```

**Relay protocol:**

1. Client encrypts the query for the final server (using the session's transport keys).
2. Client prepends a relay header: `[relay_magic: 1B] [server_addr_len: 1B] [server_addr] [encrypted_query]`.
3. Client sends the combined packet to the relay.
4. Relay extracts the server address and forwards the encrypted query to the server.
5. Server decrypts, resolves, encrypts the response, and sends it back to the relay.
6. Relay forwards the encrypted response to the client.

**Relay header format:**

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| Relay Magic   | Server Addr  |                                |
|   (0xC7)      |   Length     |   Server Address (variable)    |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|              Encrypted Query (512 bytes)                       |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

**Anti-abuse measures:**
- Relay MUST NOT modify the encrypted query payload.
- Relay MUST NOT add, remove, or modify any bytes after the relay header.
- Server enforces the same fixed-size constraints on relayed traffic.
- Relay cannot decrypt queries (does not have session keys).
- Server rate-limits per relay IP to prevent relay-based DoS.

**Trust model:** The relay and server must not collude. If they do, the client's IP
address is linked to query content. This is the same trust model as Anonymized DNSCrypt
and ODoH. The client selects the relay independently from the server.

---

## 8. Server Architecture

### 8.1 Single-Server Deployment Model

Syxx Cipher is designed for single-server deployment by an individual operator. The
server is a standalone process that:

1. Listens on UDP and TCP port 5443 (configurable).
2. Manages cryptographic keys (provider key, server key, per-user PSKs).
3. Performs recursive DNS resolution with DNSSEC validation.
4. Applies per-user policy (blocklists, allowlists, categories).
5. Generates resolution receipts and Ed25519 signatures.
6. Implements traffic analysis resistance (constant-time, cover traffic handling).
7. Optionally integrates with the Syxx Dimensions server.

**Deployment diagram:**

```
    ┌─────────────────────────────────────────────────────┐
    │               Single Server (VPS, home server)       │
    │                                                       │
    │  ┌─────────────────────────────────────────────────┐ │
    │  │           syxx-cipherd (main process)            │ │
    │  │                                                   │ │
    │  │  ┌──────────┐ ┌──────────┐ ┌────────────────┐   │ │
    │  │  │ Protocol │ │ Policy   │ │ Recursive      │   │ │
    │  │  │ Handler  │ │ Engine   │ │ Resolver       │   │ │
    │  │  │ (Noise,  │ │ (RPZ,    │ │ (QNAME min,    │   │ │
    │  │  │  mux,    │ │  block,  │ │  DNSSEC, aggr  │   │ │
    │  │  │  pad)    │ │  allow)  │ │  NSEC, cache)  │   │ │
    │  │  └──────────┘ └──────────┘ └────────────────┘   │ │
    │  │                                                   │ │
    │  │  ┌──────────┐ ┌──────────┐ ┌────────────────┐   │ │
    │  │  │ Key Mgr  │ │ Receipt  │ │ Analytics      │   │ │
    │  │  │ (Ed25519,│ │ Engine   │ │ Store          │   │ │
    │  │  │  X25519, │ │ (sign,   │ │ (encrypted,    │   │ │
    │  │  │  PSK)    │ │  chain)  │ │  per-user)     │   │ │
    │  │  └──────────┘ └──────────┘ └────────────────┘   │ │
    │  │                                                   │ │
    │  └─────────────────────────────────────────────────┘ │
    │                                                       │
    │  ┌──────────────────┐  ┌──────────────────────────┐  │
    │  │ Config Directory │  │ Data Directory            │  │
    │  │ /etc/syxx-cipher/│  │ /var/lib/syxx-cipher/     │  │
    │  │                  │  │                           │  │
    │  │ cipher.toml      │  │ users.db (SQLite)         │  │
    │  │ provider.key     │  │ analytics.db (SQLite)     │  │
    │  │ blocklists/      │  │ cache.db (LMDB)           │  │
    │  │ rpz-zones/       │  │ receipts/ (append-only)   │  │
    │  └──────────────────┘  └──────────────────────────┘  │
    │                                                       │
    └─────────────────────────────────────────────────────┘
```

### 8.2 Key Management

**Provider Key (Ed25519):**
- Generated once during server initialization: `syxx-cipherd init`.
- Private key stored in `/etc/syxx-cipher/provider.key` with mode `0600`.
- Recommended: store in HSM or hardware token (YubiKey, Nitrokey) for production.
- Public key is embedded in all user stamps.
- Used to sign server certificates (ephemeral X25519 keys).
- Rotation: manual process, requires re-issuing all user stamps. Should be rare (years).

**Server Key (X25519 ephemeral):**
- Generated automatically on server start and rotated every 24 hours (configurable).
- Signed certificate: `Ed25519_Sign(provider_private_key, server_public_key || serial || validity_period)`.
- Old server keys are discarded after rotation (no longer needed).
- Stored in memory only; never written to disk.

**Per-User PSKs (32 bytes):**
- Generated during user provisioning: `syxx-cipherd user add <name>`.
- Stored in `users.db` (SQLite, encrypted at rest with server master key).
- Each PSK is independent; rotating one user's PSK does not affect others.
- Revocation: `syxx-cipherd user revoke <name>` marks PSK as revoked with configurable
  grace period (default: 24 hours).

**Key hierarchy in storage:**

```
    /etc/syxx-cipher/
    ├── cipher.toml              # Server configuration
    ├── provider.key             # Ed25519 provider private key (0600)
    ├── provider.pub             # Ed25519 provider public key (0644)
    ├── master.key               # Master key for encrypting user DB (0600)
    ├── blocklists/              # Blocklist zone files
    │   ├── malware.zone
    │   ├── ads.zone
    │   └── trackers.zone
    ├── rpz-zones/               # Response Policy Zone files
    │   ├── custom-rpz.zone
    │   └── parental-rpz.zone
    └── allowlists/
        └── default-allow.zone
```

### 8.3 Caching Strategy

The server implements a multi-layer caching strategy derived from Unbound:

**Layer 1: Message Cache**
- Caches complete DNS response messages keyed by (QNAME, QTYPE, QCLASS).
- TTL-based expiration following the response's minimum TTL.
- Maximum cache size: configurable (default: 50 MB, ~500K entries).
- Cache entries include DNSSEC validation status.

**Layer 2: RRset Cache**
- Caches individual resource record sets.
- Enables partial cache hits (e.g., A record cached but AAAA requires new query).
- Shared with the message cache for efficiency.

**Layer 3: Key Cache**
- Caches DNSKEY and DS records to accelerate DNSSEC validation.
- Reduces the number of upstream queries needed for chain-of-trust validation.
- TTL-based expiration following the DNSKEY/DS TTL.

**Layer 4: Aggressive NSEC Cache**
- Caches NSEC and NSEC3 records from negative responses.
- Enables synthesis of negative answers for names within cached NSEC intervals (RFC 8198).
- Significantly reduces upstream query volume for NXDOMAIN-heavy workloads.

**Layer 5: Authority Zones (Optional)**
- Complete zone copies loaded locally (e.g., root zone, commonly used TLDs).
- Eliminates upstream queries for these zones entirely.
- Updated via zone transfer (AXFR/IXFR) or HTTP fetch.

**Cache interaction with constant-time responses:** The cache layer operates independently
of the constant-time normalization. Fast cache hits are delayed to match the target
response time, so the client cannot distinguish cache state from response timing.

### 8.4 Rate Limiting and Abuse Prevention

**Per-session rate limiting:**
- Maximum queries per second per session: 100 (configurable).
- Exceeded queries receive an encrypted error response with code `0x05` (RATE_LIMITED).
- Rate limiter uses a token bucket algorithm with burst allowance of 2x the rate limit.

**Per-IP rate limiting:**
- Maximum new sessions per minute per IP: 10 (configurable).
- Prevents session-flooding attacks.
- Exceeded connections are silently dropped (no error response).

**Amplification prevention:**
- Over UDP: the server ensures the response size never exceeds the query size by more
  than a factor of 2 (1024B response vs 512B query). This is within the acceptable
  amplification factor for DNS.
- Over TCP: no amplification concern (connection-oriented).
- Relay mode: the relay MUST verify that query and response sizes match the protocol
  fixed sizes before forwarding, preventing the relay from being used as an amplifier.

**Anti-enumeration:**
- Failed handshake attempts (wrong PSK) are silently dropped with no error response.
- The `BAD_HANDSHAKE` error is sent only for malformed messages, not for authentication
  failures, preventing PSK enumeration.
- After 10 failed handshake attempts from the same IP in 60 seconds, the IP is
  temporarily blocked for 300 seconds.

### 8.5 Integration with Syxx Dimensions

Syxx Cipher is designed to integrate with the Syxx Dimensions server for:

**Leak Detection Integration:**
- The Syxx Cipher server can serve as the leak detection collector endpoint.
- `syxx leak` sends test queries through the Cipher protocol to verify that DNS traffic
  is routed through the expected path.
- The server attests to observed queries with Ed25519 signatures, providing the same
  collector-confirmed leak proof as the existing Syxx leak detection system.

**Investigation API:**
- `syxx mirror` can use a Syxx Cipher server as one of its vantage points for
  cross-vantage comparison.
- The server provides resolution receipts that can be included in Syxx investigation
  reports as evidence of answer integrity.

**Witness Integration:**
- Resolution receipts and Ed25519-signed responses from Syxx Cipher can be incorporated
  into Syxx Witness signed evidence packages.
- The Cipher server's attestation key can be registered as a trusted signer in the
  Witness verification chain.

---

## 9. Security Analysis

### 9.1 Formal Security Properties

The Syxx Cipher protocol provides the following security properties under the standard
assumptions for its cryptographic primitives:

**Confidentiality (IND-CCA2):**
- All query and response content is encrypted using XChaCha20-Poly1305, which provides
  IND-CCA2 security under the assumption that XChaCha20 is a secure PRF and Poly1305
  is a secure MAC.
- The per-message nonce construction (client_nonce || server_nonce) ensures unique nonces
  with overwhelming probability for the 24-byte XChaCha20 nonce space.
- Forward secrecy: session keys are derived from ephemeral DH contributions. Compromise
  of long-term keys does not expose past session content.

**Integrity (INT-CTXT):**
- AEAD authentication provides ciphertext integrity: any modification of encrypted
  messages is detected.
- Ed25519 signatures on responses and receipts provide additional integrity guarantees
  that survive beyond the session (non-repudiable).

**Authentication:**
- Server authentication: the client verifies the server's identity via the pinned Ed25519
  provider key in the stamp. The server proves possession of the corresponding private
  key by signing ephemeral certificates.
- Client authentication: the server identifies the client via the per-user PSK mixed into
  the Noise handshake. No plaintext identity is transmitted.

**Replay Resistance:**
- Within a session: client nonces are tracked; duplicate nonces are rejected.
- Across sessions: session tickets include a creation timestamp and are valid for a
  bounded period (default: 24 hours). The server tracks used tickets to prevent replay.
- 0-RTT data: DNS queries are idempotent, so replay has no side effects. The server
  MAY silently accept replayed tickets without processing the enclosed query.

### 9.2 Comparison to DNSCrypt Security Model

| Property | DNSCrypt v2 | Syxx Cipher |
|----------|-------------|-------------|
| Key exchange | Per-query X25519 | Noise_IK session-based X25519 |
| Forward secrecy | Per-query (ephemeral client key) | Per-session (ephemeral keys in handshake) |
| Client authentication | None | Per-user PSK |
| Nonce construction | 12B client + 12B server | Same (Noise-derived) |
| Response binding | Client nonce echoed | Client nonce echoed (identical) |
| Padding | 64-byte multiples | Fixed-size blocks |
| Anti-amplification | Response ≤ query size | Fixed 2:1 ratio (1024B/512B) |
| Non-repudiation | None (MAC only) | Ed25519 signatures |
| Answer integrity proof | None | Resolution receipts + DNSSEC |
| Replay protection | Per-nonce tracking | Per-nonce + session ticket tracking |
| PSK support | None (PQDNSCrypt only) | Native (Noise PSK position 0) |
| Formal verification | None | Noise_IK pattern has formal proofs |

**Key improvements over DNSCrypt:**
1. Per-user authentication without network-visible identity.
2. Session-based operation eliminates per-query key exchange overhead.
3. Non-repudiable response signatures enable third-party verification.
4. Resolution receipts provide answer integrity evidence beyond DNSSEC.
5. Fixed-size padding eliminates size-based traffic analysis entirely.
6. Formal security properties inherited from the Noise Protocol Framework.

### 9.3 Known Limitations and Mitigations

| Limitation | Impact | Mitigation |
|-----------|--------|-----------|
| Single server (no HA) | Server failure = total outage | Client implements failover to secondary Cipher server or fallback to system DNS |
| PSK lookup scales linearly | Server with >256 users has slower handshake | Bloom filter optimization; shard users across multiple servers |
| Fixed-size padding wastes bandwidth | 8-12x overhead vs. unpadded queries | Acceptable for modern connections; optional variable padding for trusted networks |
| Constant-time responses add latency | Cached queries delayed up to `constant_time_ms` | Configurable; users can disable for performance at the cost of timing leakage |
| Cover traffic increases bandwidth | Additional queries proportional to `cover_rate` | Opt-in; rate tunable per threat model |
| No browser support | Requires dedicated client | Out of scope (NG1); DoH exists for browser use |
| Relay trust model | Colluding relay + server breaks IP privacy | Client selects relay independently; use multiple relays for high-security scenarios |
| DNSSEC coverage incomplete | ~70% of domains unsigned | Resolution receipts provide audit trail; multi-path cross-reference for unsigned zones |
| 0-RTT replay | Captured first messages can be replayed | DNS queries are idempotent; server can reject without processing |

### 9.4 Post-Quantum Readiness Assessment

**Current state:** Syxx Cipher uses X25519 for key exchange, which is vulnerable to a
sufficiently powerful quantum computer (Shor's algorithm).

**Migration path:** The protocol defines a post-quantum variant using X-Wing (ML-KEM-768 +
X25519), which provides both classical and post-quantum security simultaneously.

**Readiness assessment:**

| Aspect | Status | Notes |
|--------|--------|-------|
| KEM algorithm | Ready | X-Wing is specified and has reference implementations |
| Protocol integration | Designed | Handshake variant defined in Section 2.7 |
| Performance overhead | Moderate | ~1120 bytes additional ciphertext per handshake; no per-query overhead |
| Implementation availability | Emerging | liboqs, CIRCL, and other libraries provide ML-KEM-768 |
| Migration mechanism | Defined | Server capability flag in stamp + handshake version field |
| Backward compatibility | Preserved | PQ-naive clients continue using X25519; PQ-capable clients negotiate X-Wing |
| NIST standardization | Complete | ML-KEM (FIPS 203) standardized |

**Recommendation:** Implement the classical (X25519-only) variant first. Add X-Wing support
in Phase 5 or when PQ-capable crypto libraries are available in the target implementation
languages.

---

## 10. Implementation Plan

### 10.1 Phase 1: Core Protocol

**Scope:** Handshake, encryption, single-user operation.

**Deliverables:**
- Noise_IK handshake implementation (client and server)
- XChaCha20-Poly1305 AEAD encryption for queries and responses
- Fixed-size padding (512B queries, 1024B responses)
- UDP transport (TCP fallback)
- Stream multiplexing (basic)
- Session establishment (1-RTT) and resumption (0-RTT)
- Stamp parser (`cipher://` URI)
- Single-user operation (no per-user policy)
- Basic recursive resolution (no DNSSEC validation yet)
- Integration test: real query/response over encrypted channel

**Files to create:**

| File | Language | Purpose |
|------|----------|---------|
| `Server/cipher/protocol.py` | Python | Protocol constants, message types, flag definitions |
| `Server/cipher/noise_handshake.py` | Python | Noise_IK handshake implementation |
| `Server/cipher/transport.py` | Python | UDP/TCP listener, framing, multiplexing |
| `Server/cipher/session.py` | Python | Session state management, key derivation |
| `Server/cipher/padding.py` | Python | Fixed-size padding (ISO 7816-4) |
| `Server/cipher/stamp.py` | Python | Stamp generation and parsing |
| `Server/cipher/resolver.py` | Python | Basic recursive resolver (stub for Phase 1) |
| `macOS/Sources/SyxxKit/Cipher/CipherSession.swift` | Swift | Client-side session management |
| `macOS/Sources/SyxxKit/Cipher/CipherHandshake.swift` | Swift | Client-side Noise_IK handshake |
| `macOS/Sources/SyxxKit/Cipher/CipherTransport.swift` | Swift | Client-side UDP/TCP transport |
| `macOS/Sources/SyxxKit/Cipher/CipherStamp.swift` | Swift | Client-side stamp parser |
| `Windows/Syxx.Kit/Cipher/CipherSession.cs` | C# | Client-side session management (parity) |
| `Windows/Syxx.Kit/Cipher/CipherHandshake.cs` | C# | Client-side Noise_IK handshake (parity) |
| `Windows/Syxx.Kit/Cipher/CipherTransport.cs` | C# | Client-side UDP/TCP transport (parity) |
| `Windows/Syxx.Kit/Cipher/CipherStamp.cs` | C# | Client-side stamp parser (parity) |

**Dependencies:**
- Python: `cryptography` or `PyNaCl` for X25519, Ed25519, XChaCha20-Poly1305
- Swift: `CryptoKit` (native X25519, Ed25519); `swift-crypto` for XChaCha20-Poly1305
- C#: `NSec` or `BouncyCastle` for X25519, Ed25519, XChaCha20-Poly1305

**Estimated effort:** 3-4 weeks (1 full-time developer).

**Verification:**
- Handshake test vectors (known key pairs, verify derived session keys match)
- Encryption test vectors (known plaintext, key, nonce → known ciphertext)
- Round-trip test: encrypt query, decrypt on server, encrypt response, decrypt on client
- Real run: client resolves `example.com` through encrypted channel, verify correct A record

### 10.2 Phase 2: Per-User Policy Engine

**Scope:** Multi-user authentication, policy configuration, structured block responses.

**Deliverables:**
- PSK-based user authentication in handshake
- User provisioning CLI (`syxx-cipherd user add/remove/revoke`)
- User database (SQLite)
- Policy configuration parser (TOML format)
- Query evaluation pipeline (RPZ trigger matching)
- Blocklist zone file loading and management
- Category-based domain classification
- Structured block response generation with Ed25519 signatures
- Per-user allowlists, denylists, rewrites
- Parental controls (basic: category blocking, SafeSearch)

**Estimated effort:** 3-4 weeks.

**Verification:**
- Multi-user test: two users with different policies get different responses for the same query
- Block test: query for known malware domain returns structured block response with valid signature
- Allow test: query for allowlisted domain resolves even when blocklist would block it
- Revocation test: revoked user's PSK is rejected after grace period

### 10.3 Phase 3: Answer Integrity

**Scope:** DNSSEC validation, resolution receipts, Ed25519 attestation.

**Deliverables:**
- Full DNSSEC chain-of-trust validation (root anchor → leaf)
- DNSSEC status in response metadata
- Resolution receipt generation and serialization
- Ed25519 response attestation signing
- Client-side receipt verification
- Aggressive NSEC synthesis
- QNAME minimisation
- Authority zone loading (optional)

**Estimated effort:** 4-5 weeks.

**Verification:**
- DNSSEC test: query for `dnssec.works` returns VALIDATED status
- DNSSEC failure test: query for `fail01.dnssec.works` returns BOGUS status
- Receipt test: verify receipt signature, verify delegation chain is complete and consistent
- Aggressive NSEC test: after NXDOMAIN for `a.example.com`, synthesize NXDOMAIN for `b.example.com` from cached NSEC
- QNAME min test: verify root server receives only TLD in QNAME

### 10.4 Phase 4: Traffic Analysis Resistance + Relay

**Scope:** Constant-time responses, cover traffic, relay support.

**Deliverables:**
- Constant-time response normalization with configurable target
- Response jitter
- Cover traffic client implementation
- Cover traffic server handling
- Relay protocol implementation (client, relay, server)
- Relay stamp format
- Anti-amplification measures for relay mode

**Estimated effort:** 2-3 weeks.

**Verification:**
- Timing test: measure response time variance across 1000 queries; verify standard deviation is within jitter range
- Cover traffic test: verify cover queries are processed and discarded without logging
- Relay test: verify server sees relay IP, not client IP; verify relay cannot decrypt queries
- Size test: capture traffic, verify all queries are exactly 512B and all responses are exactly 1024B

### 10.5 Phase 5: Integration with Syxx Dimensions

**Scope:** Leak detection integration, investigation API, witness integration, post-quantum.

**Deliverables:**
- Syxx Cipher as leak detection collector endpoint
- `syxx mirror` vantage point integration
- Resolution receipt inclusion in Witness evidence packages
- Multi-path cross-reference implementation
- X-Wing KEM integration (post-quantum)
- GUI integration (SyxxKit Cipher settings, connection management)

**Estimated effort:** 3-4 weeks.

**Verification:**
- Leak test: `syxx leak` through Cipher protocol detects expected resolver IPs
- Mirror test: `syxx mirror` includes Cipher server as vantage point, compares results
- Witness test: Cipher resolution receipt passes Witness signature verification
- PQ test: handshake with X-Wing KEM produces same session keys as reference implementation

### 10.6 Server Language Recommendation

**Primary recommendation: Python**

| Factor | Python | Rust |
|--------|--------|------|
| Syxx server integration | Native (existing Syxx server is Python) | Requires FFI or separate service |
| Development speed | Fast (high-level, rapid prototyping) | Slow (systems language, borrow checker) |
| Performance | Adequate for single-server (<100 users) | Excellent (production-scale) |
| Crypto libraries | PyNaCl, cryptography (mature) | ring, sodiumoxide (mature) |
| DNS resolution | dnslib, dnspython (adequate) | trust-dns (excellent) |
| Deployment | pip install, systemd service | compiled binary, systemd service |
| Unbound integration | Python module interface available | Requires FFI to libunbound |

**Rationale:** The Syxx server is already Python. Implementing Syxx Cipher in Python
enables direct integration with the existing leak collector, content-integrity proxy,
and licensing server. For a single-server deployment with fewer than 100 concurrent
users, Python's performance is adequate. The Noise handshake and AEAD operations use
C-backed libraries (libsodium via PyNaCl), providing near-native crypto performance.

**When to consider Rust:** If the server needs to support more than 500 concurrent users
or if latency requirements exceed Python's capabilities (measured, not assumed), a Rust
implementation should be developed. The protocol specification is language-agnostic; the
Python implementation serves as the reference.

### 10.7 Client Library Design

The client library is embedded in SyxxKit (macOS/Linux) and Syxx.Kit (Windows/Linux):

```
    SyxxKit (Swift) / Syxx.Kit (C#)
    │
    ├── CipherSession
    │   ├── connect(stamp: CipherStamp) → establishes session
    │   ├── query(name: String, type: QType) → CipherResponse
    │   ├── close() → graceful session termination
    │   └── state: SessionState (connecting, established, resuming, closed)
    │
    ├── CipherStamp
    │   ├── parse(uri: String) → CipherStamp
    │   ├── generate(serverPK, userPSK, address, properties) → String
    │   └── properties: ServerCapabilities
    │
    ├── CipherResponse
    │   ├── dnsResponse: DNSMessage
    │   ├── dnssecStatus: DNSSECStatus (unchecked, validated, insecure, bogus)
    │   ├── blockInfo: BlockInfo? (if blocked)
    │   ├── receipt: ResolutionReceipt? (if requested)
    │   ├── crossReference: CrossReferenceResult? (if requested)
    │   └── signature: Ed25519Signature?
    │
    └── CipherClient
        ├── configure(stamps: [CipherStamp]) → set up resolvers
        ├── resolve(name, type) → uses primary Cipher session
        └── fallback: system DNS or secondary Cipher server
```

**Thread safety:** `CipherSession` is designed to be used from multiple concurrent callers.
Internal state is protected by a serial dispatch queue (Swift) or `SemaphoreSlim` (C#).
Stream multiplexing enables concurrent queries on a single session without external
synchronization.

---

## 11. Appendix

### A. IETF Draft Outline

For future standardization, the following outline maps to IETF Internet-Draft structure:

```
draft-syxx-cipher-protocol-00

1. Introduction
   1.1. Background and Motivation
   1.2. Terminology (RFC 8174)
   1.3. Scope

2. Protocol Overview
   2.1. Architecture
   2.2. Design Goals

3. Cryptographic Foundations
   3.1. Noise_IK Handshake Pattern
   3.2. XChaCha20-Poly1305 AEAD
   3.3. Ed25519 Signatures
   3.4. Key Derivation (HKDF-SHA256)

4. Wire Protocol
   4.1. Message Header
   4.2. Handshake Messages
   4.3. Transport Messages
   4.4. Stream Multiplexing
   4.5. Error Handling

5. Stamp Format
   5.1. URI Scheme
   5.2. Binary Payload

6. Policy Engine
   6.1. User Identity Model
   6.2. Query Evaluation Pipeline
   6.3. Block Response Format

7. Answer Integrity
   7.1. DNSSEC Validation
   7.2. Resolution Receipts
   7.3. Response Attestation

8. Traffic Analysis Resistance
   8.1. Fixed-Size Padding
   8.2. Constant-Time Responses
   8.3. Cover Traffic

9. Relay Extension
   9.1. Architecture
   9.2. Relay Protocol

10. Security Considerations
    10.1. Threat Model
    10.2. Security Properties
    10.3. Known Limitations
    10.4. Post-Quantum Migration

11. IANA Considerations
    11.1. Port Registration (5443)
    11.2. URI Scheme Registration (cipher://)

12. References
```

### B. Reference Implementation Architecture

```
    syxx-cipherd (server)
    │
    ├── main.py                     # Entry point, configuration loading
    │
    ├── protocol/
    │   ├── constants.py            # Magic bytes, version, message types
    │   ├── header.py               # Header serialization/deserialization
    │   ├── noise.py                # Noise_IKpsk0 handshake (via noisesocket or custom)
    │   ├── session.py              # Session state, key management
    │   ├── transport.py            # UDP/TCP listener, framing
    │   ├── multiplexer.py          # Stream ID allocation and tracking
    │   └── padding.py              # ISO 7816-4 padding
    │
    ├── resolver/
    │   ├── recursive.py            # Recursive resolution engine
    │   ├── dnssec.py               # DNSSEC chain-of-trust validation
    │   ├── qname_min.py            # QNAME minimisation (RFC 9156)
    │   ├── aggressive_nsec.py      # Aggressive NSEC synthesis (RFC 8198)
    │   ├── cache.py                # Multi-layer cache (message, RRset, key, NSEC)
    │   └── authority_zones.py      # Local zone serving
    │
    ├── policy/
    │   ├── engine.py               # Query evaluation pipeline
    │   ├── rpz.py                  # Response Policy Zone processing
    │   ├── categories.py           # Category classification
    │   ├── blocklists.py           # Blocklist loading and matching
    │   ├── parental.py             # Parental control enforcement
    │   └── schedule.py             # Time-based policy evaluation
    │
    ├── integrity/
    │   ├── receipt.py              # Resolution receipt generation
    │   ├── attestation.py          # Ed25519 response signing
    │   └── multipath.py            # Multi-path cross-reference
    │
    ├── traffic/
    │   ├── constant_time.py        # Response time normalization
    │   ├── cover.py                # Cover traffic handling
    │   └── relay.py                # Relay protocol processing
    │
    ├── admin/
    │   ├── cli.py                  # Admin CLI (user add/remove, config)
    │   ├── key_management.py       # Key generation, rotation
    │   └── analytics.py            # Analytics query/export
    │
    └── integration/
        ├── leak_collector.py       # Syxx Dimensions leak detection
        ├── mirror_vantage.py       # Syxx Mirror vantage point
        └── witness_bridge.py       # Syxx Witness evidence bridge
```

### C. Test Vectors

#### C.1 Noise_IK Handshake Test Vector

```
Provider private key (Ed25519):
  9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60

Provider public key (Ed25519):
  d75a980182b10ab7d54bfed3c964073a0ee172f3daa3f4a18446b0b8d183f8e3

Server static private key (X25519):
  a8471a20f6f1c17b80e9cf814062a02d4e0484f5c2b1a9e3d7f6c5b4a3928170

Server static public key (X25519):
  de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f

User PSK (32 bytes):
  4a6f686e446f655573657250534b303132333435363738394142434445463031

Client ephemeral private key (X25519):
  892f26fc1bf0c1a29e6f9e1c3d8a0b5e6f4c3a2b1d0e9f8a7b6c5d4e3f2a1b0c

Client ephemeral public key (X25519):
  285415590e4c8e0b4a3e5c7d9f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f

Client static private key (X25519):
  b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c

Client static public key (X25519):
  5f9e8d7c6b5a4938271f0e6d5c4b3a291807f6e5d4c3b2a1908f7e6d5c4b3a29

Noise protocol name:
  "Noise_IKpsk0_25519_XChaCha20Poly1305_BLAKE2s"

Expected chaining key after Message 1 processing:
  (implementation-dependent; verify against Noise reference implementation)

Expected transport keys after handshake:
  send_key = HKDF(chaining_key, "syxx-cipher-send-v1", 32)
  recv_key = HKDF(chaining_key, "syxx-cipher-recv-v1", 32)
```

#### C.2 Encrypted Query Test Vector

```
Transport key (32 bytes):
  7c8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f

Counter: 0x0000000000000000 (first query)
Nonce (24 bytes): 0x0000000000000000 || counter = 0x000000000000000000000000000000000000000000000000

DNS query wire format (example.com A):
  0000 0100 0001 0000 0000 0000
  0765 7861 6d70 6c65 0363 6f6d 0000 0100 01

Plaintext (before padding, 29 bytes):
  0000 0100 0001 0000 0000 0000
  0765 7861 6d70 6c65 0363 6f6d 0000 0100 01

Padded plaintext (504 bytes, after subtracting 8-byte header from 512-byte block):
  0000 0100 0001 0000 0000 0000
  0765 7861 6d70 6c65 0363 6f6d 0000 0100 01
  80 00 00 00 ... (475 NUL bytes)

Associated data (header):
  C6 01 00 03 0001 01F8

Ciphertext + tag (504 + 16 = 520 bytes, but truncated to fit 504-byte payload field):
  (implementation-dependent; verify against XChaCha20-Poly1305 reference)
```

#### C.3 Structured Block Response Test Vector

```
Block code: 0x0003 (CATEGORY_MALWARE)
Category: "Security > Malware"
Reason text: "Domain classified as malware distribution (ThreatFeed-Alpha)"
Source: "threatfeed-alpha-2026-07"
Override allowed: false
Override token: 0x00000000000000000000000000000000
Log ID: 0x000000000001A2B3
Response timestamp: 1753526400000 (2026-07-26T12:00:00Z)

Attestation signing input:
  0003 || "Security > Malware" || "Domain classified as..." || "threatfeed-alpha-2026-07" || 1753526400000

Ed25519 signature (64 bytes):
  (implementation-dependent; verify against Ed25519 reference)
```

#### C.4 Stamp Encoding Test Vector

```
Input parameters:
  Protocol ID: 0xC6
  Properties: 0x0000000000000345 (DNSSEC + FILTERING + RECEIPTS + COVER_TRAFFIC + CONSTANT_TIME)
  Address: "203.0.113.1:5443" (17 bytes)
  Provider PK: d4b1c8a7f2e39d04a5c6b7e8f1a2d3c4e5f6a7b8c9d0e1f2a3b4c5d6e7f8091a (32 bytes)
  User PSK: 1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f1a2b (32 bytes)
  Name: "my-syxx-server" (14 bytes)

Binary payload (hex):
  C6 4503000000000000 11 3230332E302E3131332E313A35343433
  20 D4B1C8A7F2E39D04A5C6B7E8F1A2D3C4E5F6A7B8C9D0E1F2A3B4C5D6E7F8091A
  20 1A2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F9A0B1C2D3E4F5A6B7C8D9E0F1A2B
  0E 6D792D737978782D736572766572

Base64url encoded stamp:
  cipher://zAUAAAAAAAAAAAAAEzIwMy4wLjExMy4xOjU0NDMg1LHIp_LjnQSlxrf...
```

### D. Glossary

| Term | Definition |
|------|-----------|
| **AEAD** | Authenticated Encryption with Associated Data. A symmetric encryption scheme that provides both confidentiality and integrity. |
| **Attestation key** | The Ed25519 private key used by the server to sign responses for non-repudiation. |
| **Block code** | A machine-readable 16-bit identifier indicating why a query was blocked. |
| **Chaining key** | An intermediate value in the Noise handshake from which session keys are derived. |
| **Constant-time response** | A server behavior where all responses take approximately the same time, regardless of whether the answer was cached or required recursive resolution. |
| **Cover traffic** | Dummy queries sent by the client at randomized intervals to mask real query patterns from timing analysis. |
| **Delegation chain** | The sequence of DNS delegations from the root zone to the authoritative zone for a queried domain. |
| **EDE** | Extended DNS Errors (RFC 8914). An EDNS option providing machine-readable error codes. |
| **Forward secrecy** | The property that compromise of long-term keys does not expose past session content. |
| **KEM** | Key Encapsulation Mechanism. A public-key primitive for establishing shared secrets. |
| **Noise_IK** | A Noise Protocol Framework handshake pattern where the Initiator's static key is Immediately transmitted and the Responder's static key is Known to the initiator. |
| **Non-repudiation** | The property that the signer of a message cannot deny having signed it. Provided by digital signatures (Ed25519), not by MACs. |
| **NSEC** | Next Secure record. A DNSSEC record type that proves the non-existence of a domain name by providing the interval between two existing names. |
| **Provider key** | The long-term Ed25519 signing key that identifies the server. Its public key is distributed in stamps. |
| **PSK** | Pre-Shared Key. A 32-byte secret shared between the server and a specific user, used for authentication during the Noise handshake. |
| **QNAME minimisation** | The practice of sending only the minimum necessary query name to each authoritative server during recursive resolution (RFC 9156). |
| **Resolution receipt** | A cryptographic proof generated by the server showing the complete delegation chain used to resolve a query, signed for verifiability. |
| **RPZ** | Response Policy Zones. A standardized mechanism for DNS filtering using zone files with multiple trigger types and actions. |
| **Server key** | A short-term X25519 encryption key, rotated every 24 hours, signed by the provider key. |
| **Session ticket** | An encrypted token issued by the server that allows the client to resume a session without a full handshake. |
| **Stamp** | A `cipher://` URI encoding all parameters needed to connect to a Syxx Cipher server, including keys, address, and capabilities. |
| **Stream** | A logical channel within a session, identified by a 16-bit stream ID, used for one query/response pair. |
| **Transport key** | A 32-byte symmetric key derived from the Noise handshake, used for AEAD encryption of queries and responses within a session. |
| **X-Wing** | A hybrid KEM combining ML-KEM-768 (post-quantum) with X25519 (classical), providing security against both classical and quantum adversaries. |

---

*End of specification.*

*This document is a design specification. No implementation has been written. All cryptographic
choices, wire formats, and protocol behaviors described herein are subject to validation
during implementation. Test vectors in Appendix C are illustrative; implementation must
produce verifiable outputs against reference implementations of each primitive.*
