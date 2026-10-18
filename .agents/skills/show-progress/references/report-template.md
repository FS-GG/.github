# Compact progress report

Use the same short overview in chat and in the saved report. Aim for 300–500 words.
No mandatory per-item appendix, scoring system or estimation worksheet.

```markdown
# Development progress — [date]

As of [time and timezone]. Compared with [previous report/date, or first snapshot].

[Two or three sentences: overall health, meaningful movement, biggest constraint.]

| Lane | Meaningful progress | Problem / next step |
|---|---|---|
| [plain-language name] | [what improved or is usable] | [main obstacle and next useful result] |

## Process problems we can fix

- [Observed problem → delivery impact → practical improvement and owner/decision.]

## Blockers the current process cannot resolve

- [Missing input/capability → affected work → supplying owner or required scope change.]

## Priorities

1. [Most useful next action or decision; at most three.]

Sources: [a few direct links; material evidence limits stated briefly].
```

## Saved-report delivery

An explicit `$show-progress` retains authorization to save one timestamped report under
`docs/reports/progressReports/` in the repository, commit it, open a focused PR and merge
when required checks pass. Use `YYYY-MM-DD-HHMMSSZ-unified-v3-progress.md` for the default
programme report; preserve older reports and use a suffix on collision. Keep an existing
report's cutoff fixed. Required delivery CI is allowed; reporting-only product runs are not.

Use the repository's normal delivery route without touching unrelated work. Conversation
restrictions take precedence: do not create a checkout, delegate or publish where prohibited.
If delivery is unavailable, provide the overview and state the actual saved/PR state briefly.
Do not claim a merge or retry indefinitely. A request for a draft, local save or chat answer
limits delivery accordingly. Finish with the useful overview and a link to the saved report;
never substitute a delivery receipt for the answer.
