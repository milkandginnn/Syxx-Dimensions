"""Tests for syxx_server.lookalike — lookalike/homoglyph domain permutation scanner.

Covers permutation generation (omission, substitution, insertion, transposition,
hyphenation, TLD substitution), domain splitting, and deduplication.
DNS resolution is mocked for scan() tests.
"""

import os

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import lookalike


class TestSplitDomain:
    def test_simple_domain(self):
        label, tld = lookalike._split_domain("example.com")
        assert label == "example"
        assert tld == "com"

    def test_multi_part_tld(self):
        """SM-034: co.uk must not be split as label='co', tld='uk'."""
        label, tld = lookalike._split_domain("example.co.uk")
        assert label == "example"
        assert tld == "co.uk"

    def test_subdomain_with_multi_part_tld(self):
        label, tld = lookalike._split_domain("sub.example.co.uk")
        assert "example" in label
        assert tld == "co.uk"

    def test_single_label(self):
        label, tld = lookalike._split_domain("localhost")
        assert label == "localhost"
        assert tld == ""

    def test_trailing_dot_stripped(self):
        label, tld = lookalike._split_domain("example.com.")
        assert label == "example"
        assert tld == "com"


class TestOmit:
    def test_generates_variants(self):
        variants = list(lookalike._omit("google"))
        assert len(variants) == 6  # one per character position
        labels = [v for v, _ in variants]
        assert "gogle" in labels  # omit second 'o'
        assert "googl" in labels  # omit trailing 'e'

    def test_single_char_returns_empty(self):
        variants = list(lookalike._omit("a"))
        assert len(variants) == 0


class TestSubstitute:
    def test_homoglyph_substitution(self):
        variants = list(lookalike._substitute("google"))
        labels = [v for v, _ in variants]
        # 'o' -> '0' substitution
        assert "g0ogle" in labels or "go0gle" in labels

    def test_no_substitution_for_unmapped_chars(self):
        variants = list(lookalike._substitute("xyz"))
        # x, y, z have no homoglyph mappings
        assert len(variants) == 0

    def test_deduplication(self):
        """Adjacent identical chars should not produce duplicate substitutions."""
        variants = list(lookalike._substitute("oo"))
        labels = [v for v, _ in variants]
        # Both 'o' positions map to '0', producing "0o" and "o0" — both valid
        assert len(labels) == len(set(labels))


class TestInsert:
    def test_doubles_each_letter(self):
        variants = list(lookalike._insert("abc"))
        labels = [v for v, _ in variants]
        assert "aabc" in labels
        assert "abbc" in labels
        assert "abcc" in labels

    def test_sm033_deduplication(self):
        """SM-033: doubling adjacent identical chars must not produce dupes."""
        variants = list(lookalike._insert("oo"))
        labels = [v for v, _ in variants]
        assert len(labels) == len(set(labels))


class TestTranspose:
    def test_swaps_adjacent(self):
        variants = list(lookalike._transpose("abcd"))
        labels = [v for v, _ in variants]
        assert "bacd" in labels  # swap a,b
        assert "acbd" in labels  # swap b,c
        assert "abdc" in labels  # swap c,d

    def test_no_self_swap(self):
        """Adjacent identical chars should not produce the original."""
        variants = list(lookalike._transpose("aab"))
        labels = [v for v, _ in variants]
        assert "aab" not in labels

    def test_single_char(self):
        assert list(lookalike._transpose("a")) == []


class TestHyphenate:
    def test_insert_hyphens(self):
        variants = list(lookalike._hyphenate("abc"))
        labels = [v for v, _ in variants]
        assert "a-bc" in labels
        assert "ab-c" in labels

    def test_remove_hyphens(self):
        variants = list(lookalike._hyphenate("my-domain"))
        labels = [v for v, _ in variants]
        assert "mydomain" in labels


class TestGeneratePermutations:
    def test_generates_variants(self):
        perms = lookalike.generate_permutations("example.com")
        assert len(perms) > 0
        fqdns = [fqdn for fqdn, _ in perms]
        # Original domain must not appear
        assert "example.com" not in fqdns

    def test_tld_substitutions(self):
        perms = lookalike.generate_permutations("example.com")
        fqdns = [fqdn for fqdn, _ in perms]
        assert "example.net" in fqdns
        assert "example.org" in fqdns

    def test_max_permutations_cap(self):
        perms = lookalike.generate_permutations("example.com", max_permutations=5)
        assert len(perms) <= 5

    def test_empty_label(self):
        perms = lookalike.generate_permutations(".com")
        assert perms == []

    def test_all_variants_are_unique(self):
        perms = lookalike.generate_permutations("google.com")
        fqdns = [fqdn for fqdn, _ in perms]
        assert len(fqdns) == len(set(fqdns))


class TestScan:
    @pytest.fixture(autouse=True)
    def _mock_dns(self):
        """Mock DNS resolution so scan() doesn't hit the network."""
        from unittest.mock import patch, MagicMock
        from syxx_server import resolver as _resolver

        def mock_query(domain, qtype, server, timeout=3):
            # Only "example.com" variants resolve
            if "example" in domain and domain != "example.com":
                return {"rcode": 0, "records": [{"type": "A", "value": "93.184.216.34"}]}
            return {"rcode": 3, "records": []}

        with patch.object(lookalike.resolver, "query", side_effect=mock_query):
            with patch.object(lookalike.resolver, "DNSError", Exception):
                with patch.object(lookalike.config, "PUBLIC_RESOLVERS", ["8.8.8.8"]):
                    yield

    def test_scan_returns_structure(self):
        result = lookalike.scan("example.com", max_permutations=20)
        assert "domain" in result
        assert "permutations" in result
        assert "summary" in result
        assert result["domain"] == "example.com"

    def test_scan_summary_counts(self):
        result = lookalike.scan("example.com", max_permutations=20)
        summary = result["summary"]
        assert "total" in summary
        assert "resolving" in summary
        assert "deadline_truncated" in summary
