# Projection source and marker controls

`run.sh` copies the producer's targets and registry sources into a temporary
tree. It calls the actual `generate-projections --check` and write modes with a
built coordination engine supplied through `FSGG_COORD_ENGINE_BIN`. Its Python
environment needs PyYAML, as does the live producer.

The fixture checks the unchanged projection, duplicate and reversed markers,
write-mode non-mutation on a duplicate region, empty/duplicate/malformed skill
rows, Markdown owner injection, duplicate YAML keys, order-independent counts,
and source/target symlink refusal. It changes no checked-in generated document.
