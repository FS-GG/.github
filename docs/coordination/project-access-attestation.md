# Coordination Project access attestation

GitHub's ProjectV2 API exposes project visibility but cannot reveal the
organization base permission or the effective writer set. This record is the
durable, dated human observation for the Coordination board; it is not API
proof and it must never be treated as an automated access-control pass.

## Current record

No human observation has been recorded yet. An operator must verify **Project
→ Settings → Manage access** for `FS-GG/Coordination`, then replace this
paragraph with exactly one marker in the following form:

```text
< !-- fsgg:project-access-attestation v1
project=FS-GG/Coordination
verified=YYYY-MM-DD
expires=YYYY-MM-DD
base-permission=Read
writers=EHotwagner,nuklearwanze
verifier=GitHub-login
-- >
```

Use the effective writer list displayed in the UI, in the same comma-separated
order as `scripts/projects-audit.sh --trusted-writers`; do not replace it with
an assumed list. The board's expected access is base permission `Read` with
only `EHotwagner,nuklearwanze` as writers.

## Recurrence and expiry

Re-attest at least every 30 days, or immediately after any access change. Set
`expires` to no more than 30 calendar days after `verified`. The audit reports
missing, malformed, mismatched, and expired records as `unknown` or `stale`;
it continues to report the access boundary as a `noverdict` because only a
human can inspect this setting. This keeps a missing or old observation
visible without falsely turning it into a pass.
