"""Acquire terminal logs and available result bytes under the caller's original grant.

The existing caller owns run/job identity, admission, transport and custody. This
composition never grants time, refreshes a deadline or interprets effect success.
"""
import datetime
import hashlib
import io
import stat
import zipfile


def terminal_timing(run, jobs, intent, now=None):
    """Use actual job completion or, for a never-started run, run.updated_at."""
    now = now or datetime.datetime.now(datetime.timezone.utc)
    def instant(value):
        assert isinstance(value, str) and value, "Original native timing unavailable"
        value = datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
        assert value.tzinfo is not None, "Original native timing has no timezone"
        return value.astimezone(datetime.timezone.utc)
    post, created, end = map(instant, (intent["postUtc"], run["created_at"], intent["deadlineUtc"]))
    assert (intent["queueSeconds"], intent["activeJobsSeconds"], intent["retentionSeconds"], intent["aggregateSeconds"]) == (600, 3600, 300, 4500) and (end-post).total_seconds() == 4500, "Original timing bounds differ"
    assert run["status"] == "completed" and isinstance(jobs, list) and len(jobs) <= 1, "Original terminal population differs"
    updated = instant(run["updated_at"])
    assert created >= post-datetime.timedelta(seconds=2) and post <= now and created <= updated <= now <= end, "Original terminal chronology differs"
    start = completion = None
    if jobs:
        assert jobs[0]["status"] == "completed", "Original terminal job is not completed"
        if jobs[0].get("started_at"):
            start = instant(jobs[0]["started_at"])
            completion = instant(jobs[0]["completed_at"])
            assert max(created, post) <= start <= completion <= updated, "Original job chronology differs"
        elif jobs[0].get("completed_at"):
            completion = instant(jobs[0]["completed_at"])
            assert created <= completion <= updated, "Original unstarted job chronology differs"
    terminal = completion if completion is not None else updated
    queue = ((start if start is not None else terminal)-post).total_seconds()
    active = (completion-start).total_seconds() if start is not None else 0
    assert 0 <= queue <= 600 and 0 <= active <= 3600, "Original queue or active bound exceeded"
    retention = min(end, terminal+datetime.timedelta(seconds=300))
    assert now <= retention, "Original terminal retention expired"
    return {"postUtc": post.isoformat(), "createdUtc": created.isoformat(),
            "queueSeconds": queue, "activeSeconds": active,
            "jobStartedUtc": start.isoformat() if start is not None else None,
            "jobCompletedUtc": completion.isoformat() if completion is not None else None,
            "terminalTimestampSource": "job.completed_at" if completion is not None else "run.updated_at",
            "originalDeadlineUtc": end.isoformat(), "retentionDeadlineUtc": retention.isoformat()}


def collect_terminal(reader, run, jobs, terminal, payload, decode_result, current,
                     *, clock=None, member_names=("release-successor-result.json",),
                     archive_limit=4194304, member_limit=16384):
    partial = {"runId": run.get("id"), "conclusion": run.get("conclusion"),
               "jobLogSHA256": None, "archiveDownloaded": False, "nativeResult": None,
               "evidenceComplete": False, "gaps": [], "localProviderWrites": 0}
    try:
        return _collect(reader, run, jobs, terminal, payload, decode_result, current,
                        clock=clock, member_names=member_names, archive_limit=archive_limit,
                        member_limit=member_limit, partial=partial)
    except BaseException as primary:
        # Existing caller reports the original exception and this partial subset;
        # a failing reporter must not replace either with a success assertion.
        partial["acquisitionFailureType"] = type(primary).__name__
        primary.terminalEvidence = partial
        raise


