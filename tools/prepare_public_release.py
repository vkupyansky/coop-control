#!/usr/bin/env python3
"""Prepare local public archives under outputs/public (Python 3.11+).

The required client SHA-256 must come from the DLL checked by
EngineCompositionCheck --public-release. A digest is an attestation of those
exact bytes; this packer does not replace the managed assembly check.
"""

from __future__ import annotations

import argparse
import codecs
import hashlib
import json
import os
import re
import shutil
import stat
import tempfile
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import IO

DOC_NAMES = (
    "README.md",
    "README.ru.md",
    "ENGINE_CHANGES.md",
    "ENGINE_CHANGES.ru.md",
    "LICENSE",
    "THIRD_PARTY.txt",
)
SCREENSHOT_FILES = (
    "screenshots/01-shared-play.png",
    "screenshots/02-main-menu.jpg",
    "screenshots/03-transport.jpg",
    "screenshots/04-teleports.jpg",
    "screenshots/05-character.jpg",
    "screenshots/06-weapons.jpg",
    "screenshots/07-yacht.jpg",
    "screenshots/08-cayo-perico.jpg",
    "screenshots/09-map.jpg",
    "screenshots/10-casino.jpg",
)
PROJECT = Path("work/RageCoopAdmin")
PUBLIC_BUILD = Path("outputs/public-build")
PUBLIC_CLIENT = PUBLIC_BUILD / "Client/Release/net48/RageCoopAdmin.Client.dll"
BASELINE_DIRECTORY = Path("outputs/public-baseline")
BASELINE_PINNED_HASHES = {
    "client/scripts/RageCoop/RageCoop.Client.dll": (
        "0fde6879b5cf6dd5eeb2b91684aca67215bec39c21baa37f1ec0d653163d517a",
        "7fc650fa4142be5c1fb0c433025b4ba313ba1f191802cb5481ace0aafc78cbc6",
    ),
    "client/scripts/RageCoop/RageCoop.Core.dll": (
        "59bcc3a4d6990182a7b7c8ff4d122a256704706a7826ab1d62a75ee753aee256",
        "59bcc3a4d6990182a7b7c8ff4d122a256704706a7826ab1d62a75ee753aee256",
    ),
    "client/scripts/RageCoop/Lidgren.Network.dll": (
        "0cf348189180207818848fe669b60b03d9176596fa5b4bb02533ffcb04e599c3",
        "0cf348189180207818848fe669b60b03d9176596fa5b4bb02533ffcb04e599c3",
    ),
    "server/RageCoop.Server.dll": (
        "cfafbe010ce5003ef67e957c05e870dd1b0ab3268339b1fa140abe72d126e711",
        "cfafbe010ce5003ef67e957c05e870dd1b0ab3268339b1fa140abe72d126e711",
    ),
    "server/RageCoop.Core.dll": (
        "59bcc3a4d6990182a7b7c8ff4d122a256704706a7826ab1d62a75ee753aee256",
        "59bcc3a4d6990182a7b7c8ff4d122a256704706a7826ab1d62a75ee753aee256",
    ),
    "server/ScriptHookVDotNet.dll": (
        "53c67854b3fe7a9b6b51e1be5ddb99b47b119652c39e216165265f7271971ba1",
        "53c67854b3fe7a9b6b51e1be5ddb99b47b119652c39e216165265f7271971ba1",
    ),
    "server/ScriptHookVDotNet3.dll": (
        "3b2cd1a998d278f584c287bb05bbe14d1699ce0e4f117b62038f25fa19427951",
        "3b2cd1a998d278f584c287bb05bbe14d1699ce0e4f117b62038f25fa19427951",
    ),
    "server/RageCoop.Server.deps.json": (
        "b3cae23af707fb5269f21d3baee914459125577754306aba670861b617d6dcf4",
        "b3cae23af707fb5269f21d3baee914459125577754306aba670861b617d6dcf4",
    ),
    "server/RageCoop.Server.runtimeconfig.json": (
        "ded3aeb5856a11db0b654a785574490cab55839ebfb17efe9e39b89618fc5b91",
        "ded3aeb5856a11db0b654a785574490cab55839ebfb17efe9e39b89618fc5b91",
    ),
}
BASELINE_SOURCE_FILES = ("Program.cs", "PublicBaseline.csproj", "global.json", "packages.lock.json")
REMOVED_DIAGNOSTIC_SHA256 = "d22133818a30313e0becf010d78a556a56b34ea361dbd33588c9817631fed540"
SOURCE_DIRECTORIES = ("Client", "Server", "Shared", "Bootstrap", "EarlyClient", "Installer", "Checks")
PROJECT_FILES = (
    ".editorconfig",
    "Directory.Build.props",
    "References.example.props",
    "global.json",
    "Client/RageCoopAdmin.Client.csproj",
    "Client/packages.lock.json",
    "Client/Assets/PublicVehicleCatalog.json",
    "Client/Assets/MenuEnglish.resx",
    "Server/CoopAdmin.Server.csproj",
    "Server/packages.lock.json",
    "Bootstrap/CoopAdmin.Startup.csproj",
    "Bootstrap/packages.lock.json",
    "EarlyClient/RageCoop.EarlyClient.csproj",
    "EarlyClient/packages.lock.json",
    "Installer/app.manifest",
)
# Player files beside the client and the server: the installer at the archive
# root, the SHVDN settings template and the optional ReShade preset.
PUBLIC_INSTALLER = PUBLIC_BUILD / "Installer/Release/net48/CoopInstaller.exe"
EXTRA_FILES = (
    (Path("publication/ScriptHookVDotNet.ini"), "client/ScriptHookVDotNet.ini", "publication/ScriptHookVDotNet.ini"),
    (Path("work/ReShade/OGCoop.ini"), "reshade/OGCoop.ini", "work/ReShade/OGCoop.ini"),
)
# GitHub issue forms; they live in the repository, not in the add-on.
REPOSITORY_FILES = (
    (Path("publication/github/ISSUE_TEMPLATE/bug_report.yml"), ".github/ISSUE_TEMPLATE/bug_report.yml"),
    (Path("publication/github/ISSUE_TEMPLATE/config.yml"), ".github/ISSUE_TEMPLATE/config.yml"),
)
# Official upstream downloads, pinned by digest, unpacked into the add-on so a
# player needs only the game and ScriptHookV. They are fetched into
# outputs/public-deps (ENGINE → «Публичные проверки и упаковка»).
DEPENDENCY_DIRECTORY = Path("outputs/public-deps")


