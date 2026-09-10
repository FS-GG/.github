# Standalone local dashboard qualification

`run.sh` measures the installed CLI against an already activated, assessor-qualified local association. It requires 20 cold dashboard starts, repeated Linux RSS samples after quiescence, and 100 caller-observed durable submissions with payloads between 60 and 64 KiB. The package and exact source SHA must match the coherent release manifest.

The JSON result separates filesystem/process evidence from physical power-loss, Main installation, and public-release facts. Run it on the qualified release host with a separately recorded filesystem evidence JSON object; current container overlay results cannot qualify the local-store profile.
