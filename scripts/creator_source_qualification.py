"""Closed Creator021 normal-source qualification; deliberately grants no release effects.

The normal Creator021 source composition is coherent111. Retained Creator020 source qualification
and its separate genuine108 published-dependency mode keep their identities. Creator021
is normal-source only; neither source binding is an installed/publication receipt.
"""
from dataclasses import dataclass


@dataclass(frozen=True)
class SourceQualificationBinding:
    package: str
    version: str
    tag: str

SOURCE_020 = SourceQualificationBinding("FS.GG.NewSddWorkspace", "0.20.0", "new-sdd-workspace/v0.20.0")
SOURCE_021 = SourceQualificationBinding("FS.GG.NewSddWorkspace", "0.21.0", "new-sdd-workspace/v0.21.0")
SOURCE_020_COHERENT_VERSION = "0.110.0"
SOURCE_COHERENT_VERSION = "0.111.0"


def qualification_version(binding):
    """Retained published-dependency reader; SOURCE_021 has no admission here."""
    if binding is None:
        return "0.19.0"  # Retain the consumed019 reader without broadening its authority.
    if binding is not SOURCE_020:
        raise ValueError("unknown Creator source qualification binding")
    return SOURCE_020.version
