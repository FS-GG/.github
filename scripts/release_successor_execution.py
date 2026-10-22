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


@dataclass(frozen=True)
class PendingObservation:
    # Observation context only. A later transition always rereads the journal,
    # verifies the whole prefix and obtains fresh admission before any mutation.
    effect: Effect
    journal: JournalState


@dataclass(frozen=True)
class Transition:
    state: str
    pending: PendingObservation | None = None


class Journal(Protocol):
    def read(self) -> JournalState: ...

    def compare_and_swap(self, expected: JournalState, effect: str, state: str) -> bool: ...


class Admission(Protocol):
    def authorize(self, content_id: str, effect: str, action: str, request_digest: str) -> bool: ...


class Provider(Protocol):
    def observe(self, effect: Effect) -> Observation: ...

    def dispatch(self, effect: Effect) -> Dispatch: ...


_INVOCATION = re.compile(r"[A-Za-z0-9][A-Za-z0-9._:-]{0,127}")
BOUND_INTENT_FENCE = 1  # capability marker: the engine accepts `fence=` and enforces BoundIntentFence


class BoundIntentFence:
    """Opt-in binding of a journal `intent` to the invocation that wrote it.

    One fence belongs to exactly one invocation of a caller. The engine arms it only
    from its own won intent compare-and-swap and spends it only immediately before
    `provider.dispatch`, after re-reading the journal and finding exactly the state
    that write produced. An intent found in the journal at the start of a transition
    (written by any earlier invocation, or by this one in an earlier call) never
    arms the fence and is therefore never a send permit. A permit is single-use and
    at most one is outstanding; once a release is refused the fence arms nothing more.
    Callers that do not pass a fence behave as before.

    Trust root: the release compares the journal's state, not a writer identity, so
    it relies on the journal's compare-and-swap being linearizable (the protected
    journal's non-forced fast-forward plus matching readback is) and on `journal.read()`
    being a fresh read, not a cached one. `invocation` names the owner in refusals; the
    binding itself is the identity of this object and of the state its own write produced.
    """

    def __init__(self, invocation: str):
        if type(invocation) is not str or not _INVOCATION.fullmatch(invocation):
            raise Refused("bound-intent fence requires a closed invocation identifier")
        self.invocation = invocation
        self._armed: tuple[str, str, JournalState] | None = None
        self._released: str | None = None
        self._refused = False
        self._written: list[tuple[str, int]] = []
        self._sent: list[str] = []

    @property
    def written(self) -> tuple[tuple[str, int], ...]:
        """(effect, journal generation) of each intent this invocation wrote."""
        return tuple(self._written)

    @property
    def sends(self) -> tuple[str, ...]:
        """Effects whose single provider send this invocation was permitted."""
        return tuple(self._sent)

    def arm(self, content_id: str, effect: str, written: JournalState) -> None:
        """Bind the intent this invocation just won to its exact journal state."""
        if self._armed is not None or self._released is not None or self._refused:
            raise Refused(f"{effect}: a prior intent in invocation {self.invocation} was never dispatched")
        if effect in self._sent or any(name == effect for name, _ in self._written):
            raise Refused(f"{effect}: this invocation already wrote an intent for the effect")
        if written.effects.get(effect) != "intent":
            raise Refused(f"{effect}: bound journal state carries no intent")
        self._armed = (content_id, effect, written)
        self._written.append((effect, written.generation))

    def release(self, content_id: str, effect: str, journal: Journal) -> None:
        """Spend the permit for exactly this effect; refuse unless the journal still shows our write."""
        armed, self._armed = self._armed, None  # spent on any outcome: never retried
        if armed is None or armed[0] != content_id or armed[1] != effect:
            self._refused = True
            raise Refused(f"{effect}: send fence {self.invocation}: no intent written by this invocation permits a send")
        try:
            current = journal.read()
        except Exception as error:
            self._refused = True
            raise Refused(f"{effect}: send fence {self.invocation}: journal unreadable before send: {error}") from error
        if current != armed[2]:
            self._refused = True
            raise Refused(f"{effect}: send fence {self.invocation}: journal no longer holds the intent this invocation wrote")
        self._released = effect
        self._sent.append(effect)

    def guard(self, provider: Provider) -> Provider:
        """A provider whose `dispatch` itself refuses unless this fence just released that effect."""
        return _GuardedProvider(provider, self)

    def _consume(self, effect: str) -> None:
        released, self._released = self._released, None
        if released != effect:
            raise Refused(f"{effect}: send fence {self.invocation}: no intent written by this invocation permits a send")


