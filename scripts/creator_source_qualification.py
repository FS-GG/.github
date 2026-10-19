"""Closed Creator020 source qualification; deliberately grants no release effects.

The normal source composition is coherent109. Its independent published-dependency
qualification reuses genuine coherent108; neither is an installed/publication receipt.
"""
from dataclasses import dataclass


@dataclass(frozen=True)
class SourceQualificationBinding:
    package: str
    version: str
    tag: str

SOURCE_020 = SourceQualificationBinding("FS.GG.NewSddWorkspace", "0.20.0", "new-sdd-workspace/v0.20.0")
SOURCE_COHERENT_VERSION = "0.109.0"


def qualification_version(binding):
    if binding is None:
        return "0.19.0"  # Retain the consumed019 reader without broadening its authority.
    if binding is not SOURCE_020:
        raise ValueError("unknown Creator source qualification binding")
    return SOURCE_020.version
