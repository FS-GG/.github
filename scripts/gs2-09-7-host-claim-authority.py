#!/usr/bin/env python3
"""Source-only durable host claim and revocation authority for GS2-09.7.

No protected store, credential, or provider adapter is installed. The reviewed
store pins are deliberately empty, so no claim or revocation can be confirmed.
"""

import hashlib
import re
from urllib.parse import urlsplit
from typing import Protocol


HEX64 = re.compile(r"[0-9a-f]{64}\Z")
STORE_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-claim-store/1"
REVOKER_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-revoker/1"

# A future protected host revision must pin an independently credentialed CAS
# service that the candidate token and workspace cannot write or delete.
PINNED_STORE_ORIGIN = ""
PINNED_STORE_RESOURCE_ID = ""
PINNED_STORE_ENDPOINT = ""


class Refused(Exception):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


class ProtectedStore(Protocol):
    def describe(self) -> dict: ...
    def cas_claim_once(self, binding_id: str, token_sha256: str) -> str: ...
    def read_claim(self, binding_id: str) -> str: ...
    def append_revoke_intent(self, binding_id: str, token_sha256: str) -> str: ...
    def append_revoked_receipt(self, binding_id: str, token_sha256: str) -> str: ...


class ProtectedRevoker(Protocol):
    def describe(self) -> dict: ...
    def revoke(self, token: str) -> str: ...
    def observe(self, token: str) -> str: ...


def check_store(store: ProtectedStore | None) -> None:
    require(bool(PINNED_STORE_ORIGIN) and bool(PINNED_STORE_RESOURCE_ID)
            and bool(PINNED_STORE_ENDPOINT),
            "store-unconfigured")
    require(store is not None and all(callable(getattr(store, name, None)) for name in
                                       ("describe", "cas_claim_once", "read_claim",
                                        "append_revoke_intent", "append_revoked_receipt")),
            "store-unconfigured")
    descriptor = store.describe()
    require(type(descriptor) is dict and set(descriptor) == {
        "schema", "origin", "resourceId", "endpoint", "durable",
        "atomicCas", "nativeReadback", "credentialScope", "candidateCanWrite",
    }, "store-descriptor")
    endpoint = descriptor["endpoint"]
    require(type(endpoint) is str, "store-endpoint")
    try:
        parsed = urlsplit(endpoint)
        pinned = urlsplit(PINNED_STORE_ENDPOINT)
        valid_host = bool(parsed.hostname) and bool(pinned.hostname)
        parsed.port
        pinned.port
    except ValueError as error:
        raise Refused("store-endpoint") from error
    require(parsed.scheme == "https" and valid_host
            and not parsed.username and not parsed.password
            and not parsed.query and not parsed.fragment
            and endpoint == PINNED_STORE_ENDPOINT
            and f"{parsed.scheme}://{parsed.netloc}" == PINNED_STORE_ORIGIN,
            "store-endpoint")
    require(descriptor["schema"] == STORE_SCHEMA
            and descriptor["origin"] == PINNED_STORE_ORIGIN
            and descriptor["resourceId"] == PINNED_STORE_RESOURCE_ID
            and descriptor["durable"] is True
            and descriptor["atomicCas"] is True
            and descriptor["nativeReadback"] is True
            and descriptor["credentialScope"] == "protected-host-only"
            and descriptor["candidateCanWrite"] is False, "store-authority")


def check_revoker(revoker: ProtectedRevoker | None) -> None:
    require(revoker is not None and all(callable(getattr(revoker, name, None)) for name in
                                         ("describe", "revoke", "observe")),
            "revoker-unconfigured")
    descriptor = revoker.describe()
    require(type(descriptor) is dict and descriptor == {
        "schema": REVOKER_SCHEMA,
        "apiOrigin": "https://api.github.com",
        "credentialScope": "protected-host-only",
        "candidateCanWrite": False,
        "nativeObservation": True,
    }, "revoker-authority")


class HostClaimAuthority:
    """Adapter shape for #3712's claim_once/revoke port methods.

    The actual store and revoker must be protected, durable, and independently
    read back. Caller-owned files or self-reported metadata are not authority.
    """

    def __init__(self, expected_binding_id: str, expected_token_sha256: str,
                 store: ProtectedStore | None, revoker: ProtectedRevoker | None):
        require(type(expected_binding_id) is str and HEX64.fullmatch(expected_binding_id)
                and type(expected_token_sha256) is str
                and HEX64.fullmatch(expected_token_sha256), "binding-identity")
        self.binding_id = expected_binding_id
        self.token_sha256 = expected_token_sha256
        self.store = store
        self.revoker = revoker

    def _authority(self) -> None:
        check_store(self.store)
        check_revoker(self.revoker)

    def claim_once(self, binding_id: str) -> str:
        self._authority()
        require(binding_id == self.binding_id, "binding-identity")
        try:
            result = self.store.cas_claim_once(binding_id, self.token_sha256)
        except Exception:
            result = "unknown"
        if result == "committed":
            # A positive CAS response is not proof of durable commit. Require
            # exact native readback before the token can leave host custody.
            try:
                observed = self.store.read_claim(binding_id)
            except Exception:
                observed = "unknown"
            return ("granted" if type(observed) is str
                    and observed == self.token_sha256 else "unknown")
        if result == "duplicate":
            return "duplicate"
        # Readback can locate a possibly committed claim for recovery, but a
        # lost CAS response never grants another token handoff.
        try:
            self.store.read_claim(binding_id)
        except Exception:
            pass
        return "unknown"

    def revoke(self, token: str) -> str:
        self._authority()
        require(type(token) is str and len(token) > 20 and token.isascii()
                and not any(character.isspace() for character in token), "token")
        require(hashlib.sha256(token.encode("ascii")).hexdigest() == self.token_sha256,
                "token-digest")
        try:
            intent = self.store.append_revoke_intent(self.binding_id, self.token_sha256)
        except Exception:
            intent = "unknown"
        # A lost intent response must not keep a minted token alive. The
        # attempted revoke is conservative, but without durable intent and
        # independent native observation it cannot be reported confirmed.
        try:
            self.revoker.revoke(token)
        except Exception:
            pass
        try:
            observed = self.revoker.observe(token)
        except Exception:
            observed = "unknown"
        if intent != "committed" or observed != "revoked":
            return "unknown"
        try:
            receipt = self.store.append_revoked_receipt(self.binding_id,
                                                        self.token_sha256)
        except Exception:
            receipt = "unknown"
        return "confirmed" if receipt == "committed" else "unknown"