@dataclass(frozen=True)
class Upstream:
    archive: str
    url: str
    algorithm: str
    digest: str
    # Archive member -> add-on path; None maps every file under prefix.
    members: tuple[tuple[str, str], ...] | None
    prefix: str = ""


UPSTREAM = (
    Upstream(
        "ScriptHookVDotNet-v3.7.0-nightly.188.zip",
        "https://github.com/scripthookvdotnet/scripthookvdotnet-nightly/releases/download/"
        "v3.7.0-nightly.188/ScriptHookVDotNet-v3.7.0-nightly.188.zip",
        "sha256",
        "a9df84396363f040f40d6635e4f81951cac45f024be64d8d323ae24f4eceed19",
        (
            ("ScriptHookVDotNet.asi", "client/ScriptHookVDotNet.asi"),
            ("ScriptHookVDotNet2.dll", "client/ScriptHookVDotNet2.dll"),
            ("ScriptHookVDotNet3.dll", "client/ScriptHookVDotNet3.dll"),
            ("Licenses/LICENSE.txt", "licenses/ScriptHookVDotNet/LICENSE.txt"),
            ("Licenses/THIRD-PARTY-NOTICES.txt", "licenses/ScriptHookVDotNet/THIRD-PARTY-NOTICES.txt"),
            ("Licenses/COPYRIGHT.txt", "licenses/ScriptHookVDotNet/COPYRIGHT.txt"),
        ),
    ),
    Upstream(
        "dotnet-runtime-10.0.13-win-x64.zip",
        "https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.13/dotnet-runtime-10.0.13-win-x64.zip",
        "sha512",
        "2b98792ce86adc50899764ad2e4d21bd25cfda5432eeb0b4dfe7944f3ea049f9"
        "e0570a8f4aee92314d97cdb5f8b7be1363d501fc5012e680371e607af648ef40",
        None,
        "server/dotnet/",
    ),
)
UPSTREAM_MEMBER_LIMIT = 512
UPSTREAM_SIZE_LIMIT = 256 * 1024 * 1024
# One-click server start and stop for Windows and for Linux/macOS.
LAUNCHERS = ("Start server.cmd", "Stop server.cmd", "start-server.sh", "stop-server.sh")
TOOL_FILES = (
    "check.sh",
    "check-public.sh",
    "prepare_public_release.py",
    "test_prepare_public_release.py",
)
# What the private build adds lives in a rules file that is never published:
# its own files, terms that must not reach a public file, and its catalog.
PRIVATE_RULES = Path("tools/public-release-private.json")
# Product sources ship byte for byte: the client's version is a hash of them.
# Private code there lives in its own files, never in private blocks.
PRODUCT_DIRECTORIES = ("Client", "Server", "Shared", "Bootstrap", "EarlyClient", "Installer")
PRIVATE_SOURCE_BEGIN = "// <private-source>"
PRIVATE_SOURCE_END = "// </private-source>"
PRIVATE_PROJECT_CONDITION = "'$(PublicRelease)' == 'false'"
PRIVATE_DEFAULT = "<PublicRelease Condition=\"'$(PublicRelease)' == ''\">false</PublicRelease>"
PUBLIC_DEFAULT = "<PublicRelease Condition=\"'$(PublicRelease)' == ''\">true</PublicRelease>"
SOURCE_VIEW = PUBLIC_BUILD / "source-view"
TEXT_SUFFIXES = frozenset(
    (".cs", ".csproj", ".props", ".json", ".md", ".txt", ".ini", ".cmd", ".sh", ".py", ".yml", ".resx", ".manifest")
)
# Our own binaries; third-party and baseline files are pinned elsewhere.
OWN_BINARIES = (
    "client/scripts/OGCoop.EarlyClient.dll",
    "server/Resources/Client/OGCoop/RageCoopAdmin.Client.dll",
    "server/Resources/Server/CoopAdmin/CoopAdmin.dll",
    "server/Bootstrap/CoopAdmin.Startup.dll",
    "Install.exe",
)
SKIP_DIRECTORIES = frozenset(("bin", "obj", "lib", "__pycache__"))
ARCHIVE_NAMES = ("OGCoop-Addon.zip", "OGCoop-Source.zip")
MANIFEST_NAME = "OGCoop-manifest.json"
OUTPUT_DIRECTORY = Path("outputs/public")
CHUNK_SIZE = 1024 * 1024
PRIVATE_PATH = re.compile(
    rb"/(?:Users|home)/[^\s/'\"<>]{1,255}/|[A-Za-z]:[\\/](?:Users|Documents and Settings)[\\/]"
)
# Unmodified Lib.Harmony 2.4.3 binaries from the pinned NuGet package.
PINNED_HARMONY_SHA256 = frozenset(
    (
        "ddc070b57f695ece6f92c43cf82cf4d863c69685de96a99ebd9d2fa1d8fa7a19",
        "f7d167b2500808c6c2ad6e38b6efa2741a2b809f79b0e92c36e4a7079c38d9bb",
    )
)
UPSTREAM_CI_PREFIX = b"/home" + b"/runner/work/"


