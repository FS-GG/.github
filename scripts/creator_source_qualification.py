"""Closed Creator023 normal-source qualification; deliberately grants no release effects.

The normal Creator023 source composition follows the repository coherent-set scalar. Retained Creator020 source qualification
and its separate genuine108 published-dependency mode keep their identities. Creator021,
Creator022 and Creator023 are normal-source only; no source binding is an installed/publication receipt.
"""
from dataclasses import dataclass
from pathlib import Path
import re
from xml.etree import ElementTree


@dataclass(frozen=True)
class SourceQualificationBinding:
    package: str
    version: str
    tag: str

SOURCE_020 = SourceQualificationBinding("FS.GG.NewSddWorkspace", "0.20.0", "new-sdd-workspace/v0.20.0")
SOURCE_021 = SourceQualificationBinding("FS.GG.NewSddWorkspace", "0.21.0", "new-sdd-workspace/v0.21.0")
SOURCE_022 = SourceQualificationBinding("FS.GG.NewSddWorkspace", "0.22.0", "new-sdd-workspace/v0.22.0")
SOURCE_023 = SourceQualificationBinding("FS.GG.NewSddWorkspace", "0.23.0", "new-sdd-workspace/v0.23.0")
SOURCE_020_COHERENT_VERSION = "0.110.0"
def source_coherent_version(props):
    """Read one unconditional stable source scalar; never infer a published dependency."""
    try:
        with Path(props).open("rb") as stream:
            raw = stream.read(1048577)
        if len(raw) > 1048576:
            raise ValueError("source coherent version input exceeds its bound")
        root = ElementTree.fromstring(raw)
        nodes = list(root.iter("FsggCoherentSetVersion"))
    except (OSError, ElementTree.ParseError) as error:
        raise ValueError("source coherent version is unavailable") from error
    if len(nodes) != 1 or nodes[0].attrib:
        raise ValueError("source coherent version requires one unconditional scalar")
    supported = [node for group in root if group.tag == "PropertyGroup" and not group.attrib
                 for node in group if node.tag == "FsggCoherentSetVersion"]
    if root.tag != "Project" or root.attrib or len(supported) != 1 or supported[0] is not nodes[0]:
        raise ValueError("source coherent version requires unconditional Project/PropertyGroup ancestry")
    value = (nodes[0].text or "").strip()
    if re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", value) is None:
        raise ValueError("source coherent version must be a literal stable version")
    return value


SOURCE_COHERENT_VERSION = source_coherent_version(Path(__file__).resolve().parents[1] / "Directory.Build.props")


def qualification_version(binding):
    """Retained published-dependency reader; SOURCE_021/SOURCE_022/SOURCE_023 have no admission here."""
    if binding is None:
        return "0.19.0"  # Retain the consumed019 reader without broadening its authority.
    if binding is not SOURCE_020:
        raise ValueError("unknown Creator source qualification binding")
    return SOURCE_020.version
