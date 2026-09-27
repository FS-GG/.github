# Skill registry count projection (source pilot)

This pure F# reducer renders only the dynamic count/table body of the
`skill-registry-counts` region from typed `id`, `scope`, and `owner` rows. The
test adapter parses the exact checked-in `registry/skills.yml` bytes and checks
the reducer output against the current generated region. It refuses duplicate
region markers and symlinked test sources/targets, and tests malformed and
duplicate rows independently. The live producer repair is draft #3709; these
test-adapter controls do not replace that producer gate.

The Bash `generate-projections` producer remains live. YAML parsing, the fixed
region preamble/markers, source catalog completeness, installation, producer
publication, and receiver pinning are outside this source slice. No generated
document or receiver is written by the reducer.