class _GuardedProvider:
    def __init__(self, provider: Provider, fence: BoundIntentFence):
        self._provider, self._fence = provider, fence

    def observe(self, effect: Effect) -> Observation:
        return self._provider.observe(effect)

    def dispatch(self, effect: Effect) -> Dispatch:
        self._fence._consume(effect.identity)
        return self._provider.dispatch(effect)


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
    for package in PACKAGES:
        digest = packages[package]["artifact"]["sha256"]
        effects.append(Effect(f"archive-asset:{package}", digest, digest))
    effects.extend(
        (
            Effect("qualification-asset:evidence", content_id, content_id),
            Effect("qualification-asset:runtime", content_id, content_id),
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


def advance(manifest: dict, journal: Journal, admission: Admission, provider: Provider, *, fence: BoundIntentFence | None = None) -> str:
    """Attempt at most one provider effect; return `verified`, `waiting`, or `complete`.

    A live caller first verifies the manifest and archives through release-saga.py.
    The protected operation journal must already exist and bind the content id.
    """
    return advance_effects(manifest["contentId"], ordered_effects(manifest), journal, admission, provider, fence=fence)


def advance_effects(content_id: str, effects: tuple[Effect, ...], journal: Journal, admission: Admission, provider: Provider, *, fence: BoundIntentFence | None = None) -> str:
    """Apply the same admitted, readback-first policy to an exact ordered effect set."""
    result = full_transition(content_id, effects, journal, admission, provider, fence=fence)
    return "waiting" if result.state == "unknown" else result.state


def full_transition(content_id: str, effects: tuple[Effect, ...], journal: Journal, admission: Admission, provider: Provider, *, prepare_dispatch=None, fence: BoundIntentFence | None = None) -> Transition:
    """One full-prefix transition, never a cached authorization for polling.

    With a `fence`, a provider send additionally requires the intent that this very
    invocation just wrote (see BoundIntentFence); without one the behavior is unchanged.
    """
    if fence is not None:
        provider = fence.guard(provider)
    current = journal.read()
    if current.content_id != content_id or current.generation < 1:
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
                return Transition("waiting", PendingObservation(effect, current))
            if not admission.authorize(content_id, effect.identity, "settle", effect.request_digest):
                raise Refused(f"{effect.identity}: settlement admission denied")
            if not journal.compare_and_swap(current, effect.identity, "verified"):
                raise Refused(f"{effect.identity}: journal settlement CAS conflict")
            return Transition("verified")
        if observation.state != "absent":
            raise Refused(f"{effect.identity}: preexisting effect outside the admitted operation")
        if not admission.authorize(content_id, effect.identity, "intent", effect.request_digest):
            raise Refused(f"{effect.identity}: intent admission denied")
        if not journal.compare_and_swap(current, effect.identity, "intent"):
            raise Refused(f"{effect.identity}: journal intent CAS conflict")
        written = JournalState(current.generation + 1, content_id, {**current.effects, effect.identity: "intent"})
        if fence is not None:
            fence.arm(content_id, effect.identity, written)
        if prepare_dispatch is not None:
            prepare_dispatch(effect)
        # Re-read authority after all potentially slow preparation at the remote boundary.
        if not admission.authorize(content_id, effect.identity, "dispatch", effect.request_digest):
            raise Refused(f"{effect.identity}: dispatch admission denied")
        if fence is not None:
            fence.release(content_id, effect.identity, journal)
        outcome = provider.dispatch(effect)
        if outcome.state not in {"applied", "unknown", "refused"}:
            raise Refused(f"{effect.identity}: unrecognized dispatch outcome")
        if outcome.state == "refused":
            raise Refused(f"{effect.identity}: provider refused the write")
        # Even a success response is not completion. A later independently observed
        # advance must read back; an admitted caller may wait within its original window.
        return Transition("unknown" if outcome.state == "unknown" else "waiting",
                          PendingObservation(effect, written))
    return Transition("complete")
