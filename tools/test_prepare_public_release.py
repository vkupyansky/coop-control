"""Run with Python warnings as errors and unittest discover -s tools."""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

import prepare_public_release as release

IMAGES = (".png", ".jpg", ".jpeg", ".webp", ".gif")


class PublicReleaseTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="public-release-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        for name in release.DOC_NAMES:
            self.put(Path("publication") / name, "Public documentation\n")
        for name in release.SCREENSHOT_FILES:
            self.put(Path("publication") / name, b"isolated screenshot fixture\x00")
        for name in ("start-server.sh", "stop-server.sh"):
            self.put(Path("publication") / name, "#!/bin/sh\nexit 0\n")
        for name in ("Start server.cmd", "Stop server.cmd"):
            self.put(Path("publication") / name, "@exit /b 0\r\n")
        for name in release.PROJECT_FILES:
            self.put(release.PROJECT / name, "public project\n")
        self.put(release.PROJECT / "Directory.Build.props", "<Project>\n</Project>\n")
        self.put(
            release.PRIVATE_RULES,
            json.dumps(
                {
                    "excludedFiles": ["Client/PrivateFeature.cs", "Checks/Example/Program.PrivateFeature.cs"],
                    "forbiddenSubstrings": ["privatefeature"],
                    "privateCatalog": "work/PrivateCars/catalog.json",
                    "sharedCatalogWords": ["sharedcar"],
                }
            ),
        )
        self.put(
            Path("work/PrivateCars/catalog.json"),
            json.dumps(
                {
                    "version": 1,
                    "vehicles": [
                        {"id": "private-car", "spawnCode": "privcar7"},
                        {"id": "shared-car", "spawnCode": "sharedcar"},
                    ],
                }
            ),
        )
        self.put(
            release.PROJECT / "Client/Assets/PublicVehicleCatalog.json",
            '{"version":1,"vehicles":[]}\n',
        )
        for component in release.SOURCE_DIRECTORIES:
            self.put(release.PROJECT / component / "Example.cs", "namespace PublicExample;\n")
        for name in release.TOOL_FILES:
            self.put(Path("tools") / name, "public tool\n")
        for name in release.BASELINE_SOURCE_FILES:
            self.put(Path("work/PublicBaseline") / name, "public baseline source\n")
        self.create_baseline()
        for path in (
            "EarlyClient/Release/net48/OGCoop.EarlyClient.dll",
            "EarlyClient/Release/net48/0Harmony.dll",
            "Server/Release/net10.0/CoopAdmin.dll",
            "Bootstrap/Release/net10.0/CoopAdmin.Startup.dll",
            "Bootstrap/Release/net10.0/0Harmony.dll",
        ):
            self.put(release.PUBLIC_BUILD / path, b"isolated binary fixture\x00")
        self.put(release.PUBLIC_CLIENT, b"checked public client fixture\x00")
        self.put(release.PUBLIC_INSTALLER, b"MZ isolated installer fixture\x00")
        for path, _, _ in release.EXTRA_FILES:
            self.put(path, "Fixture=1\n")
        for path, _ in release.REPOSITORY_FILES:
            self.put(path, "name: fixture\n")
        self.create_upstream()
        self.digest = hashlib.sha256((self.root / release.PUBLIC_CLIENT).read_bytes()).hexdigest()

    def create_upstream(self) -> None:
        pinned = []
        for upstream in release.UPSTREAM:
            members = upstream.members or (("dotnet.exe", ""), ("shared/runtime.dll", ""))
            path = self.root / release.DEPENDENCY_DIRECTORY / upstream.archive
            path.parent.mkdir(parents=True, exist_ok=True)
            with zipfile.ZipFile(path, "w") as archive:
                for member, _ in members:
                    archive.writestr(member, ("upstream fixture " + member).encode())
                archive.writestr("unused/readme.txt", "not shipped")
            digest = hashlib.new(upstream.algorithm, path.read_bytes()).hexdigest()
            pinned.append(release.Upstream(upstream.archive, upstream.url, upstream.algorithm, digest, upstream.members, upstream.prefix))
        patcher = patch.object(release, "UPSTREAM", tuple(pinned))
        patcher.start()
        self.addCleanup(patcher.stop)

    def create_baseline(self) -> None:
        pins = {}
        rows = []
        for name in release.BASELINE_PINNED_HASHES:
            data = ("isolated baseline fixture: " + name).encode()
            self.put(release.BASELINE_DIRECTORY / name, data)
            digest = hashlib.sha256(data).hexdigest()
            source_digest = (
                hashlib.sha256(b"original" + data).hexdigest()
                if name.endswith("RageCoop.Client.dll")
                else digest
            )
            pins[name] = (source_digest, digest)
            rows.append(
                {"path": name, "sourceSha256": source_digest, "sha256": digest, "size": len(data)}
            )
        pinned = patch.object(release, "BASELINE_PINNED_HASHES", pins)
        pinned.start()
        self.addCleanup(pinned.stop)
        manifest = {
            "version": 1,
            "compatibility": "RageCoop 1.5.12.15 / SHVDN 3.7.0-nightly.188",
            "provenance": "Isolated test fixture",
            "transformation": {
                "resource": "costura.system.diagnostics.diagnosticsource.dll.compressed",
                "metadata": "costura.metadata",
                "removedAssemblySha256": release.REMOVED_DIAGNOSTIC_SHA256,
                "operation": "Isolated fixture transformation",
            },
            "verification": {
                "MethodBodies": 778,
                "RawIlBytes": 73173,
                "UnchangedMetadataTables": 52,
                "ResourcesBefore": 22,
                "ResourcesAfter": 21,
                "Mvid": "81af895b-a74a-4395-9e93-3aaf57441be4",
                "Scope": "Isolated fixture verification",
            },
            "files": rows,
        }
        self.put(release.BASELINE_DIRECTORY / "manifest.json", json.dumps(manifest))

    def put(self, relative: Path, content: str | bytes) -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content.encode() if isinstance(content, str) else content)
        return path

    def prepare(self) -> tuple[Path, ...]:
        return release.prepare_release(self.root, self.digest)

    def assert_no_release_outputs(self) -> None:
        for directory in (self.root / "outputs", self.root / release.OUTPUT_DIRECTORY):
            for name in (*release.ARCHIVE_NAMES, release.MANIFEST_NAME):
                self.assertFalse((directory / name).exists())
            self.assertEqual(list(directory.glob(".*.tmp")), [])

    def test_only_public_output_directory_is_used_and_old_root_artifacts_are_preserved(
        self,
    ) -> None:
        old_paths = [
            self.put(Path("outputs") / name, b"untouched old root artifact")
            for name in (*release.ARCHIVE_NAMES, release.MANIFEST_NAME)
        ]
        paths = self.prepare()
        self.assertEqual(
            paths,
            tuple(
                self.root / release.OUTPUT_DIRECTORY / name
                for name in (*release.ARCHIVE_NAMES, release.MANIFEST_NAME)
            ),
        )
        self.assertTrue(
            all(path.read_bytes() == b"untouched old root artifact" for path in old_paths)
        )

    def test_symbolic_public_output_directory_is_rejected_before_writes(self) -> None:
        output = self.root / release.OUTPUT_DIRECTORY
        target = self.root / "outside"
        target.mkdir()
        marker = target / "untouched.txt"
        marker.write_bytes(b"untouched target")
        for destination in (target, self.root / "absent-outside"):
            with self.subTest(destination=destination.name):
                output.symlink_to(destination, target_is_directory=True)
                with self.assertRaises(release.ReleaseError):
                    self.prepare()
                self.assertEqual(list(target.iterdir()), [marker])
                self.assertEqual(marker.read_bytes(), b"untouched target")
                self.assertFalse((self.root / "absent-outside").exists())
                self.assert_no_release_outputs()
                output.unlink()

    def test_nondirectory_public_output_is_preserved(self) -> None:
        output = self.put(release.OUTPUT_DIRECTORY, b"not an output directory")
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assertEqual(output.read_bytes(), b"not an output directory")
        self.assert_no_release_outputs()

    def test_symbolic_output_parent_is_rejected_before_writes(self) -> None:
        parent = self.root / "outputs"
        target = self.root / "moved-outputs"
        parent.rename(target)
        parent.symlink_to(target, target_is_directory=True)
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assertFalse((target / "public").exists())
        self.assertEqual(list(target.glob(".*.tmp")), [])
        self.assert_no_release_outputs()

    def test_release_requires_every_server_button(self) -> None:
        for name in release.LAUNCHERS:
            path = self.root / "publication" / name
            kept = path.read_bytes()
            path.unlink()
            with self.subTest(name=name), self.assertRaises(release.ReleaseError):
                self.prepare()
            self.assert_no_release_outputs()
            path.write_bytes(kept)

    def test_exact_public_binary_tree_and_manifest(self) -> None:
        binary, source, manifest_path = self.prepare()
        self.assertTrue(
            all(
                path.parent == self.root / release.OUTPUT_DIRECTORY
                for path in (binary, source, manifest_path)
            )
        )
        expected = (
            set(release.DOC_NAMES)
            | set(release.SCREENSHOT_FILES)
            | {
                "client/scripts/OGCoop.EarlyClient.dll",
                "client/scripts/0Harmony.dll",
                "server/Resources/Client/OGCoop/RageCoopAdmin.Client.dll",
                "server/Resources/Client/OGCoop/0Harmony.dll",
                "server/Resources/Server/CoopAdmin/CoopAdmin.dll",
                "server/Bootstrap/CoopAdmin.Startup.dll",
                "server/Bootstrap/0Harmony.dll",
                "server/start-server.sh",
                "server/stop-server.sh",
                "server/Start server.cmd",
                "server/Stop server.cmd",
                "public-baseline/manifest.json",
                "Install.exe",
                "client/ScriptHookVDotNet.ini",
                "reshade/OGCoop.ini",
                "client/ScriptHookVDotNet.asi",
                "client/ScriptHookVDotNet2.dll",
                "client/ScriptHookVDotNet3.dll",
                "licenses/ScriptHookVDotNet/LICENSE.txt",
                "licenses/ScriptHookVDotNet/THIRD-PARTY-NOTICES.txt",
                "licenses/ScriptHookVDotNet/COPYRIGHT.txt",
                # The runtime ships whole, as Microsoft packs it.
                "server/dotnet/dotnet.exe",
                "server/dotnet/shared/runtime.dll",
                "server/dotnet/unused/readme.txt",
            }
            | set(release.BASELINE_PINNED_HASHES)
        )
        with zipfile.ZipFile(binary) as archive:
            self.assertEqual(set(archive.namelist()), expected)
            for item in archive.infolist():
                self.assertEqual(item.date_time, (1980, 1, 1, 0, 0, 0))
                self.assertFalse(item.is_dir())
            self.assertEqual(
                archive.getinfo("server/start-server.sh").external_attr >> 16, 0o100755
            )
        manifest = json.loads(manifest_path.read_text())
        self.assertEqual(len(manifest["baseline"]["files"]), 9)
        self.assertEqual(
            [record["archive"] for record in manifest["upstream"]],
            [upstream.archive for upstream in release.UPSTREAM],
        )
        self.assertNotIn(str(self.root), manifest_path.read_text())
        for path in (binary, source):
            record = manifest["archives"][path.name]
            self.assertEqual(record["sha256"], hashlib.sha256(path.read_bytes()).hexdigest())
            with zipfile.ZipFile(path) as archive:
                for item in record["files"]:
                    data = archive.read(item["path"])
                    self.assertEqual(
                        (len(data), hashlib.sha256(data).hexdigest()),
                        (item["size"], item["sha256"]),
                    )

    def test_changed_upstream_download_is_rejected_before_writes(self) -> None:
        path = self.root / release.DEPENDENCY_DIRECTORY / release.UPSTREAM[0].archive
        path.write_bytes(path.read_bytes() + b"tampered")
        with self.assertRaisesRegex(release.ReleaseError, "pinned digest"):
            self.prepare()
        self.assert_no_release_outputs()

    def test_missing_installer_is_rejected_before_writes(self) -> None:
        (self.root / release.PUBLIC_INSTALLER).unlink()
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()

    def test_source_allowlist_excludes_private_and_third_party_files(self) -> None:
        forbidden = (
            "Client/PrivateFeature.cs",
            "Checks/Example/Program.PrivateFeature.cs",
            "Client/Assets/Private/base.png",
            "Client/lib/Private.cs",
            "Client/bin/Generated.cs",
            "Client/obj/Generated.cs",
            "Client/settings.ini",
            "References.props",
        )
        for name in forbidden:
            self.put(release.PROJECT / name, "must never be included\n")
        self.put(Path("work/RageCoopServerMac/Settings.xml"), "private server settings\n")
        self.put(Path("outputs/RageCoop-Windows.zip"), b"private full archive")
        _, source, _ = self.prepare()
        with zipfile.ZipFile(source) as archive:
            names = set(archive.namelist())
            self.assertTrue(
                {(release.PROJECT / name).as_posix() for name in release.PROJECT_FILES} <= names
            )
            self.assertTrue({"tools/" + name for name in release.TOOL_FILES} <= names)
            self.assertTrue(
                {"work/PublicBaseline/" + name for name in release.BASELINE_SOURCE_FILES} <= names
            )
            self.assertFalse(
                any((release.PROJECT / name).as_posix() in names for name in forbidden)
            )
            self.assertFalse(any(name.endswith((".dll", ".rpf", ".ytd", ".exe")) for name in names))
            # Only our own settings template and ReShade preset are INI files.
            self.assertEqual(
                {name for name in names if name.endswith(".ini")},
                {source_name for _, _, source_name in release.EXTRA_FILES},
            )
            self.assertTrue({name for _, name in release.REPOSITORY_FILES} <= names)
            self.assertEqual(
                {name for name in names if name.endswith(IMAGES)},
                set(release.SCREENSHOT_FILES),
            )
            self.assertFalse(
                any("PrivateCars" in name or "RageCoopServerMac" in name for name in names)
            )

    def test_checks_and_projects_are_published_as_the_public_profile_builds_them(self) -> None:
        check = (
            "class Example\n{\n#if !PUBLIC_RELEASE\n    void Private() { }\n#else\n    void Public() { }\n#endif\n"
            "#if PUBLIC_RELEASE\n    void AlsoPublic() { }\n#else\n    void AlsoPrivate() { }\n#endif\n"
            "#if NET48\n    void Framework() { }\n#endif\n"
            "    // <private-source>\n    void Guard() { }\n    // </private-source>\n}\n"
        )
        self.put(release.PROJECT / "Checks/Example.cs", check)
        project = (
            "<Project>\n  <PropertyGroup>\n    " + release.PRIVATE_DEFAULT + "\n  </PropertyGroup>\n"
            "  <ItemGroup Condition=\"" + release.PRIVATE_PROJECT_CONDITION + "\">\n    <Compile Include=\"Hidden.cs\" />\n"
            "  </ItemGroup>\n  <ItemGroup>\n    <Compile Remove=\"PrivateFeature.cs\" />\n"
            "    <EmbeddedResource Include=\"a.json\" Condition=\"" + release.PRIVATE_PROJECT_CONDITION + "\" />\n"
            "    <Compile Include=\"Public.cs\" />\n  </ItemGroup>\n</Project>\n"
        )
        self.put(release.PROJECT / "Client/RageCoopAdmin.Client.csproj", project)
        _, source, _ = self.prepare()
        with zipfile.ZipFile(source) as archive:
            published = archive.read((release.PROJECT / "Checks/Example.cs").as_posix()).decode()
            self.assertEqual(
                published,
                "class Example\n{\n    void Public() { }\n    void AlsoPublic() { }\n"
                "#if NET48\n    void Framework() { }\n#endif\n}\n",
            )
            published_project = archive.read((release.PROJECT / "Client/RageCoopAdmin.Client.csproj").as_posix()).decode()
            self.assertIn(release.PUBLIC_DEFAULT, published_project)
            self.assertNotIn("Hidden.cs", published_project)
            self.assertNotIn("PrivateFeature", published_project)
            self.assertNotIn(release.PRIVATE_PROJECT_CONDITION, published_project)
            self.assertIn("Public.cs", published_project)
            props = archive.read((release.PROJECT / "Directory.Build.props").as_posix()).decode()
            self.assertIn(release.PUBLIC_DEFAULT, props)
            # Product sources are published unchanged.
            self.assertEqual(
                archive.read((release.PROJECT / "Client/Example.cs").as_posix()),
                (self.root / release.PROJECT / "Client/Example.cs").read_bytes(),
            )

    def test_private_blocks_in_product_sources_are_rejected(self) -> None:
        for content in (
            "#if !PUBLIC_RELEASE\nclass Hidden { }\n#endif\n",
            "#if  !PUBLIC_RELEASE\nclass Hidden { }\n#endif\n",
            "#if (!PUBLIC_RELEASE)\nclass Hidden { }\n#endif\n",
            "#if PUBLIC_RELEASE\nclass Shown { }\n#else\nclass Hidden { }\n#endif\n",
            "#if DEBUG\nclass A { }\n#elif !PUBLIC_RELEASE\nclass Hidden { }\n#endif\n",
        ):
            with self.subTest(content=content):
                self.put(release.PROJECT / "Client/Example.cs", content)
                with self.assertRaisesRegex(release.ReleaseError, "private file"):
                    self.prepare()
                self.assert_no_release_outputs()

    def test_public_only_blocks_in_product_sources_are_kept(self) -> None:
        content = "#if PUBLIC_RELEASE\nclass Shown { }\n#endif\n#if DEBUG\nclass A { }\n#else\nclass B { }\n#endif\n"
        self.put(release.PROJECT / "Client/Example.cs", content)
        _, source, _ = self.prepare()
        with zipfile.ZipFile(source) as archive:
            self.assertEqual(archive.read((release.PROJECT / "Client/Example.cs").as_posix()).decode(), content)

    def test_private_terms_are_rejected_in_text_and_own_binaries(self) -> None:
        for name, content in (
            ("Shared/Example.cs", "// mentions PrivateFeature\n"),
            ("Shared/Example.cs", "var code = \"privcar7\";\n"),
            ("Shared/Example.cs", "var id = \"private-car\";\n"),
        ):
            with self.subTest(content=content):
                self.put(release.PROJECT / name, content)
                with self.assertRaisesRegex(release.ReleaseError, "private term"):
                    self.prepare()
                self.assert_no_release_outputs()
        self.put(release.PROJECT / "Shared/Example.cs", "// a1privcar7b2 is a digest; sharedcar is a shared word\n")
        self.prepare()
        self.put(
            release.PUBLIC_BUILD / "EarlyClient/Release/net48/OGCoop.EarlyClient.dll",
            "PrivateFeature".encode("utf-16-le"),
        )
        with self.assertRaisesRegex(release.ReleaseError, "private term"):
            self.prepare()

    def test_gallery_is_exact_in_both_archives_and_extra_images_are_excluded(self) -> None:
        self.put(Path("publication/screenshots/private-transport.png"), b"not selected")
        self.put(Path("publication/screenshots/unlisted-extra.jpg"), b"not selected")
        binary, source, _ = self.prepare()
        for path in (binary, source):
            with self.subTest(archive=path.name), zipfile.ZipFile(path) as archive:
                self.assertEqual(
                    {name for name in archive.namelist() if name.endswith(IMAGES)},
                    set(release.SCREENSHOT_FILES),
                )
                for name in release.SCREENSHOT_FILES:
                    self.assertEqual(
                        archive.read(name), (self.root / "publication" / name).read_bytes()
                    )

    def test_missing_gallery_image_fails_before_outputs(self) -> None:
        (self.root / "publication" / release.SCREENSHOT_FILES[-1]).unlink()
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()

    def test_repeated_preparation_is_byte_identical(self) -> None:
        paths = self.prepare()
        before = [path.read_bytes() for path in paths]
        for path in (self.root / "publication").iterdir():
            path.touch()
        self.assertEqual(before, [path.read_bytes() for path in self.prepare()])

    def test_hash_mismatch_and_invalid_hash_fail_before_outputs(self) -> None:
        for digest in ("0" * 64, "", "not-a-digest"):
            with self.subTest(digest=digest), self.assertRaises(release.ReleaseError):
                release.prepare_release(self.root, digest)
            self.assert_no_release_outputs()

    def test_missing_required_document_fails_before_outputs(self) -> None:
        (self.root / "publication/LICENSE").unlink()
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()

    def test_nonempty_public_catalog_is_rejected_before_outputs(self) -> None:
        self.put(
            release.PROJECT / "Client/Assets/PublicVehicleCatalog.json",
            '{"version":1,"vehicles":[{"private":true}]}\n',
        )
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()

    def test_baseline_schema_paths_and_hashes_are_strict(self) -> None:
        path = self.root / release.BASELINE_DIRECTORY / "manifest.json"
        original = path.read_text()
        for field, value in (
            ("path", "../../private.dll"),
            ("sha256", "0" * 64),
            ("sourceSha256", "1" * 64),
            ("size", True),
        ):
            with self.subTest(field=field):
                document = json.loads(original)
                document["files"][0][field] = value
                path.write_text(json.dumps(document))
                with self.assertRaises(release.ReleaseError):
                    self.prepare()
                self.assert_no_release_outputs()
        for mutation in ("extra", "missing", "duplicate"):
            with self.subTest(mutation=mutation):
                document = json.loads(original)
                if mutation == "extra":
                    document["unexpected"] = True
                elif mutation == "missing":
                    document["files"].pop()
                else:
                    document["files"][1] = document["files"][0]
                path.write_text(json.dumps(document))
                with self.assertRaises(release.ReleaseError):
                    self.prepare()
                self.assert_no_release_outputs()

    def test_missing_or_changed_baseline_file_fails_before_outputs(self) -> None:
        path = self.root / release.BASELINE_DIRECTORY / next(iter(release.BASELINE_PINNED_HASHES))
        path.write_bytes(b"changed baseline bytes")
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()
        path.unlink()
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()

    def test_selected_symlink_and_symbolic_source_directory_are_rejected(self) -> None:
        document = self.root / "publication/README.md"
        document.unlink()
        document.symlink_to(self.root / "publication/README.ru.md")
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()
        document.unlink()
        self.put(Path("publication/README.md"), "restored public document\n")
        (self.root / release.PROJECT / "Shared/linked").symlink_to(
            self.root / release.PROJECT / "Client", target_is_directory=True
        )
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()

    def test_private_absolute_paths_in_selected_text_are_rejected(self) -> None:
        for text in (
            "/" + "Users/alice/private/file",
            "C:" + "\\Users\\alice\\private\\file",
            str(self.root / "private"),
        ):
            with self.subTest(text=text):
                self.put(release.PROJECT / "Shared/Example.cs", text)
                with self.assertRaises(release.ReleaseError):
                    self.prepare()
                self.assert_no_release_outputs()

    def test_shipped_tools_pass_their_own_path_scan(self) -> None:
        # The source archive ships the packer, so its own patterns and examples
        # must not look like a personal path.
        tools = Path(__file__).resolve().parent
        for name in release.TOOL_FILES:
            with self.subTest(name=name):
                source = tools / name
                digest = hashlib.sha256(source.read_bytes()).hexdigest()
                release.reject_private_paths(source, "tools/" + name, text=True, root=self.root, sha256=digest)

    def test_public_client_override_uses_only_the_attested_file(self) -> None:
        rebuilt = self.put(Path("rebuilt/Client.dll"), b"rebuilt public fixture")
        digest = hashlib.sha256(rebuilt.read_bytes()).hexdigest()
        paths = release.prepare_release(self.root, digest, rebuilt)
        with zipfile.ZipFile(paths[0]) as archive:
            self.assertEqual(
                archive.read("server/Resources/Client/OGCoop/RageCoopAdmin.Client.dll"),
                rebuilt.read_bytes(),
            )

    def test_private_binary_paths_are_rejected_before_outputs(self) -> None:
        samples = (
            ("/" + "Users/alice/private/client.pdb").encode(),
            ("C:" + "\\Users\\alice\\private\\client.pdb").encode(),
            ("/" + "home/alice/private/client.pdb").encode("utf-16-le"),
            ("C:" + "\\\\Users\\\\alice\\\\private").encode(),
            ("\\/" + "Users\\/alice\\/private").encode(),
        )
        for sample in samples:
            with self.subTest(sample=sample):
                self.put(
                    release.PUBLIC_BUILD / "EarlyClient/Release/net48/OGCoop.EarlyClient.dll",
                    b"MZ" + b"x" * (release.CHUNK_SIZE - 5) + sample,
                )
                with self.assertRaises(release.ReleaseError):
                    self.prepare()
                self.assert_no_release_outputs()

    def test_utf16_path_near_the_end_of_a_binary_is_found(self) -> None:
        # in UTF-16 the scanned text is half the bytes: the carried tail lost such a path
        path = ("/" + "Users/alice/private/client.pdb").encode("utf-16-le")
        filler = "y".encode("utf-16-le") * 1500
        self.put(
            release.PUBLIC_BUILD / "EarlyClient/Release/net48/OGCoop.EarlyClient.dll",
            b"MZ" + b"x" * (release.CHUNK_SIZE - 2 - len(path) - len(filler)) + path + filler,
        )
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()

    def test_upstream_ci_exception_requires_a_pinned_harmony_digest(self) -> None:
        data = b"MZ" + release.UPSTREAM_CI_PREFIX + b"Harmony/build/0Harmony.pdb\x00"
        self.put(release.PUBLIC_BUILD / "EarlyClient/Release/net48/0Harmony.dll", data)
        with self.assertRaises(release.ReleaseError):
            self.prepare()
        self.assert_no_release_outputs()
        digest = hashlib.sha256(data).hexdigest()
        # The fixture substitutes one attested upstream binary, ordinary tests
        # never need game files or a download of the real dependency.
        with patch.object(release, "PINNED_HARMONY_SHA256", frozenset((digest,))):
            _, _, manifest_path = self.prepare()
        manifest = json.loads(manifest_path.read_text())
        records = manifest["archives"][release.ARCHIVE_NAMES[0]]["files"]
        exceptions = [item for item in records if item.get("upstreamCiBuildPaths")]
        self.assertEqual(len(exceptions), 2)
        self.assertTrue(all(item["sha256"] == digest for item in exceptions))

    def test_even_pinned_harmony_cannot_contain_personal_paths(self) -> None:
        data = b"MZ/" + b"Users/alice/private/0Harmony.pdb\x00"
        self.put(release.PUBLIC_BUILD / "EarlyClient/Release/net48/0Harmony.dll", data)
        digest = hashlib.sha256(data).hexdigest()
        with (
            patch.object(release, "PINNED_HARMONY_SHA256", frozenset((digest,))),
            self.assertRaises(release.ReleaseError),
        ):
            self.prepare()
        self.assert_no_release_outputs()

    def test_interrupted_preparation_preserves_existing_outputs_and_cleans_staging(self) -> None:
        paths = self.prepare()
        before = [path.read_bytes() for path in paths]
        original = release.write_archive
        calls = 0

        def fail_second_archive(path: Path, entries: list[release.Entry]) -> None:
            nonlocal calls
            calls += 1
            if calls == 2:
                raise OSError("isolated second archive failure")
            original(path, entries)

        with (
            patch.object(release, "write_archive", side_effect=fail_second_archive),
            self.assertRaises(OSError),
        ):
            self.prepare()
        self.assertEqual(before, [path.read_bytes() for path in paths])
        self.assertEqual(list((self.root / release.OUTPUT_DIRECTORY).glob(".*.tmp")), [])

    def test_input_change_after_validation_is_rejected_before_replacement(self) -> None:
        original = release.write_archive

        def change_document(path: Path, entries: list[release.Entry]) -> None:
            self.put(Path("publication/README.md"), "changed during preparation\n")
            original(path, entries)

        with (
            patch.object(release, "write_archive", side_effect=change_document),
            self.assertRaises(release.ReleaseError),
        ):
            self.prepare()
        self.assert_no_release_outputs()


if __name__ == "__main__":
    unittest.main()