class ReleaseError(ValueError):
    """An input does not meet the public archive contract."""


@dataclass(frozen=True)
class Entry:
    source: Path
    name: str
    size: int
    sha256: str
    upstream_ci_paths: bool = False


def regular_file(path: Path) -> None:
    """Reject symbolic links in the selected file's entire path."""
    for part in (*reversed(path.parents), path):
        if part.is_symlink():
            raise ReleaseError(f"Symbolic links are not allowed: {part.name}")
    if not path.is_file():
        raise ReleaseError(f"Required file is missing or not regular: {path.name}")
    if not stat.S_ISREG(path.stat().st_mode):
        raise ReleaseError(f"Input is not a regular file: {path.name}")


def stream_digest(stream: IO[bytes]) -> tuple[int, str]:
    digest = hashlib.sha256()
    size = 0
    while block := stream.read(CHUNK_SIZE):
        digest.update(block)
        size += len(block)
    return size, digest.hexdigest()


def file_digest(path: Path) -> tuple[int, str]:
    regular_file(path)
    with path.open("rb") as stream:
        return stream_digest(stream)


def reject_private_paths(path: Path, name: str, *, text: bool, root: Path, sha256: str) -> bool:
    root_bytes = os.fsencode(root)
    overlap = max(2048, 2 * len(root_bytes) + 2)
    decoder = codecs.getincrementaldecoder("utf-8")() if text else None
    tail = b""
    pinned_harmony = not text and path.name == "0Harmony.dll" and sha256 in PINNED_HARMONY_SHA256

    def inspect(data: bytes, *, final: bool) -> bool:
        scan = data if text else data.replace(b"\x00", b"")
        if root_bytes in scan:
            raise ReleaseError(f"Private absolute path found in public file: {name}")
        allowed = False
        for match in PRIVATE_PATH.finditer(scan):
            # Keep a trailing partial CI prefix for the following chunk.
            if not final and match.start() > len(scan) - overlap:
                continue
            if pinned_harmony and scan[match.start() :].startswith(UPSTREAM_CI_PREFIX):
                allowed = True
            else:
                raise ReleaseError(f"Private absolute path found in public file: {name}")
        return allowed

    upstream_ci_paths = False
    with path.open("rb") as stream:
        while block := stream.read(CHUNK_SIZE):
            if decoder is not None:
                decoder.decode(block)
            combined = tail + block
            # CodeView uses ASCII; managed literals can contain UTF-16 paths.
            # Removing NULs also detects those without retaining the whole DLL.
            upstream_ci_paths |= inspect(combined, final=False)
            tail = combined[-overlap:]
    if decoder is not None:
        decoder.decode(b"", final=True)
    return upstream_ci_paths | inspect(tail, final=True)


def entry(path: Path, name: str, *, text: bool, root: Path) -> Entry:
    archive_path = PurePosixPath(name)
    if archive_path.is_absolute() or ".." in archive_path.parts or "\\" in name:
        raise ReleaseError("Archive names must be relative portable paths.")
    size, digest = file_digest(path)
    if size == 0:
        raise ReleaseError(f"Required file is empty: {name}")
    upstream_ci_paths = reject_private_paths(path, name, text=text, root=root, sha256=digest)
    return Entry(path, name, size, digest, upstream_ci_paths)


