"""UTEL-REL-01 effect order and recovery policy, independent of live credentials.

The production adapter must supply the protected v1 admission and CAS journal.
This module never constructs authority, credentials, or a provider request itself.
An unreadable observation or an uncertain prior write leaves the effect pending.
"""

from __future__ import annotations

import hashlib
import json
import re
from dataclasses import dataclass
from typing import Protocol

PACKAGES = ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers")
FEEDS = ("github", "nuget")


class Refused(RuntimeError):
    pass


@dataclass(frozen=True)
class Effect:
    identity: str
    target_digest: str
    request_digest: str


@dataclass(frozen=True)
class Observation:
    # `absent` is not strong absence after a dispatch: package indexing can lag.
    state: str  # absent, matched, mismatched, unknown
    observed_digest: str | None = None


@dataclass(frozen=True)
class Dispatch:
    state: str  # applied, unknown, refused


@dataclass(frozen=True)
class JournalState:
    generation: int
    content_id: str
    effects: dict[str, str]  # pending (omitted), intent, verified


class Journal(Protocol):
    def read(self) -> JournalState: ...

    def compare_and_swap(self, expected: JournalState, effect: str, state: str) -> bool: ...


class Admission(Protocol):
    def authorize(self, content_id: str, effect: str, action: str, request_digest: str) -> bool: ...


class Provider(Protocol):
    def observe(self, effect: Effect) -> Observation: ...

    def dispatch(self, effect: Effect) -> Dispatch: ...


def ordered_effects(manifest: dict) -> tuple[Effect, ...]:
    descriptor = manifest["descriptor"]
    content_id = "sha256:" + hashlib.sha256(
        json.dumps(descriptor, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    ).hexdigest()
    if (
        manifest.get("contentId") != content_id
        or descriptor.get("policyVersion") != "release-successor/1"
        or not re.fullmatch(r"[0-9a-f]{40}", descriptor.get("sourceSha", ""))
        or {row["id"] for row in descriptor["packages"]} != set(PACKAGES)
        or len(descriptor["packages"]) != len(PACKAGES)
        or "standaloneTelemetry" not in descriptor
    ):
        raise Refused("not a qualified three-package successor descriptor")
    packages = {row["id"]: row for row in descriptor["packages"]}
    effects = [
        Effect("tag", descriptor["sourceSha"], content_id),
        Effect("draft", content_id, content_id),
    ]
    for feed in FEEDS:
        for package in PACKAGES:
            artifact = packages[package]["artifact"]
            if (
                not re.fullmatch(r"[0-9a-f]{64}", artifact.get("sha256", ""))
                or not re.fullmatch(r"sha256:[0-9a-f]{64}", artifact.get("payloadSha256", ""))
            ):
                raise Refused(f"{package}: malformed archive or payload digest")
            effects.append(
                Effect(
                    f"{feed}:{package}",
                    artifact["payloadSha256"],
                    artifact["sha256"],
                )
            )
    # Asset uploads are separate remote effects. A draft can survive an
    # interrupted upload without being mistaken for a promoted release.
    effects.extend(
        (
            Effect("channel-asset", content_id, content_id),
            Effect("manifest-asset", content_id, content_id),
            Effect("promote", content_id, content_id),
        )
    )
    return tuple(effects)


def _observe(provider: Provider, effect: Effect) -> Observation:
    try:
        observation = provider.observe(effect)
    except Exception as error:
        raise Refused(f"{effect.identity}: provider observation unreadable: {error}") from error
    if observation.state not in {"absent", "matched", "mismatched", "unknown"}:
        raise Refused(f"{effect.identity}: unrecognized provider observation")
    if observation.state == "matched" and observation.observed_digest != effect.target_digest:
        raise Refused(f"{effect.identity}: observed digest contradicts matched status")
    if observation.state in {"mismatched", "unknown"}:
        raise Refused(f"{effect.identity}: {observation.state} provider observation")
    return observation


def advance(manifest: dict, journal: Journal, admission: Admission, provider: Provider) -> str:
    """Attempt at most one provider effect; return `verified`, `waiting`, or `complete`.

    A live caller first verifies the manifest and archives through release-saga.py.
    The protected operation journal must already exist and bind the content id.
    """
    effects = ordered_effects(manifest)
    current = journal.read()
    if current.content_id != manifest["contentId"] or current.generation < 1:
        raise Refused("protected journal does not bind the release candidate")
    known = {effect.identity for effect in effects}
    if set(current.effects) - known or any(value not in {"intent", "verified"} for value in current.effects.values()):
        raise Refused("protected journal has an unknown effect or state")
    open_effect = False
    for effect in effects:
        state = current.effects.get(effect.identity, "pending")
        if state == "verified" and open_effect:
            raise Refused("protected journal has an out-of-order verified effect")
        if state != "verified":
            if open_effect and state != "pending":
                raise Refused("protected journal has multiple in-flight effects")
            open_effect = True
    for effect in effects:
        state = current.effects.get(effect.identity, "pending")
        if state == "verified":
            if _observe(provider, effect).state != "matched":
                raise Refused(f"{effect.identity}: verified provider effect no longer matches")
            continue
        observation = _observe(provider, effect)
        if state == "intent":
            if observation.state == "absent":
                # A 404 after a send is not proof of no effect on an eventually indexed feed.
                return "waiting"
            if not admission.authorize(manifest["contentId"], effect.identity, "settle", effect.request_digest):
                raise Refused(f"{effect.identity}: settlement admission denied")
            if not journal.compare_and_swap(current, effect.identity, "verified"):
                raise Refused(f"{effect.identity}: journal settlement CAS conflict")
            return "verified"
        if observation.state != "absent":
            raise Refused(f"{effect.identity}: preexisting effect outside the admitted operation")
        if not admission.authorize(manifest["contentId"], effect.identity, "intent", effect.request_digest):
            raise Refused(f"{effect.identity}: intent admission denied")
        if not journal.compare_and_swap(current, effect.identity, "intent"):
            raise Refused(f"{effect.identity}: journal intent CAS conflict")
        # Re-read authority at the remote boundary; preparation-time admission is insufficient.
        if not admission.authorize(manifest["contentId"], effect.identity, "dispatch", effect.request_digest):
            raise Refused(f"{effect.identity}: dispatch admission denied")
        outcome = provider.dispatch(effect)
        if outcome.state not in {"applied", "unknown", "refused"}:
            raise Refused(f"{effect.identity}: unrecognized dispatch outcome")
        if outcome.state == "refused":
            raise Refused(f"{effect.identity}: provider refused the write")
        # Even a success response is not completion. A later invocation must read back.
        return "waiting"
    return "complete"
