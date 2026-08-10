"""D-4 DoQ RAM gate — the ship/defer decision logic.

The honesty rule: a measurement that looks bad, or a missing measurement, must DEFER. DoQ shipping
on a box that cannot hold it is an outage waiting to happen; "we could not measure" must never
resolve to "ship". These test the pure gate; the live aioquic run happens on the box.
"""

import os
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "scripts"))
import doq_ram_probe as probe  # noqa: E402


def test_marginal_cost_is_the_delta_over_n():
    assert probe.per_conn_mb(100.0, 150.0, 500) == pytest.approx(0.1)


def test_zero_n_raises_rather_than_dividing():
    with pytest.raises(ValueError):
        probe.per_conn_mb(100.0, 150.0, 0)


def test_constant_per_conn_is_linear():
    assert probe.is_linear({100: 0.10, 500: 0.11, 1000: 0.10})


def test_growing_per_conn_is_not_linear():
    # 0.1 -> 0.5 as N grows: buffer/stream bloat. Must read as non-linear (a defer signal).
    assert not probe.is_linear({100: 0.10, 500: 0.28, 1000: 0.50})


def test_one_point_cannot_judge():
    assert probe.is_linear({100: 0.10}) is None


def test_healthy_linear_with_headroom_ships():
    v = probe.compute_result(
        rss_0_mb=80, per_conn_by_n={100: 0.1, 500: 0.1, 1000: 0.1}, mem_available_mb=4000, realistic_peak_conns=200
    )
    assert v["conclusion"] == "ship"
    assert v["max_safe_conns"] > 200


def test_superlinear_scaling_defers_even_with_ram():
    v = probe.compute_result(
        rss_0_mb=80, per_conn_by_n={100: 0.1, 500: 0.3, 1000: 0.6}, mem_available_mb=8000, realistic_peak_conns=50
    )
    assert v["conclusion"] == "defer"
    assert v["linear"] is False


def test_no_measurement_defers_never_ships():
    v = probe.compute_result(
        rss_0_mb=80, per_conn_by_n={100: None, 500: None}, mem_available_mb=8000, realistic_peak_conns=50
    )
    assert v["conclusion"] == "defer"


def test_tight_ram_defers():
    # Per-conn fine and linear, but the box has almost no free memory after co-residents.
    v = probe.compute_result(
        rss_0_mb=80, per_conn_by_n={100: 0.1, 500: 0.1}, mem_available_mb=420, realistic_peak_conns=200
    )  # 420 - 400 reserve = 20MB budget
    assert v["conclusion"] == "defer"


def test_peak_above_max_safe_defers():
    v = probe.compute_result(
        rss_0_mb=80, per_conn_by_n={100: 0.5, 500: 0.5}, mem_available_mb=1000, realistic_peak_conns=5000
    )
    assert v["conclusion"] == "defer"
