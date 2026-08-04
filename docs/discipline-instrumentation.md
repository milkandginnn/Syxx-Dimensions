# Instrumentation Requirements per Discipline

This file lists precise data and instrumentation changes required in Prophet, Mirror, Portal, Glutton, Cage, and Witness to support the determinative core.

1) Prophet (DNS Resolution & Protocol Integrity)
- Emit EvidenceLedger entries for each probe run with fields:
  - resolver_ip, resolver_port, transport, dns_raw_packet_ref, parsed_dns (qname,qtype,rcode,answers), txid_entropy (float), port_entropy (float), edns0_present (bool), edns0_options (array), dnssec_chain_status (none/partial/valid/invalid), chaos_version (optional string), probe_timeout (ms), resolverDidFail (bool)
- Add CHAOS-class probe (version.bind) and record result as chaos_identity string.
- Ensure telemetry marked with "adversarial_default": true in provenance metadata.

2) Mirror (Tampering & Censorship Detection)
- Emit: local_content_hash (sha256), remote_content_hash (sha256), http_status_local, http_status_remote, tls_pubkey_fingerprint (sha256-spki), sri_status (ok/mismatch/missing), body_length_delta, cname_chain_array, ttl_anomalies (list) and vantage_id.
- Add SRI check routine and TLS pubkey extraction pipeline.

3) Portal (Network & Path Diagnostics)
- Emit: structured_traceroute (array of hop objects: ip, rtt_ms, asn, geoip), last_hop_asn, route_diff_token (hash of path), interface_state snapshot (ip, mac, dhcp_gateway).
- ASN resolution and geoip lookup required in collector or local database; include as optional fields.

4) Glutton (Privacy Leak Detection)
- Emit: collector_signed_payload_ref, collector_pubkey_id, collector_signature_valid (bool), ecsecho_status (echoed ip/ipv6/none), collector_transport_spki (sha256), collector_first_seen (timestamp)
- Bind collector observations to interface and egress ip if possible.

5) Cage (Capture, Forensics & Attribution)
- Emit: pcap_path, capture_start/end timestamps, parsed_records_count, unparseable_count, process_pid, process_binary_signature (code-sign), socket_owner, interface_name, top_n_domains, anomalous_domain_flags (entropy, length, encoding), response_size_histogram.
- Store pcap as on-disk path; ledger stores raw_payload_ref to it. Enforce capture directory ownership checks.

6) Witness (Trust Infrastructure)
- Emit: witness_signature (ed25519), witness_key_id, witness_pubkey_fingerprint, ledger_anchor (sequence number), signer_process_info, sign_time.
- Witness must only sign payloads whose linked evidence.processing_status == 'validated'.

Cross-cutting:
- All evidence entries must include links: array of evidence_ids referenced.
- All modules must populate provenance.transport and provenance.interface; if unknown, set to "unknown" not null.
- Telemetry must be treated adversarial: collectors that accept data from network must be validated (spki pins) before trusting.