@dataclass(frozen=True)
class PrivateRules:
    excluded: frozenset[str]
    substrings: tuple[str, ...]
    words: tuple[str, ...]


def relative_project_path(value: object) -> str:
    if not isinstance(value, str) or not value:
        raise ReleaseError("Private rules list project paths as strings.")
    path = PurePosixPath(value)
    if path.is_absolute() or ".." in path.parts or "\\" in value:
        raise ReleaseError("Private rules paths must be relative portable paths.")
    return path.as_posix()


def load_private_rules(root: Path) -> PrivateRules:
    path = root / PRIVATE_RULES
    regular_file(path)
    try:
        raw = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_object)
    except json.JSONDecodeError as error:
        raise ReleaseError("The private rules are not valid JSON.") from error
    rules = object_fields(
        raw, {"excludedFiles", "forbiddenSubstrings", "privateCatalog", "sharedCatalogWords"}, "private rules"
    )
    for name in ("excludedFiles", "forbiddenSubstrings", "sharedCatalogWords"):
        if not isinstance(rules[name], list) or not all(isinstance(item, str) and item for item in rules[name]):
            raise ReleaseError(f"Private rules {name} must be a list of non-empty strings.")
    if not rules["forbiddenSubstrings"]:
        raise ReleaseError("Private rules must forbid at least one term.")
    catalog_path = root / relative_project_path(rules["privateCatalog"])
    regular_file(catalog_path)
    try:
        catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
        vehicles = catalog["vehicles"]
        words = {
            str(vehicle[key]).casefold()
            for vehicle in vehicles
            for key in ("id", "spawnCode")
            if isinstance(vehicle[key], str) and vehicle[key]
        }
    except (json.JSONDecodeError, KeyError, TypeError) as error:
        raise ReleaseError("The private catalog does not list its vehicles.") from error
    if not words:
        raise ReleaseError("The private catalog lists no vehicles.")
    words -= {word.casefold() for word in rules["sharedCatalogWords"]}
    return PrivateRules(
        frozenset(relative_project_path(name) for name in rules["excludedFiles"]),
        tuple(term.casefold() for term in rules["forbiddenSubstrings"]),
        tuple(sorted(words)),
    )


def public_csharp(text: str, name: str) -> str:
    """The checks as the public profile compiles them, without private blocks."""
    kept: list[str] = []
    frames: list[list[object]] = []
    private_block = False
    for line in text.splitlines(keepends=True):
        stripped = line.strip()
        if stripped == PRIVATE_SOURCE_BEGIN or stripped == PRIVATE_SOURCE_END:
            if private_block == (stripped == PRIVATE_SOURCE_BEGIN):
                raise ReleaseError(f"Unbalanced private-source markers: {name}")
            private_block = stripped == PRIVATE_SOURCE_BEGIN
            continue
        directive = re.fullmatch(r"#\s*(if|elif|else|endif)\b\s*(.*)", stripped)
        if directive is not None:
            word, condition = directive.group(1), directive.group(2).strip()
            if word == "if":
                kind = {"PUBLIC_RELEASE": "public", "!PUBLIC_RELEASE": "private"}.get(condition, "other")
                if kind == "other" and "PUBLIC_RELEASE" in condition:
                    raise ReleaseError(f"Unsupported PUBLIC_RELEASE condition: {name}")
                frames.append([kind, False])
                if kind != "other":
                    continue
            elif not frames:
                raise ReleaseError(f"Unbalanced preprocessor block: {name}")
            elif word == "elif":
                if frames[-1][0] != "other" or "PUBLIC_RELEASE" in condition:
                    raise ReleaseError(f"Unsupported PUBLIC_RELEASE condition: {name}")
            elif word == "else" and frames[-1][0] != "other":
                frames[-1][1] = True
                continue
            elif word == "endif" and frames.pop()[0] != "other":
                continue
        if not private_block and all(
            kind == "other" or (kind == "public") != in_else for kind, in_else in frames
        ):
            kept.append(line)
    if frames or private_block:
        raise ReleaseError(f"Unbalanced preprocessor block: {name}")
    return "".join(kept)


def public_project(text: str, excluded: frozenset[str]) -> str:
    """A project file without private items, defaulting to the public profile."""
    names = {PurePosixPath(path).name for path in excluded}
    kept: list[str] = []
    in_private_group = False
    for line in text.splitlines(keepends=True):
        stripped = line.strip()
        if in_private_group:
            in_private_group = not stripped.startswith("</ItemGroup>")
            continue
        if PRIVATE_PROJECT_CONDITION in stripped:
            in_private_group = stripped.startswith("<ItemGroup") and not stripped.endswith("/>")
            continue
        if any(name in stripped for name in names):
            continue
        kept.append(line.replace(PRIVATE_DEFAULT, PUBLIC_DEFAULT))
    if in_private_group:
        raise ReleaseError("A private item group in a project file is not closed.")
    return "".join(kept)


