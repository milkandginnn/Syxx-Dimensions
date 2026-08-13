# DEV-ONLY update signing keys (NOT PRODUCTION)

These keys exist so local development and Phase 3.2 client pin tests can form a
closed loop. They are **not** a production update identity.

| File | Contents |
|------|----------|
| `update-signing.priv.b64` | Raw 32-byte Ed25519 seed, base64. Signs appcast manifests. |
| `update-signing.pub.b64` | Matching raw 32-byte public key, base64. Pinned in clients. |

## Rules

1. **Do not reuse the collector / leak identity.** Update signing is a separate
   long-term trust anchor (`SYXX_UPDATE_PRIVATE_KEY` / this pair), never
   `SYXX_COLLECTOR_PRIVATE_KEY`.
2. **Production key rotation is owner-gated.** Shipping a new production public
   key requires an owner-approved client pin change on macOS, Windows, and Linux
   simultaneously. Do not rotate from an agent session by inference.
3. **Never fetch the verification key from the appcast origin.** Clients pin the
   raw public key in the binary. A key served next to the manifest is not a pin.
4. Treat `update-signing.priv.b64` as a development secret. Production deployments
   must supply `SYXX_UPDATE_PRIVATE_KEY` (PKCS8 DER, base64) or
   `SYXX_UPDATE_PRIVATE_KEY_PATH` from offline storage and must not load this
   directory.

## Client pin (current DEV public key)

```
6OcHid3EjTbWJvwIkR10VjQnP+0W9fHTv+wt1NuBqFU=
```

Pinned in:

- `macOS/Sources/SyxxKit/UpdateService.swift` → `AppcastVerifier.pinnedPublicKeysBase64`
- `Windows/dotNET/Syxx.Kit/UpdateService.cs` → `AppcastVerifier.PinnedPublicKeysBase64`

## Server load order

1. `SYXX_UPDATE_PRIVATE_KEY` (PKCS8 DER base64) if set
2. else `SYXX_UPDATE_PRIVATE_KEY_PATH` if set
3. else, only when `SYXX_ALLOW_DEV_UPDATE_KEY=1`, the DEV file in this directory

Production must use (1) or (2). The DEV fallback refuses to load unless the
explicit allow flag is set.