def _collect(reader, run, jobs, terminal, payload, decode_result, current,
             *, clock, member_names, archive_limit, member_limit, partial):
    clock = clock or (lambda: datetime.datetime.now(datetime.timezone.utc))
    def admitted():
        current()
        deadline = datetime.datetime.fromisoformat(terminal["timing"]["retentionDeadlineUtc"].replace("Z", "+00:00"))
        assert deadline.tzinfo is not None and clock() <= deadline, "Original terminal readback grant expired"
    admitted()
    expected_run = terminal["run"]
    assert all(run.get(k) == expected_run.get(k) for k in ("id", "workflow_id", "head_sha", "run_attempt", "event", "head_branch", "path", "actor", "repository", "status", "conclusion")) and run["status"] == "completed", "Original terminal run differs"
    expected_jobs = terminal["jobs"]
    assert len(jobs) == len(expected_jobs) <= 1 and all(job["status"] == "completed" and all(job.get(k) == expected_jobs[index].get(k) for k in ("id", "run_id", "run_attempt", "head_sha", "name", "status", "conclusion", "started_at", "completed_at")) for index, job in enumerate(jobs)), "Original terminal job differs"
    assert run["conclusion"] in {"success", "failure", "cancelled", "timed_out", "action_required", "neutral", "skipped", "stale"}, "Original terminal conclusion differs"
    value = partial
    first = None
    log_expected = bool(jobs and jobs[0].get("started_at"))
    if log_expected:
        try:
            log, _, _ = payload("job-log", "actions/jobs/" + str(jobs[0]["id"]) + "/logs", 2097152)
            value["jobLogSHA256"] = hashlib.sha256(log).hexdigest()
            admitted()
        except BaseException as error:
            first = error
            value["gaps"].append("terminal job log unavailable or incomplete")
    else:
        value["gaps"].append("original terminal run has no started job; execution log unavailable")
    # Independent result acquisition still proceeds after a log transport failure,
    # only while the same original grant and caller request budget permit it.
    try:
        admitted()
    except BaseException as error:
        if first is None:
            raise
        first.add_note("additional terminal grant failure: " + type(error).__name__)
        raise first
    expected = terminal.get("resultArtifact")
    if expected is None:
        value["gaps"].append("selected result artifact unavailable; log-only terminal evidence")
        if first is not None:
            raise first
        return value
    try:
        assert expected["name"] == "release-successor-result" and type(expected["id"]) is int and expected["id"] > 0, "Selected result artifact differs"
        assert type(expected["size_in_bytes"]) is int and 0 < expected["size_in_bytes"] <= archive_limit, "Selected result archive byte bound"
        artifact = reader.required("artifact", "actions/artifacts/" + str(expected["id"]))
        admitted()
        assert all(artifact.get(k) == expected.get(k) for k in ("id", "name", "size_in_bytes", "digest", "expires_at", "workflow_run")) and artifact["expired"] is False, "Original terminal artifact tuple changed"
        assert artifact["workflow_run"]["id"] == run["id"] and artifact["workflow_run"]["head_sha"] == run["head_sha"], "Original result artifact source differs"
        expires = datetime.datetime.fromisoformat(artifact["expires_at"].replace("Z", "+00:00"))
        assert expires.tzinfo is not None and clock() < expires, "Original result artifact expired"
        body, _, _ = payload("result-archive", "actions/artifacts/" + str(artifact["id"]) + "/zip", artifact["size_in_bytes"])
        admitted()
        digest = hashlib.sha256(body).hexdigest()
        value.update(artifactId=artifact["id"], archiveDownloaded=True, archiveBytes=len(body),
                     archiveSHA256=digest, archiveAuthenticated=False)
        assert len(body) == artifact["size_in_bytes"] and "sha256:" + digest == artifact["digest"], "Original result archive digest differs"
        value["archiveAuthenticated"] = True
        with zipfile.ZipFile(io.BytesIO(body)) as archive:
            entries = archive.infolist()
            assert len(entries) == len(member_names) <= 3 and {e.filename for e in entries} == set(member_names) and len(set(member_names)) == len(member_names), "Original result member set differs"
            assert all(0 < e.file_size <= member_limit and not e.flag_bits & 1 and not e.is_dir()
                       and stat.S_IFMT(e.external_attr >> 16) in (0, stat.S_IFREG) for e in entries), "Original result member unsafe or oversized"
            members = {e.filename: archive.read(e) for e in entries}
        admitted()
        value["memberSHA256"] = {n: hashlib.sha256(b).hexdigest() for n, b in members.items()}
        result = decode_result(members)
        admitted()
        value.update(artifactId=artifact["id"], archiveDownloaded=True, archiveBytes=len(body),
                     archiveSHA256=digest, memberSHA256={n: hashlib.sha256(b).hexdigest() for n, b in members.items()},
                     nativeResult=result, evidenceComplete=first is None and log_expected)
    except BaseException as error:
        value["gaps"].append("terminal result artifact unavailable or incomplete")
        if first is None:
            raise
        first.add_note("additional terminal artifact acquisition failure: " + type(error).__name__)
    if first is not None:
        raise first
    return value
