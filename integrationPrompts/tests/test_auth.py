"""Tests for Onshape authentication."""

import os
from unittest.mock import patch

import pytest

from sync.core.auth import OnshapeAuth


class TestOnshapeAuth:
    """Tests for OnshapeAuth class."""

    def test_init_with_credentials(self) -> None:
        auth = OnshapeAuth(
            access_key="test_access",
            secret_key="test_secret",
            base_url="https://test.onshape.com",
        )

        assert auth.access_key == "test_access"
        assert auth.secret_key == "test_secret"
        assert auth.base_url == "https://test.onshape.com"

    def test_init_missing_credentials(self) -> None:
        # Clear env vars for this test
        with patch.dict(os.environ, {}, clear=True):
            with pytest.raises(ValueError, match="Missing Onshape API credentials"):
                OnshapeAuth()

    def test_generate_nonce(self) -> None:
        auth = OnshapeAuth(
            access_key="test",
            secret_key="test",
        )

        nonce1 = auth._generate_nonce()
        nonce2 = auth._generate_nonce()

        assert len(nonce1) == 25
        assert nonce1 != nonce2  # Should be random

    def test_get_utc_timestamp(self) -> None:
        auth = OnshapeAuth(
            access_key="test",
            secret_key="test",
        )

        timestamp = auth._get_utc_timestamp()

        # Should be in format: "Day, DD Mon YYYY HH:MM:SS GMT"
        assert "GMT" in timestamp
        assert len(timestamp) > 20

    def test_get_headers(self) -> None:
        auth = OnshapeAuth(
            access_key="test_key",
            secret_key="test_secret",
        )

        headers = auth.get_headers(
            method="GET",
            path="/api/v10/documents",
        )

        assert "Authorization" in headers
        assert headers["Authorization"].startswith("On test_key:")
        assert "Date" in headers
        assert "On-Nonce" in headers
        assert headers["Content-Type"] == "application/json"

    def test_get_full_url(self) -> None:
        auth = OnshapeAuth(
            access_key="test",
            secret_key="test",
            base_url="https://cad.onshape.com",
        )

        url = auth.get_full_url("/api/v10/documents")
        assert url == "https://cad.onshape.com/api/v10/documents"

        url_with_params = auth.get_full_url(
            "/api/v10/documents",
            query_params={"foo": "bar", "baz": "qux"},
        )
        assert "foo=bar" in url_with_params
        assert "baz=qux" in url_with_params

    def test_verify_credentials(self) -> None:
        auth = OnshapeAuth(
            access_key="test",
            secret_key="test",
            base_url="https://cad.onshape.com",
        )

        assert auth.verify_credentials() is True

    def test_base_url_trailing_slash_removed(self) -> None:
        auth = OnshapeAuth(
            access_key="test",
            secret_key="test",
            base_url="https://cad.onshape.com/",
        )

        assert auth.base_url == "https://cad.onshape.com"
