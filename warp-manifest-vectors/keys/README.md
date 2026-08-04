# `keys/` — TEST-ONLY Ed25519 keypairs

**These are TEST KEYS committed on purpose. Never trust them for anything real.**
They exist solely so the WARP_MANIFEST_V1 fixture corpus is reproducible and so
the untrusted-signer case has a concrete non-pinned key.

| file | meaning |
|------|---------|
| `test_signing_key.pem` | TEST signing private key (PKCS8, unencrypted). Signs every case except 006. |
| `test_signing_key.pub.pem` | its public key (SPKI PEM). |
| `test_signing_key.key_id` | `key_id` = lowercase hex of the raw 32-byte Ed25519 public key. This is the pinned key for every case. |
| `untrusted_key.pem` | TEST **untrusted** private key. Signs case 006 only. |
| `untrusted_key.pub.pem` | its public key. |
| `untrusted_key.key_id` | its `key_id`; deliberately NOT in any case's pinned set. |

Both keypairs are derived deterministically from fixed 32-byte seeds in
`_build_vectors.py`, so regenerating always yields the same `key_id`s.
