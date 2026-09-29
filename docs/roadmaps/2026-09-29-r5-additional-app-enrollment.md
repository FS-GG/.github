# Additional apps in the R5 completed-item cohort

The user selected four independent apps on 2026-09-29. The existing schema-2 cohort prospectively enrolls their lanes and selects their whole originals at **2026-09-29T06:31:33Z**, before planning or implementation outcomes. The first ten distinct completed originals across all enrolled lanes still set the repair cutoff. BAR, SC2, LEARN and FOURD remain enrolled and continue independently.

| Lane / original | Whole acceptance | Owning source |
| --- | --- | --- |
| TODO-01 / TODO-01.1 | Add, edit, complete/reopen, filter and delete tasks; retain valid local state across reload; accessible pointer/keyboard controls and honest storage-failure behavior. | Templates `examples/enrollment-apps/todo/` |
| TTT-01 / TTT-01.1 | Complete local two-player play; alternate turns; detect every win and draw; refuse occupied/invalid/post-terminal moves; restart through accessible controls. | Templates `examples/enrollment-apps/tic-tac-toe/` |
| SNAKE-01 / SNAKE-01.1 | Keyboard direction, food/growth/score, bounded collision/terminal rules, pause and restart; deterministic reducer and actual timer/control qualification. | Templates `examples/enrollment-apps/snake/` |
| HELLO-01 / HELLO-01.1 | Serve the real app entry point and render an accessible “Hello, world!” greeting in an actual browser. | Templates `examples/enrollment-apps/hello-world/` |

Each app has one whole original across its planning, implementation, tests, repairs and delivery. Each requires meaningful owning acceptance, actual browser qualification, native source delivery and protected-main readback before counting. Existing console greetings or other already completed examples are excluded. Planning, scaffolding alone, shared harness changes, actions, test jobs and this enrollment update do not increment the count. Native usage remains missing; enrollment or ten completions cannot certify economics.

Templates protected baseline is `408bc596ad435908ff730b464096bf700ed71b3e`. The four apps are explicit browser-native ESM samples outside packaged `templates/**`, using a root-owned shared serve/Playwright harness. Each app worker owns only its disjoint subtree. The parent owns shared configuration, integration and PR admission. Fresh Astra-high plans are retained at `/tmp/r5-todo-app-plan-20260929.md`, `/tmp/r5-tic-tac-toe-plan-20260929.md`, `/tmp/r5-snake-app-plan-20260929.md` and `/tmp/r5-hello-world-app-plan-20260929.md`; durable owning roadmaps land with their implementation, rather than a planning-only PR.

Under [Unified §9.9](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#99-when-new-workspaces-change), these source samples have no generated-workspace effect. A clean source checkout and real browser prove sample reproducibility. They do not prove installed scaffold adoption or a retained generated-workspace upgrade. Provider contents, lifecycle defaults and package publication remain separate, unselected boundaries.

[Unified §0 and §9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md) project progress. The [existing cohort input](../reports/evidence/2026-09-07-routine-route-cohort.json) owns prospective selection and count evidence; each app's delivered owning roadmap supplies completion authority. R5's 95% usage requirement, comparison margins and stop rules, and Q10's separate fifteen-item gate remain unchanged.
