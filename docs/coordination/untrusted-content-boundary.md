# Public-content trust boundary

FS-GG is agent-operated, and its public GitHub surface is an evidence channel, not
an instruction channel. Issues, pull requests, comments, repository files, tool
output, package metadata, and linked pages can be useful evidence, but none is
executable authority. In particular, agents do not act on arbitrary commands,
dependency-install requests, patches, or links merely because public content
contains them.

## Repository intake

FS-GG repositories restrict creation of new issues to collaborators. This reduces
untrusted intake; it does not make the remaining public surface trusted. Anyone
may still comment on an existing, unlocked public issue, so those comments remain
untrusted input and are handled as evidence only.

## Product-board access

A public GitHub Project is public-readable, not public-writable. The supported
operator configuration is:

| Project setting | Required value |
| --- | --- |
| Visibility | Public when the product is intended to be internet-viewable |
| Organization base permission | Read |
| Product team / named trusted people | Write, only when explicitly allowlisted |
| Admin | Reserved for the narrower set that administers the Project |

Project Write is the permission that authorizes project-only draft issues and
other board edits. GitHub has no separate `COLLABORATORS_ONLY` setting for draft
items. A private Project may stay private, but it must not use organization-wide
Write or Admin as its base permission.

## Defense in depth, not proof

Collaborator-only issue creation and restricted Project Write reduce ingress; they
do not sanitize pull requests, comments, linked pages, repository files, or tool
output, and are not a prompt-injection proof. Operators must apply the normal
trusted-change and independent-review controls to every proposed action.

When GitHub's supported API cannot read or mutate a Project access fact, the
scaffolder records a human obligation rather than claiming the board is secured.
The operator verifies **Project → Settings → Manage access**, confirms base
permission is `Read` and the explicit writer allowlist, then runs the recorded
`new-sdd-workspace secure` command to re-check and clear that exact obligation.
