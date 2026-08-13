"""Shared value types for the Syxx server.

Phase 0 introduced a canonical tri-state result enum. The three values
("success", "failure", "inconclusive") are the ONLY wire-format strings the
client kits accept from the server; any other spelling breaks JSON parity
with the macOS/Windows SyxxKit twins.

The enum subclasses `str` so it serialises as a bare string under
`json.dumps` without a custom encoder, and so equality comparisons against
literal strings keep working in the rest of the codebase.
"""
from enum import Enum


class TriState(str, Enum):
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

    def __str__(self) -> str:
        return self.value
