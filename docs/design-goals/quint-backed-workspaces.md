---
title: Quint-backed workspaces
category: Design
categoryindex: 4
index: 34
description: Why framework and generated product workspaces share one Quint-backed base.
---

# Quint-backed workspaces

Every future FS.GG workspace—framework repository or generated product—is intended
to start from the same Quint-backed specification, GitHub, CI, provenance,
coordination, and evidence substrate. Product source may be F#, TypeScript,
JavaScript, Rust, Go, or another language; the workspace model is independent of
that choice.

Quint need not be the user's primary interface. A user may file plain-language
issues, use structured SDD documents, or edit literate Quint directly. Those are
authoring-depth choices over one model, not different workspace architectures.
Agents may maintain the formal representation while humans review prose and
readable semantic projections.

This common base matters most around GitHub and CI: repository profile, required
checks, external contracts, evidence obligations, and issue-change semantics
should not become weaker merely because a workspace is a consumer product rather
than an FS.GG framework.

Tracking authority: [single-lifecycle design issue](https://github.com/FS-GG/FS.GG.SDD/issues/927).
