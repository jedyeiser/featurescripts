#!/usr/bin/env python3
"""Standalone script to verify Onshape API authentication.

Usage:
    cd integrationPrompts
    python sync/verify_auth.py
"""

import sys
from pathlib import Path

# Add parent to path for imports
sys.path.insert(0, str(Path(__file__).parent.parent))

from sync.core.auth import OnshapeAuth
from sync.core.client import OnshapeClient, OnshapeAPIError


def main() -> int:
    """Verify authentication and print results."""
    print("=" * 50)
    print("Onshape API Authentication Verification")
    print("=" * 50)

    # Check credentials are set
    print("\n1. Checking credentials...")
    try:
        auth = OnshapeAuth()
        print(f"   Access Key: {auth.access_key[:8]}...{auth.access_key[-4:]}")
        print(f"   Base URL: {auth.base_url}")
        print("   [OK] Credentials loaded")
    except ValueError as e:
        print(f"   [FAIL] {e}")
        print("\n   Make sure you have created a .env file with:")
        print("     ONSHAPE_ACCESS_KEY=your_key")
        print("     ONSHAPE_SECRET_KEY=your_secret")
        print("     ONSHAPE_BASE_URL=https://cad.onshape.com")
        return 1

    # Test API connection
    print("\n2. Testing API connection...")
    try:
        client = OnshapeClient(auth)
        if client.verify_connection():
            print("   [OK] API connection successful")
        else:
            print("   [FAIL] API returned unexpected response")
            return 1
    except OnshapeAPIError as e:
        print(f"   [FAIL] API error: {e}")
        if e.status_code == 401:
            print("\n   Authentication failed. Check your API keys.")
        elif e.status_code == 403:
            print("\n   Access denied. Check API key permissions.")
        return 1
    except Exception as e:
        print(f"   [FAIL] Unexpected error: {e}")
        return 1

    print("\n" + "=" * 50)
    print("All checks passed! Authentication is working.")
    print("=" * 50)

    return 0


if __name__ == "__main__":
    sys.exit(main())