def public_text(path: Path, relative: PurePosixPath, rules: PrivateRules) -> str:
    text = path.read_bytes().decode("utf-8")
    if path.suffix == ".csproj":
        return public_project(text, rules.excluded)
    if path.name == "Directory.Build.props":
        # Published sources build the public profile without extra arguments.
        if not text.startswith("<Project>\n"):
            raise ReleaseError("Directory.Build.props must start with <Project>.")
        return text.replace(
            "<Project>\n",
            "<Project>\n  <PropertyGroup>\n    " + PUBLIC_DEFAULT + "\n  </PropertyGroup>\n",
            1,
        )
    if path.suffix != ".cs":
        return text
    component = relative.relative_to(PurePosixPath(PROJECT.as_posix())).parts[0]
    if component in PRODUCT_DIRECTORIES:
        if "#if !PUBLIC_RELEASE" in text or PRIVATE_SOURCE_BEGIN in text:
            raise ReleaseError(f"Product source has a private block; move it to a private file: {relative}")
        return text
    return public_csharp(text, relative.as_posix())


def public_source_entry(root: Path, path: Path, rules: PrivateRules) -> Entry:
    relative = PurePosixPath(path.relative_to(root).as_posix())
    regular_file(path)
    text = public_text(path, relative, rules)
    if text.encode("utf-8") == path.read_bytes():
        return entry(path, relative.as_posix(), text=True, root=root)
    staged = root / SOURCE_VIEW / relative
    staged.parent.mkdir(parents=True, exist_ok=True)
    staged.write_bytes(text.encode("utf-8"))
    return entry(staged, relative.as_posix(), text=True, root=root)


def reset_source_view(root: Path) -> None:
    view = root / SOURCE_VIEW
    for part in (root / PUBLIC_BUILD, view):
        if part.is_symlink():
            raise ReleaseError("The public source view must not be a symbolic link.")
    if view.exists():
        shutil.rmtree(view)


def forbidden_term(text: str, rules: PrivateRules) -> str | None:
    folded = text.casefold()
    for term in rules.substrings:
        if term in folded:
            return term
    for word in rules.words:
        if re.search(r"(?<![a-z0-9_])" + re.escape(word) + r"(?![a-z0-9_])", folded):
            return word
    return None


def reject_private_terms(entries: list[Entry], rules: PrivateRules) -> None:
    """Nothing private may reach a published text file or our own binaries."""
    for item in entries:
        name = PurePosixPath(item.name)
        if name.suffix in TEXT_SUFFIXES or name.name in ("LICENSE", ".editorconfig"):
            term = forbidden_term(item.source.read_bytes().decode("utf-8"), rules)
        elif item.name in OWN_BINARIES:
            # Type, member and resource names in any letter case; the public
            # catalog check already proves no private vehicle is embedded.
            data = item.source.read_bytes()
            lowered = data.lower()
            term = next(
                (
                    term
                    for term in rules.substrings
                    if (
                        term.encode("utf-8") in lowered or term.encode("utf-16-le") in lowered
                        if term.isascii()
                        else any(
                            variant.encode(encoding) in data
                            for variant in {term, term.capitalize(), term.upper()}
                            for encoding in ("utf-8", "utf-16-le")
                        )
                    )
                ),
                None,
            )
        else:
            continue
        if term is not None:
            raise ReleaseError(f"A private term is left in a public file: {item.name} ({term})")


def source_files(root: Path, rules: PrivateRules) -> list[Path]:
    project = root / PROJECT
    selected = {project / name for name in PROJECT_FILES}
    for component in SOURCE_DIRECTORIES:
        directory = project / component
        if directory.is_symlink() or not directory.is_dir():
            raise ReleaseError(f"Required source directory is missing or symbolic: {component}")
        for current, directories, filenames in os.walk(directory, followlinks=False):
            for name in directories:
                if (
                    name not in SKIP_DIRECTORIES
                    and not name.startswith(".")
                    and (Path(current) / name).is_symlink()
                ):
                    raise ReleaseError(f"Symbolic source directory is not allowed: {name}")
            directories[:] = sorted(
                name
                for name in directories
                if name not in SKIP_DIRECTORIES and not name.startswith(".")
            )
            for name in filenames:
                if (Path(current) / name).relative_to(project).as_posix() in rules.excluded:
                    continue
                if Path(name).suffix in (".cs", ".csproj") or name == "packages.lock.json":
                    selected.add(Path(current) / name)
    return sorted(selected)


def object_fields(value: object, names: set[str], label: str) -> dict[str, object]:
    if not isinstance(value, dict) or set(value) != names:
        raise ReleaseError(f"Invalid baseline {label} fields.")
    return {name: value[name] for name in names}


def unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for name, value in pairs:
        if name in result:
            raise ReleaseError("Duplicate baseline JSON field.")
        result[name] = value
    return result


def collect_baseline(root: Path) -> tuple[list[Entry], dict[str, object]]:
    path = root / BASELINE_DIRECTORY / "manifest.json"
    manifest_entry = entry(path, "public-baseline/manifest.json", text=True, root=root)
    if manifest_entry.size > 128 * 1024:
        raise ReleaseError("The baseline manifest is too large.")
    try:
        with path.open(encoding="utf-8") as stream:
            raw: object = json.load(stream, object_pairs_hook=unique_object)
    except json.JSONDecodeError as error:
        raise ReleaseError("The baseline manifest is not valid JSON.") from error
    manifest = object_fields(
        raw,
        {"version", "compatibility", "provenance", "transformation", "verification", "files"},
        "manifest",
    )
    if (
        type(manifest["version"]) is not int
        or manifest["version"] != 1
        or manifest["compatibility"] != "RageCoop 1.5.12.15 / SHVDN 3.7.0-nightly.188"
    ):
        raise ReleaseError("Unsupported baseline compatibility or version.")
    transformation = object_fields(
        manifest["transformation"],
        {"resource", "metadata", "removedAssemblySha256", "operation"},
        "transformation",
    )
    if (
        transformation["resource"] != "costura.system.diagnostics.diagnosticsource.dll.compressed"
        or transformation["metadata"] != "costura.metadata"
        or transformation["removedAssemblySha256"] != REMOVED_DIAGNOSTIC_SHA256
    ):
        raise ReleaseError("Unsupported baseline resource transformation.")
    verification = object_fields(
        manifest["verification"],
        {
            "MethodBodies",
            "RawIlBytes",
            "UnchangedMetadataTables",
            "ResourcesBefore",
            "ResourcesAfter",
            "Mvid",
            "Scope",
        },
        "verification",
    )
    expected_counts = {
        "MethodBodies": 778,
        "RawIlBytes": 73173,
        "UnchangedMetadataTables": 52,
        "ResourcesBefore": 22,
        "ResourcesAfter": 21,
    }
    if (
        any(
            type(verification[name]) is not int or verification[name] != count
            for name, count in expected_counts.items()
        )
        or verification["Mvid"] != "81af895b-a74a-4395-9e93-3aaf57441be4"
    ):
        raise ReleaseError("Unsupported baseline verification identity.")
    for value in (manifest["provenance"], transformation["operation"], verification["Scope"]):
        if not isinstance(value, str) or not value.strip():
            raise ReleaseError("The baseline provenance and verification scope are required.")
    records = manifest["files"]
    if not isinstance(records, list) or len(records) != len(BASELINE_PINNED_HASHES):
        raise ReleaseError("The baseline must contain exactly nine known files.")
    seen: set[str] = set()
    entries = []
    for raw_record in records:
        record = object_fields(raw_record, {"path", "sourceSha256", "sha256", "size"}, "file")
        name = record["path"]
        if not isinstance(name, str) or name not in BASELINE_PINNED_HASHES or name in seen:
            raise ReleaseError("Unexpected or duplicate baseline file path.")
        seen.add(name)
        source_digest, digest = BASELINE_PINNED_HASHES[name]
        size = record["size"]
        if (
            record["sourceSha256"] != source_digest
            or record["sha256"] != digest
            or type(size) is not int
            or size <= 0
        ):
            raise ReleaseError("The baseline file hashes or size do not match the pinned contract.")
        item = entry(root / BASELINE_DIRECTORY / name, name, text=name.endswith(".json"), root=root)
        if (item.sha256, item.size) != (digest, size):
            raise ReleaseError(f"Baseline bytes differ from the manifest: {name}")
        entries.append(item)
    entries.append(manifest_entry)
    return entries, manifest


