"""Shared value types for the Syxx server.

Phase 0 introduced a canonical tri-state result enum. The three values
("success", "failure", "inconclusive") are the ONLY wire-format strings the
client kits accept from the server; any other spelling breaks JSON parity
with the macOS/Windows SyxxKit twins.

The enum subclasses `str` so it serialises as a bare string under
`json.dumps` without a custom encoder, and so equality comparisons against
literal strings keep working in the rest of the codebase.
"""

from enum import StrEnum


class TriState(StrEnum):
    """Canonical result tri-state.

    - success:      the check ran and its positive condition held.
    - failure:      the check ran and its positive condition did NOT hold.
    - inconclusive: the check could not reach a conclusion (timeout, network
                    error, missing data, ...). Distinct from failure: an
                    inconclusive result must NEVER be read as a negative.
    """

    SUCCESS = "success"
    FAILURE = "failure"
    INCONCLUSIVE = "inconclusive"
    # C222: "unknown" is persisted by the scheduler when history is unreadable or
    # a warp diff cannot determine a verdict. Distinct from inconclusive (which
    # means "measurement could not complete"); unknown means "data exists but
    # cannot be interpreted". Mapped to inconclusive on the wire for clients
    # that do not recognise it.
    UNKNOWN = "unknown"

    def __str__(self) -> str:
        """Render as the bare wire-format string (e.g. ``"success"``)."""
        return self.value
