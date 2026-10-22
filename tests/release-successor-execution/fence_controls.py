"""Causal controls and mutation proofs for the opt-in BoundIntentFence.

Each scenario takes an engine module `E` and raises AssertionError when the fence
property it names does not hold. The same scenarios run against the real engine and
against deliberately broken copies of its source (MUTANTS), so every line of the
fence is shown to matter and the defended regression is shown to be real.
"""

from __future__ import annotations

import hashlib
import json
import pathlib
import sys
import types

SOURCE = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "release_successor_execution.py"


def load(source: str, name: str = "release_successor_execution_fence") -> types.ModuleType:
    module = types.ModuleType(name)
    module.__file__ = str(SOURCE)
    sys.modules[name] = module  # dataclasses resolves annotations through sys.modules
    try:
        exec(compile(source, str(SOURCE), "exec"), module.__dict__)
    finally:
        sys.modules.pop(name, None)
    return module


def manifest(E) -> dict:
    descriptor = {
        "policyVersion": "release-successor/1",
        "sourceSha": "b" * 40,
        "standaloneTelemetry": {"qualificationSha256": "c" * 64},
        "packages": [
            {"id": package, "artifact": {"sha256": str(index) * 64, "payloadSha256": "sha256:" + str(index) * 64}}
            for index, package in enumerate(E.PACKAGES, 1)
        ],
    }
    content_id = "sha256:" + hashlib.sha256(
        json.dumps(descriptor, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    ).hexdigest()
    return {"contentId": content_id, "descriptor": descriptor}


class Journal:
    def __init__(self, E, content_id, effects=None, generation=1):
        self.E = E
        self.state = E.JournalState(generation, content_id, dict(effects or {}))
        self.writes = []
        self.lie = False  # report a won CAS without writing
        self.read_hook = None

    def read(self):
        if self.read_hook is not None:
            self.read_hook()
        return self.state

    def compare_and_swap(self, expected, effect, state):
        if expected != self.state:
            return False
        if self.lie:
            return True
        self.state = self.E.JournalState(expected.generation + 1, expected.content_id, {**expected.effects, effect: state})
        self.writes.append((effect, state))
        return True


class Admission:
    def __init__(self):
        self.calls = []
        self.denied = set()

    def authorize(self, content_id, effect, action, request_digest):
        self.calls.append((effect, action))
        return (effect, action) not in self.denied


class Provider:
    def __init__(self, E):
        self.E = E
        self.visible = {}
        self.calls = []
        self.delay = set()
        self.indeterminate = set()

    def observe(self, effect):
        seen = self.visible.get(effect.identity)
        if seen is None:
            return self.E.Observation("absent")
        return self.E.Observation("matched" if seen == effect.target_digest else "mismatched", seen)

    def dispatch(self, effect):
        self.calls.append(effect.identity)
        if effect.identity not in self.delay:
            self.visible[effect.identity] = effect.target_digest
        return self.E.Dispatch("unknown" if effect.identity in self.indeterminate else "applied")


class Rig:
    def __init__(self, E, effects=None, generation=1):
        self.E = E
        self.manifest = manifest(E)
        self.content_id = self.manifest["contentId"]
        self.effects = E.ordered_effects(self.manifest)
        self.journal = Journal(E, self.content_id, effects, generation)
        self.admission = Admission()
        self.provider = Provider(E)

    def step(self, fence=None, **extra):
        return self.E.full_transition(self.content_id, self.effects, self.journal, self.admission, self.provider,
                                      fence=fence, **extra)

    def refuses(self, text, call):
        try:
            call()
        except self.E.Refused as error:
            assert text in str(error), f"refused for another reason: {error}"
            return
        raise AssertionError(f"expected refusal containing {text!r}")


def _sample(E):
    effect = E.Effect("tag", "t" * 40, "r" * 64)
    return effect, E.JournalState(2, "sha256:" + "a" * 64, {"tag": "intent"})


# -- scenarios -----------------------------------------------------------------


def optin_changes_nothing_and_records_each_own_send(E):
    plain, fenced = Rig(E), Rig(E)
    fence = E.BoundIntentFence("run-1.attempt-1:abc")
    for _ in range(32):
        a = E.advance(plain.manifest, plain.journal, plain.admission, plain.provider)
        b = E.advance(fenced.manifest, fenced.journal, fenced.admission, fenced.provider, fence=fence)
        assert a == b
    assert plain.journal.writes == fenced.journal.writes and len(fenced.journal.writes) == 32
    assert plain.provider.calls == fenced.provider.calls and len(fenced.provider.calls) == 16
    assert plain.admission.calls == fenced.admission.calls
    assert fence.sends == tuple(fenced.provider.calls)
    assert fence.written == tuple((effect.identity, 2 + 2 * index) for index, effect in enumerate(fenced.effects))


def inherited_intent_is_never_sent(E):
    rig = Rig(E, {"tag": "intent"}, generation=2)  # written by an earlier invocation
    fence = E.BoundIntentFence("later-invocation")
    assert rig.step(fence).state == "waiting"
    assert rig.provider.calls == [] and fence.sends == () and fence.written == ()
    rig.provider.visible["tag"] = rig.effects[0].target_digest
    assert rig.step(fence).state == "verified"
    assert rig.provider.calls == [] and fence.sends == ()


def later_invocation_after_crash_between_intent_and_send_never_sends(E):
    rig = Rig(E)
    crashed = E.BoundIntentFence("invocation-a")

    def crash(_effect):
        raise RuntimeError("process lost after the intent write")

    try:
        rig.step(crashed, prepare_dispatch=crash)
    except RuntimeError:
        pass
    assert rig.journal.state.effects == {"tag": "intent"} and rig.provider.calls == []
    later = E.BoundIntentFence("invocation-b")
    assert rig.step(later).state == "waiting"
    assert rig.provider.calls == [] and later.sends == () and later.written == ()
    # The crashed invocation's own fence stays unusable for a send too.
    rig.refuses("no intent written by this invocation", lambda: crashed.guard(rig.provider).dispatch(rig.effects[0]))
    assert rig.provider.calls == []


def own_intent_is_sent_once_even_when_the_fence_is_reused(E):
    rig = Rig(E)
    rig.provider.delay.add("tag")
    rig.provider.indeterminate.add("tag")
    fence = E.BoundIntentFence("one-invocation")
    assert rig.step(fence).state == "unknown"
    assert rig.step(fence).state == "waiting"
    assert rig.provider.calls == ["tag"] and fence.sends == ("tag",) and fence.written == (("tag", 2),)


def guarded_provider_refuses_without_a_release(E):
    rig = Rig(E, {"tag": "intent"}, generation=2)
    fence = E.BoundIntentFence("direct-attempt")
    rig.refuses("no intent written by this invocation", lambda: fence.guard(rig.provider).dispatch(rig.effects[0]))
    assert rig.provider.calls == []


def a_permit_is_single_use(E):
    effect, state = _sample(E)
    journal = types.SimpleNamespace(read=lambda: state)
    provider = Provider(E)
    fence = E.BoundIntentFence("single-use")
    fence.arm(state.content_id, "tag", state)
    fence.release(state.content_id, "tag", journal)
    guarded = fence.guard(provider)
    guarded.dispatch(effect)
    try:
        guarded.dispatch(effect)
    except E.Refused:
        pass
    else:
        raise AssertionError("a spent permit authorized a second send")
    try:
        fence.release(state.content_id, "tag", journal)
    except E.Refused:
        pass
    else:
        raise AssertionError("a spent permit was released twice")
    assert provider.calls == ["tag"]


def a_journal_changed_after_our_write_forbids_the_send(E):
    rig = Rig(E)

    def other_writer(_effect):
        rig.journal.state = E.JournalState(3, rig.content_id, {"tag": "intent", "draft": "intent"})

    rig.refuses("no longer holds the intent", lambda: rig.step(E.BoundIntentFence("raced"), prepare_dispatch=other_writer))
    assert rig.provider.calls == []


def a_cas_that_reports_a_win_without_writing_forbids_the_send(E):
    unfenced = Rig(E)
    unfenced.journal.lie = True
    unfenced.step()
    assert unfenced.provider.calls == ["tag"], "control: without the fence the lie reaches the provider"
    rig = Rig(E)
    rig.journal.lie = True
    rig.refuses("no longer holds the intent", lambda: rig.step(E.BoundIntentFence("liar")))
    assert rig.provider.calls == []


def a_changed_journal_forbids_the_send_whatever_part_changed(E):
    cases = {
        "same generation, other effects": lambda cid: E.JournalState(2, cid, {"tag": "intent", "draft": "intent"}),
        "other generation, same effects": lambda cid: E.JournalState(3, cid, {"tag": "intent"}),
        "other content, same generation and effects": lambda cid: E.JournalState(2, "sha256:" + "e" * 64, {"tag": "intent"}),
    }
    for name, changed in cases.items():
        rig = Rig(E)

        def other_writer(_effect, rig=rig, changed=changed):
            rig.journal.state = changed(rig.content_id)

        rig.refuses("no longer holds the intent", lambda: rig.step(E.BoundIntentFence("raced"), prepare_dispatch=other_writer))
        assert rig.provider.calls == [], name


def the_intent_is_recorded_before_any_preparation_can_run(E):
    rig = Rig(E)
    fence = E.BoundIntentFence("recorded")
    seen = []
    rig.step(fence, prepare_dispatch=lambda effect: seen.append(fence.written))
    assert seen == [(("tag", 2),)]


def the_provider_permit_names_the_released_effect(E):
    effect, state = _sample(E)
    journal = types.SimpleNamespace(read=lambda: state)
    provider = Provider(E)
    fence = E.BoundIntentFence("named")
    fence.arm(state.content_id, "tag", state)
    fence.release(state.content_id, "tag", journal)
    try:
        fence.guard(provider).dispatch(E.Effect("draft", "t", "r"))
    except E.Refused:
        pass
    else:
        raise AssertionError("a release for one effect authorized another")
    assert provider.calls == []


def a_refused_release_stops_the_fence_arming_again(E):
    _, state = _sample(E)
    fence = E.BoundIntentFence("poisoned")
    fence.arm(state.content_id, "tag", state)
    try:
        fence.release(state.content_id, "tag", types.SimpleNamespace(read=lambda: E.JournalState(9, state.content_id, {})))
    except E.Refused:
        pass
    else:
        raise AssertionError("release accepted a changed journal")
    try:
        fence.arm(state.content_id, "tag", E.JournalState(2, state.content_id, {"tag": "intent"}))
    except E.Refused as error:
        assert "never dispatched" in str(error)
    else:
        raise AssertionError("a fence armed again after a refused release")


def an_unreadable_journal_before_the_send_forbids_it(E):
    rig = Rig(E)
    reads = []

    def read_hook():
        reads.append(1)
        if len(reads) == 2:  # the transition's read, then the pre-send read
            raise OSError("journal unavailable")

    rig.journal.read_hook = read_hook
    rig.refuses("journal unreadable before send", lambda: rig.step(E.BoundIntentFence("blind")))
    assert rig.provider.calls == []


def a_denied_dispatch_admission_leaves_no_send(E):
    rig = Rig(E)
    rig.admission.denied.add(("tag", "dispatch"))
    fence = E.BoundIntentFence("denied")
    rig.refuses("dispatch admission denied", lambda: rig.step(fence))
    assert rig.step(fence).state == "waiting"
    assert rig.provider.calls == [] and fence.sends == ()


def only_one_unsent_intent_may_be_outstanding(E):
    effect, state = _sample(E)
    other = E.JournalState(4, state.content_id, {"tag": "verified", "draft": "intent"})
    fence = E.BoundIntentFence("two")
    fence.arm(state.content_id, "tag", state)
    try:
        fence.arm(state.content_id, "draft", other)
    except E.Refused as error:
        assert "never dispatched" in str(error)
    else:
        raise AssertionError("a second intent was armed while the first was unsent")


def a_permit_is_bound_to_its_content_and_effect(E):
    _, state = _sample(E)
    journal = types.SimpleNamespace(read=lambda: state)
    for content_id, effect in (("sha256:" + "b" * 64, "tag"), (state.content_id, "draft")):
        fence = E.BoundIntentFence("bound")
        fence.arm(state.content_id, "tag", state)
        try:
            fence.release(content_id, effect, journal)
        except E.Refused as error:
            assert "no intent written by this invocation" in str(error)
        else:
            raise AssertionError("a permit authorized a different content or effect")


def an_armed_state_must_carry_the_intent(E):
    _, state = _sample(E)
    try:
        E.BoundIntentFence("bound").arm(state.content_id, "draft", state)
    except E.Refused as error:
        assert "carries no intent" in str(error)
    else:
        raise AssertionError("armed a state without the intent")


def the_invocation_identifier_is_closed(E):
    for bad in ("", " ", "a b", "x" * 129, "-leading", 5, None, b"bytes", "line\nbreak"):
        try:
            E.BoundIntentFence(bad)
        except E.Refused:
            continue
        raise AssertionError(f"accepted invocation identifier {bad!r}")
    assert E.BoundIntentFence("x" * 128).invocation == "x" * 128


def an_intent_is_not_written_twice_by_one_invocation(E):
    _, state = _sample(E)
    journal = types.SimpleNamespace(read=lambda: state)
    fence = E.BoundIntentFence("twice")
    fence.arm(state.content_id, "tag", state)
    fence.release(state.content_id, "tag", journal)
    fence.guard(Provider(E)).dispatch(E.Effect("tag", "t", "r"))
    try:
        fence.arm(state.content_id, "tag", state)
    except E.Refused as error:
        assert "already wrote an intent" in str(error)
    else:
        raise AssertionError("one invocation armed the same effect twice")


SCENARIOS = (
    optin_changes_nothing_and_records_each_own_send,
    inherited_intent_is_never_sent,
    later_invocation_after_crash_between_intent_and_send_never_sends,
    own_intent_is_sent_once_even_when_the_fence_is_reused,
    guarded_provider_refuses_without_a_release,
    a_permit_is_single_use,
    a_journal_changed_after_our_write_forbids_the_send,
    a_cas_that_reports_a_win_without_writing_forbids_the_send,
    a_changed_journal_forbids_the_send_whatever_part_changed,
    the_intent_is_recorded_before_any_preparation_can_run,
    the_provider_permit_names_the_released_effect,
    a_refused_release_stops_the_fence_arming_again,
    an_unreadable_journal_before_the_send_forbids_it,
    a_denied_dispatch_admission_leaves_no_send,
    only_one_unsent_intent_may_be_outstanding,
    a_permit_is_bound_to_its_content_and_effect,
    an_armed_state_must_carry_the_intent,
    the_invocation_identifier_is_closed,
    an_intent_is_not_written_twice_by_one_invocation,
)


def regression_defence_fenced(E):
    """The fence refuses a regressed engine that re-dispatches an inherited intent."""
    rig = Rig(E, {"tag": "intent"}, generation=2)
    try:
        rig.step(E.BoundIntentFence("defended"))
    except E.Refused:
        pass
    assert rig.provider.calls == []


def regression_unfenced_calls(E):
    rig = Rig(E, {"tag": "intent"}, generation=2)
    try:
        rig.step()
    except E.Refused:
        pass
    return rig.provider.calls


# -- mutants --------------------------------------------------------------------
# name -> (replacements, scenarios that must fail on the mutant)

_REDISPATCH = ('                return Transition("waiting", PendingObservation(effect, current))',
               '                provider.dispatch(effect)\n                return Transition("waiting", PendingObservation(effect, current))')
_GUARD_BYPASS = ("        return _GuardedProvider(provider, self)", "        return provider")

MUTANTS = {
    "engine never releases the permit": (
        [("        if fence is not None:\n            fence.release(content_id, effect.identity, journal)\n", "")],
        {"optin_changes_nothing_and_records_each_own_send"}),
    "engine never arms the fence": (
        [("        if fence is not None:\n            fence.arm(content_id, effect.identity, written)\n", "")],
        {"optin_changes_nothing_and_records_each_own_send"}),
    "release skips the journal re-read comparison": (
        [("        if current != armed[2]:", "        if False:")],
        {"a_journal_changed_after_our_write_forbids_the_send", "a_cas_that_reports_a_win_without_writing_forbids_the_send"}),
    "release compares only the generation": (
        [("        if current != armed[2]:", "        if current.generation != armed[2].generation:")],
        {"a_changed_journal_forbids_the_send_whatever_part_changed"}),
    "release compares only the effects": (
        [("        if current != armed[2]:", "        if current.effects != armed[2].effects:")],
        {"a_changed_journal_forbids_the_send_whatever_part_changed"}),
    "release ignores the journal content id": (
        [("        if current != armed[2]:", "        if (current.generation, current.effects) != (armed[2].generation, armed[2].effects):")],
        {"a_changed_journal_forbids_the_send_whatever_part_changed"}),
    "guard accepts any released effect": (
        [("        if released != effect:\n            raise Refused(f\"{effect}: send fence {self.invocation}: no intent written by this invocation permits a send\")\n\n\nclass _GuardedProvider",
          "        if released is None:\n            raise Refused(f\"{effect}: send fence {self.invocation}: no intent written by this invocation permits a send\")\n\n\nclass _GuardedProvider")],
        {"the_provider_permit_names_the_released_effect"}),
    "engine arms the fence only after preparation": (
        [("        if fence is not None:\n            fence.arm(content_id, effect.identity, written)\n        if prepare_dispatch is not None:\n            prepare_dispatch(effect)\n",
          "        if prepare_dispatch is not None:\n            prepare_dispatch(effect)\n        if fence is not None:\n            fence.arm(content_id, effect.identity, written)\n")],
        {"the_intent_is_recorded_before_any_preparation_can_run"}),
    "a refused release leaves the fence armable": (
        [("        if self._armed is not None or self._released is not None or self._refused:", "        if self._armed is not None or self._released is not None:")],
        {"a_refused_release_stops_the_fence_arming_again"}),
    "release tolerates an unreadable journal": (
        [("        except Exception as error:\n            self._refused = True\n            raise Refused(f\"{effect}: send fence {self.invocation}: journal unreadable before send: {error}\") from error",
          "        except Exception:\n            current = armed[2]")],
        {"an_unreadable_journal_before_the_send_forbids_it"}),
    "guard returns the bare provider": (
        [_GUARD_BYPASS], {"guarded_provider_refuses_without_a_release", "later_invocation_after_crash_between_intent_and_send_never_sends"}),
    "guard does not check the release": (
        [("        if released != effect:\n            raise Refused(f\"{effect}: send fence {self.invocation}: no intent written by this invocation permits a send\")\n\n\nclass _GuardedProvider",
          "        if False:\n            raise Refused(f\"{effect}\")\n\n\nclass _GuardedProvider")],
        {"guarded_provider_refuses_without_a_release", "a_permit_is_single_use"}),
    "guard keeps the release after a send": (
        [("        released, self._released = self._released, None\n", "        released = self._released\n")],
        {"a_permit_is_single_use"}),
    "release keeps the arm after use": (
        [("        armed, self._armed = self._armed, None  # spent on any outcome: never retried", "        armed = self._armed")],
        {"a_permit_is_single_use"}),
    "release ignores the content id": (
        [("armed is None or armed[0] != content_id or armed[1] != effect", "armed is None or armed[1] != effect")],
        {"a_permit_is_bound_to_its_content_and_effect"}),
    "release ignores the effect": (
        [("armed is None or armed[0] != content_id or armed[1] != effect", "armed is None or armed[0] != content_id")],
        {"a_permit_is_bound_to_its_content_and_effect"}),
    "arm allows an outstanding intent": (
        [("        if self._armed is not None or self._released is not None or self._refused:", "        if self._refused:")],
        {"only_one_unsent_intent_may_be_outstanding"}),
    "arm allows a repeated effect": (
        [("        if effect in self._sent or any(name == effect for name, _ in self._written):", "        if False:")],
        {"an_intent_is_not_written_twice_by_one_invocation"}),
    "arm accepts a state without the intent": (
        [("        if written.effects.get(effect) != \"intent\":", "        if False:")],
        {"an_armed_state_must_carry_the_intent"}),
    "invocation identifier unbounded": (
        [("{0,127}\")", "{0,1270}\")")], {"the_invocation_identifier_is_closed"}),
    "invocation identifier unchecked": (
        [("        if type(invocation) is not str or not _INVOCATION.fullmatch(invocation):", "        if False:")],
        {"the_invocation_identifier_is_closed"}),
    "bound generation differs from the written generation": (
        [("        written = JournalState(current.generation + 1, content_id, {**current.effects, effect.identity: \"intent\"})",
          "        written = JournalState(current.generation, content_id, {**current.effects, effect.identity: \"intent\"})")],
        {"optin_changes_nothing_and_records_each_own_send"}),
}


def mutate(source: str, replacements) -> str:
    for old, new in replacements:
        assert source.count(old) == 1, f"mutation anchor is not unique: {old[:60]!r}"
        source = source.replace(old, new)
    return source