def upstream_entries(root: Path) -> tuple[list[Entry], list[dict[str, str]]]:
    """Verify each pinned upstream archive, unpack the selected files and list them."""
    entries: list[Entry] = []
    records: list[dict[str, str]] = []
    unpacked_root = root / DEPENDENCY_DIRECTORY / "unpacked"
    for upstream in UPSTREAM:
        path = root / DEPENDENCY_DIRECTORY / upstream.archive
        regular_file(path)
        digest = hashlib.new(upstream.algorithm)
        with path.open("rb") as stream:
            while block := stream.read(CHUNK_SIZE):
                digest.update(block)
        if digest.hexdigest() != upstream.digest:
            raise ReleaseError(f"Upstream download differs from its pinned digest: {upstream.archive}")
        target = unpacked_root / Path(upstream.archive).stem
        if target.is_symlink():
            raise ReleaseError("The upstream unpack directory must not be a symbolic link.")
        if target.exists():
            shutil.rmtree(target)
        target.mkdir(parents=True)
        with zipfile.ZipFile(path) as archive:
            files = [info for info in archive.infolist() if not info.is_dir()]
            if len(files) > UPSTREAM_MEMBER_LIMIT or sum(info.file_size for info in files) > UPSTREAM_SIZE_LIMIT:
                raise ReleaseError(f"Upstream archive is larger than expected: {upstream.archive}")
            by_name = {info.filename: info for info in files}
            pairs = (
                upstream.members
                if upstream.members is not None
                else tuple((info.filename, upstream.prefix + info.filename) for info in files)
            )
            for member, name in pairs:
                info = by_name.get(member)
                member_path = PurePosixPath(member)
                if info is None or member_path.is_absolute() or ".." in member_path.parts or "\\" in member:
                    raise ReleaseError(f"Upstream archive lacks a safe {member}: {upstream.archive}")
                destination = target / member_path
                destination.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(info) as source, destination.open("wb") as written:
                    shutil.copyfileobj(source, written, CHUNK_SIZE)
                entries.append(entry(destination, name, text=False, root=root))
        records.append(
            {
                "archive": upstream.archive,
                "url": upstream.url,
                upstream.algorithm: upstream.digest,
            }
        )
    return entries, records


def collect_inputs(
    root: Path, public_client: Path, expected_sha256: str
) -> tuple[list[Entry], list[Entry], dict[str, object]]:
    """Return the add-on files, the source files and their provenance."""
    if re.fullmatch(r"[0-9a-fA-F]{64}", expected_sha256) is None:
        raise ReleaseError(
            "The public client SHA-256 must contain exactly 64 hexadecimal characters."
        )
    _, actual = file_digest(public_client)
    if actual != expected_sha256.lower():
        raise ReleaseError("The public client SHA-256 differs from the checked public DLL.")
    catalog_path = root / PROJECT / "Client/Assets/PublicVehicleCatalog.json"
    regular_file(catalog_path)
    try:
        with catalog_path.open(encoding="utf-8") as stream:
            catalog = json.load(stream)
    except json.JSONDecodeError as error:
        raise ReleaseError("The public source catalog is not valid JSON.") from error
    if catalog != {"version": 1, "vehicles": []} or type(catalog["version"]) is not int:
        raise ReleaseError(
            "The public source catalog must be version 1 with an empty vehicle list."
        )

    docs = [entry(root / "publication" / name, name, text=True, root=root) for name in DOC_NAMES]
    docs.extend(
        entry(root / "publication" / name, name, text=False, root=root) for name in SCREENSHOT_FILES
    )
    binary = list(docs)
    source = list(docs)
    baseline, baseline_manifest = collect_baseline(root)
    binary.extend(baseline)
    binary_inputs = (
        (
            PUBLIC_BUILD / "EarlyClient/Release/net48/OGCoop.EarlyClient.dll",
            "client/scripts/OGCoop.EarlyClient.dll",
        ),
        (PUBLIC_BUILD / "EarlyClient/Release/net48/0Harmony.dll", "client/scripts/0Harmony.dll"),
        (public_client, "server/Resources/Client/OGCoop/RageCoopAdmin.Client.dll"),
        (
            PUBLIC_BUILD / "EarlyClient/Release/net48/0Harmony.dll",
            "server/Resources/Client/OGCoop/0Harmony.dll",
        ),
        (
            PUBLIC_BUILD / "Server/Release/net10.0/CoopAdmin.dll",
            "server/Resources/Server/CoopAdmin/CoopAdmin.dll",
        ),
        (
            PUBLIC_BUILD / "Bootstrap/Release/net10.0/CoopAdmin.Startup.dll",
            "server/Bootstrap/CoopAdmin.Startup.dll",
        ),
        (PUBLIC_BUILD / "Bootstrap/Release/net10.0/0Harmony.dll", "server/Bootstrap/0Harmony.dll"),
    )
    binary.extend(entry(root / path, name, text=False, root=root) for path, name in binary_inputs)
    binary.append(entry(root / PUBLIC_INSTALLER, "Install.exe", text=False, root=root))
    for path, binary_name, source_name in EXTRA_FILES:
        binary.append(entry(root / path, binary_name, text=True, root=root))
        source.append(entry(root / path, source_name, text=True, root=root))
    source.extend(entry(root / path, name, text=True, root=root) for path, name in REPOSITORY_FILES)
    upstream, upstream_records = upstream_entries(root)
    binary.extend(upstream)
    if (
        next(item.sha256 for item in binary if item.name.endswith("/RageCoopAdmin.Client.dll"))
        != expected_sha256.lower()
    ):
        raise ReleaseError("The public client changed during input validation.")
    rules = load_private_rules(root)
    reset_source_view(root)
    for path in source_files(root, rules):
        source.append(public_source_entry(root, path, rules))
    for name in BASELINE_SOURCE_FILES:
        relative = Path("work/PublicBaseline") / name
        source.append(entry(root / relative, relative.as_posix(), text=True, root=root))
    source.extend(
        entry(root / "tools" / name, "tools/" + name, text=True, root=root) for name in TOOL_FILES
    )
    launchers = [root / "publication" / name for name in LAUNCHERS]
    if any(path.exists() or path.is_symlink() for path in launchers):
        for path in launchers:
            binary.append(entry(path, "server/" + path.name, text=True, root=root))
            source.append(entry(path, "publication/" + path.name, text=True, root=root))
    for entries in (binary, source):
        reject_private_terms(entries, rules)
        names = [item.name.casefold() for item in entries]
        if len(names) != len(set(names)):
            raise ReleaseError(
                "Archive paths must be unique, including on case-insensitive systems."
            )
        entries.sort(key=lambda item: item.name)
    return binary, source, {"baseline": baseline_manifest, "upstream": upstream_records}


