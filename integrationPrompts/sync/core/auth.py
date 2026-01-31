"""HMAC-SHA256 authentication for Onshape API."""

import base64
import hashlib
import hmac
import os
import secrets
import string
from datetime import datetime, timezone
from urllib.parse import urlparse, urlencode

from dotenv import load_dotenv


class OnshapeAuth:
    """Handles Onshape API authentication using HMAC-SHA256 signatures."""

    def __init__(
        self,
        access_key: str | None = None,
        secret_key: str | None = None,
        base_url: str | None = None,
    ) -> None:
        """Initialize authentication with credentials.

        Args:
            access_key: Onshape API access key (or load from ONSHAPE_ACCESS_KEY env)
            secret_key: Onshape API secret key (or load from ONSHAPE_SECRET_KEY env)
            base_url: Onshape base URL (or load from ONSHAPE_BASE_URL env)
        """
        load_dotenv()

        self.access_key = access_key or os.getenv("ONSHAPE_ACCESS_KEY", "")
        self.secret_key = secret_key or os.getenv("ONSHAPE_SECRET_KEY", "")
        self.base_url = (base_url or os.getenv("ONSHAPE_BASE_URL", "https://cad.onshape.com")).rstrip("/")

        if not self.access_key or not self.secret_key:
            raise ValueError(
                "Missing Onshape API credentials. Set ONSHAPE_ACCESS_KEY and "
                "ONSHAPE_SECRET_KEY environment variables or pass them directly."
            )

    def _generate_nonce(self, length: int = 25) -> str:
        """Generate a random nonce for request signing."""
        chars = string.ascii_lowercase + string.digits
        return "".join(secrets.choice(chars) for _ in range(length))

    def _get_utc_timestamp(self) -> str:
        """Get current UTC timestamp in required format."""
        return datetime.now(timezone.utc).strftime("%a, %d %b %Y %H:%M:%S GMT")

    def _compute_signature(
        self,
        method: str,
        path: str,
        query_string: str,
        nonce: str,
        date: str,
        content_type: str,
    ) -> str:
        """Compute HMAC-SHA256 signature for a request.

        The signature string format is:
        (method + '\n' + nonce + '\n' + date + '\n' + content_type + '\n' +
         path + '\n' + query_string + '\n').lower()
        """
        # Build the string to sign (all lowercase)
        string_to_sign = (
            f"{method}\n"
            f"{nonce}\n"
            f"{date}\n"
            f"{content_type}\n"
            f"{path}\n"
            f"{query_string}\n"
        ).lower()

        # Compute HMAC-SHA256
        signature = hmac.new(
            self.secret_key.encode("utf-8"),
            string_to_sign.encode("utf-8"),
            hashlib.sha256,
        ).digest()

        # Base64 encode the signature
        return base64.b64encode(signature).decode("utf-8")

    def get_headers(
        self,
        method: str,
        path: str,
        query_params: dict[str, str] | None = None,
        content_type: str = "application/json",
    ) -> dict[str, str]:
        """Generate authentication headers for an API request.

        Args:
            method: HTTP method (GET, POST, etc.)
            path: API path (e.g., /api/v10/documents)
            query_params: Optional query parameters
            content_type: Content-Type header value

        Returns:
            Dictionary of headers including Authorization
        """
        method = method.upper()
        nonce = self._generate_nonce()
        date = self._get_utc_timestamp()

        # Build query string from params
        query_string = ""
        if query_params:
            query_string = urlencode(sorted(query_params.items()))

        signature = self._compute_signature(
            method=method,
            path=path,
            query_string=query_string,
            nonce=nonce,
            date=date,
            content_type=content_type,
        )

        # Build Authorization header
        auth_header = f"On {self.access_key}:HmacSHA256:{signature}"

        return {
            "Authorization": auth_header,
            "Date": date,
            "On-Nonce": nonce,
            "Content-Type": content_type,
            "Accept": "application/json",
        }

    def get_full_url(self, path: str, query_params: dict[str, str] | None = None) -> str:
        """Build full URL from base URL, path, and query params.

        Args:
            path: API path (e.g., /api/v10/documents)
            query_params: Optional query parameters

        Returns:
            Full URL string
        """
        url = f"{self.base_url}{path}"
        if query_params:
            url += "?" + urlencode(sorted(query_params.items()))
        return url

    def verify_credentials(self) -> bool:
        """Verify that credentials are set (does not test API connectivity)."""
        return bool(self.access_key and self.secret_key and self.base_url)