def write_archive(path: Path, entries: list[Entry]) -> None:
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for item in entries:
            regular_file(item.source)
            info = zipfile.ZipInfo(item.name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            mode = 0o100755 if item.name.endswith(".sh") else 0o100644
            info.external_attr = mode << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            with item.source.open("rb") as source, archive.open(info, "w") as target:
                shutil.copyfileobj(source, target, CHUNK_SIZE)
    with zipfile.ZipFile(path) as archive:
        if archive.namelist() != [item.name for item in entries]:
            raise ReleaseError("The generated archive has an unexpected file list.")
        for item in entries:
            with archive.open(item.name) as stream:
                if stream_digest(stream) != (item.size, item.sha256):
                    raise ReleaseError(f"Input changed while preparing the archive: {item.name}")


def prepare_release(
    root: Path, expected_sha256: str, public_client: Path | None = None
) -> tuple[Path, ...]:
    if root.is_symlink():
        raise ReleaseError("The project root must not be a symbolic link.")
    root = root.resolve(strict=True)
    if not root.is_dir():
        raise ReleaseError("The project root must be a directory.")
    selected_client = PUBLIC_CLIENT if public_client is None else public_client
    if not selected_client.is_absolute():
        selected_client = root / selected_client
    binary, source, provenance = collect_inputs(root, selected_client, expected_sha256)
    output = root / OUTPUT_DIRECTORY
    for directory in (output.parent, output):
        if directory.is_symlink() or (directory.exists() and not directory.is_dir()):
            raise ReleaseError("The output directory and its parent must be real directories.")
    destinations = tuple(output / name for name in (*ARCHIVE_NAMES, MANIFEST_NAME))
    for destination in destinations:
        if destination.exists() or destination.is_symlink():
            regular_file(destination)

    # Every input is validated before creating the output directory. All files
    # are fully prepared and checked before individual atomic replacements;
    # the manifest is replaced last and records both completed ZIP digests.
    output.mkdir(parents=True, exist_ok=True)
    staged: list[Path] = []
    try:
        for destination in destinations:
            descriptor, name = tempfile.mkstemp(
                prefix="." + destination.name + "-", suffix=".tmp", dir=output
            )
            os.close(descriptor)
            staged.append(Path(name))
        archive_records = {}
        for path, name, entries in zip(staged[:2], ARCHIVE_NAMES, (binary, source), strict=True):
            write_archive(path, entries)
            size, digest = file_digest(path)
            archive_records[name] = {
                "size": size,
                "sha256": digest,
                "files": [
                    {
                        "path": item.name,
                        "size": item.size,
                        "sha256": item.sha256,
                        **({"upstreamCiBuildPaths": True} if item.upstream_ci_paths else {}),
                    }
                    for item in entries
                ],
            }
        manifest = {
            "formatVersion": 1,
            "profile": "public-no-hud-stock-vehicles",
            "publicClientSha256": expected_sha256.lower(),
            "baseline": provenance["baseline"],
            "upstream": provenance["upstream"],
            "archives": archive_records,
        }
        staged[-1].write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        for path in staged:
            os.chmod(path, 0o644)
            with path.open("rb") as stream:
                os.fsync(stream.fileno())
        for path, destination in zip(staged, destinations, strict=True):
            os.replace(path, destination)
    finally:
        for path in staged:
            path.unlink(missing_ok=True)
    return destinations


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument(
        "--public-client",
        type=Path,
        help="Checked public DLL; relative paths are relative to --root.",
    )
    parser.add_argument(
        "--public-client-sha256",
        required=True,
        help="SHA-256 attested by the actual public assembly check.",
    )
    args = parser.parse_args()
    try:
        paths = prepare_release(args.root, args.public_client_sha256, args.public_client)
    except (OSError, UnicodeError, ReleaseError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Public release preparation failed: {error}\n")
    for path in paths:
        size, digest = file_digest(path)
        print(f"{path.relative_to(args.root.resolve())}: {size} bytes, SHA-256 {digest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
