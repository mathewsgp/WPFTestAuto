"""Tests for the timestamp-aware DLL staging feature in RuntimeInjector.

These tests do NOT spawn real processes. They exercise:
  - _stage_dll_set: copies newer sources, skips equal/older destinations,
    skips missing sources, and reports only the actually-copied set.
  - _detect_target_framework: classifies the dummy exes by content.
  - stage_dlls / unstage_dlls: end-to-end against a fake AUT folder.
"""
import os
import sys
import time
import tempfile
import shutil
from pathlib import Path

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "api"))

from runtime_injector import RuntimeInjector


def _make_fake_dll(path: Path, mtime: float = None) -> None:
    """Write a small placeholder file with a controlled mtime."""
    path.write_bytes(b"FAKE_DLL")
    if mtime is not None:
        os.utime(path, (mtime, mtime))


def test_stage_dll_set_copies_newer_source():
    with tempfile.TemporaryDirectory() as td:
        src = Path(td) / "src"
        dst = Path(td) / "dst"
        src.mkdir()
        dst.mkdir()
        old = time.time() - 100
        new = time.time()
        _make_fake_dll(src / "A.dll", mtime=new)
        _make_fake_dll(dst / "A.dll", mtime=old)

        injector = RuntimeInjector()
        copied = injector._stage_dll_set(aut_root=dst, target_dir=dst, src_dir=src, dll_names=["A.dll"])

        assert copied == ["A.dll"], f"expected A.dll in copy list, got {copied}"
        assert dst.joinpath("A.dll").read_bytes() == b"FAKE_DLL"


def test_stage_dll_set_skips_when_destination_is_newer_or_equal():
    with tempfile.TemporaryDirectory() as td:
        src = Path(td) / "src"
        dst = Path(td) / "dst"
        src.mkdir()
        dst.mkdir()
        now = time.time()
        older = now - 100
        # src older than dst -> skip
        _make_fake_dll(src / "A.dll", mtime=older)
        _make_fake_dll(dst / "A.dll", mtime=now)
        injector = RuntimeInjector()
        copied = injector._stage_dll_set(aut_root=dst, target_dir=dst, src_dir=src, dll_names=["A.dll"])
        assert copied == [], f"expected no copy, got {copied}"
        # equal mtime -> skip
        _make_fake_dll(src / "A.dll", mtime=now)
        _make_fake_dll(dst / "A.dll", mtime=now)
        copied = injector._stage_dll_set(aut_root=dst, target_dir=dst, src_dir=src, dll_names=["A.dll"])
        assert copied == [], f"expected no copy on equal mtime, got {copied}"


def test_stage_dll_set_skips_missing_source():
    with tempfile.TemporaryDirectory() as td:
        src = Path(td) / "src"
        dst = Path(td) / "dst"
        src.mkdir()
        dst.mkdir()
        injector = RuntimeInjector()
        copied = injector._stage_dll_set(aut_root=dst, target_dir=dst, src_dir=src, dll_names=["Nonexistent.dll"])
        assert copied == []


def test_stage_dll_set_only_copies_listed():
    with tempfile.TemporaryDirectory() as td:
        src = Path(td) / "src"
        dst = Path(td) / "dst"
        src.mkdir()
        dst.mkdir()
        _make_fake_dll(src / "A.dll", mtime=time.time())
        _make_fake_dll(src / "B.dll", mtime=time.time())
        injector = RuntimeInjector()
        copied = injector._stage_dll_set(aut_root=dst, target_dir=dst, src_dir=src, dll_names=["A.dll"])
        assert copied == ["A.dll"]
        assert (dst / "A.dll").exists()
        assert not (dst / "B.dll").exists(), "B.dll should not be copied"


def test_unstage_dlls_removes_only_listed():
    with tempfile.TemporaryDirectory() as td:
        target = Path(td)
        _make_fake_dll(target / "WpfSpyAgent.dll")
        _make_fake_dll(target / "UserFile.dll")
        injector = RuntimeInjector()
        injector.unstage_dlls(str(target / "fake.exe"), ["WpfSpyAgent.dll"])
        assert not (target / "WpfSpyAgent.dll").exists()
        assert (target / "UserFile.dll").exists(), "unstage must not touch user files"


def test_detect_target_framework_modern_vs_framework():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        # Modern .NET app: a sidecar runtimeconfig.json is the strongest signal
        modern_exe = td / "ModernApp.exe"
        modern_exe.write_bytes(b"MZ" + b"\x00" * 100)
        (td / "ModernApp.runtimeconfig.json").write_text("{}")
        # Framework app: no runtimeconfig.json, no coreclr/hostfxr markers
        fw_exe = td / "LegacyApp.exe"
        fw_exe.write_bytes(b"MZ" + b"\x00" * 100)
        injector = RuntimeInjector()
        assert injector._detect_target_framework(str(modern_exe)) == "modern"
        assert injector._detect_target_framework(str(fw_exe)) == "framework"


def test_stage_dlls_end_to_end_modern():
    """Full flow: stage copies the modern agent set next to a dummy AUT exe."""
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        framework_dir = td / "framework"
        aut_dir = td / "aut"
        framework_dir.mkdir()
        aut_dir.mkdir()
        # Pretend the framework bin has the modern agent set
        for name in ("WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"):
            _make_fake_dll(framework_dir / name, mtime=time.time())
        # Pretend a modern AUT exe with a runtimeconfig.json sidecar
        aut_exe = aut_dir / "MyApp.exe"
        aut_exe.write_bytes(b"MZ" + b"\x00" * 100)
        (aut_dir / "MyApp.runtimeconfig.json").write_text("{}")

        # Point RuntimeInjector at our fake source. No framework source dir is
        # present in this test, so only the modern pair will be staged.
        injector = RuntimeInjector(startup_hook_path=str(framework_dir / "WpfSpyAgent.StartupHook.dll"))
        # Force framework-source search to return None so the real
        # WpfSpyAgent.FrameworkHook/bin/Debug/net461/ on the test machine
        # is not picked up.
        injector._find_framework_agent_dir = lambda: None
        # Older staged copies -> they should be overwritten
        for name in ("WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"):
            _make_fake_dll(aut_dir / name, mtime=time.time() - 1000)

        copied = injector.stage_dlls(str(aut_exe))
        assert set(copied) == {"WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"}
        # Idempotency: second call with unchanged sources should be a no-op
        copied_again = injector.stage_dlls(str(aut_exe))
        assert copied_again == [], f"second stage should be a no-op, got {copied_again}"
        # Cleanup only removes what we staged
        injector.unstage_dlls(str(aut_exe), copied)
        assert not (aut_dir / "WpfSpyAgent.dll").exists()
        assert not (aut_dir / "WpfSpyAgent.StartupHook.dll").exists()


def test_stage_dlls_modern_target_only_stages_modern():
    """When the target is a modern (.NET Core/5+) process, only the modern pair
    is staged at the AUT root. The net461\\ subfolder must NOT be created."""
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        modern_dir = td / "framework_modern"
        fw_dir = td / "framework_net461"
        aut_dir = td / "aut"
        for d in (modern_dir, fw_dir, aut_dir):
            d.mkdir()
        for name in ("WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"):
            _make_fake_dll(modern_dir / name, mtime=time.time())
        # Framework sources present but should NOT be used
        for name in ("WpfSpyAgent.dll", "Newtonsoft.Json.dll", "WpfSpyAgent.FrameworkHook.dll"):
            _make_fake_dll(fw_dir / name, mtime=time.time())

        aut_exe = aut_dir / "ModernApp.exe"
        aut_exe.write_bytes(b"MZ" + b"\x00" * 100)
        (aut_dir / "ModernApp.runtimeconfig.json").write_text("{}")

        injector = RuntimeInjector(startup_hook_path=str(modern_dir / "WpfSpyAgent.StartupHook.dll"))
        injector._find_framework_agent_dir = lambda: fw_dir
        # Force "modern" detection regardless of test host environment
        injector._detect_target_framework = lambda _p: "modern"

        copied = injector.stage_dlls(str(aut_exe))
        assert set(copied) == {"WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"}
        assert (aut_dir / "WpfSpyAgent.dll").exists()
        assert (aut_dir / "WpfSpyAgent.StartupHook.dll").exists()
        assert not (aut_dir / "net461").exists(), \
            "net461\\ subfolder must NOT be created when target is modern"


def test_stage_dlls_framework_target_only_stages_net461():
    """When the target is a .NET Framework 4.x process, only the Framework trio
    is staged under <AUT>\\net461\\. The modern pair must NOT be at the root."""
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        modern_dir = td / "framework_modern"
        fw_dir = td / "framework_net461"
        aut_dir = td / "aut"
        for d in (modern_dir, fw_dir, aut_dir):
            d.mkdir()
        # Both sources available, but only Framework should be staged
        for name in ("WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"):
            _make_fake_dll(modern_dir / name, mtime=time.time())
        for name in ("WpfSpyAgent.dll", "Newtonsoft.Json.dll", "WpfSpyAgent.FrameworkHook.dll"):
            _make_fake_dll(fw_dir / name, mtime=time.time())

        aut_exe = aut_dir / "LegacyApp.exe"
        aut_exe.write_bytes(b"MZ" + b"\x00" * 100)
        # No runtimeconfig.json -> PE-header detection would say "framework"
        # but we also override directly to be deterministic.
        injector = RuntimeInjector(startup_hook_path=str(modern_dir / "WpfSpyAgent.StartupHook.dll"))
        injector._find_framework_agent_dir = lambda: fw_dir
        injector._detect_target_framework = lambda _p: "framework"

        copied = injector.stage_dlls(str(aut_exe))
        assert set(copied) == {
            "net461/WpfSpyAgent.dll",
            "net461/WpfSpyAgent.FrameworkHook.dll",
            "net461/Newtonsoft.Json.dll",
        }, f"unexpected copy list: {copied}"
        # Modern pair must NOT be at the root
        assert not (aut_dir / "WpfSpyAgent.dll").exists(), \
            "WpfSpyAgent.dll must not be at the AUT root when target is Framework"
        assert not (aut_dir / "WpfSpyAgent.StartupHook.dll").exists(), \
            "StartupHook.dll must not be at the AUT root when target is Framework"
        # Framework trio under net461\
        assert (aut_dir / "net461" / "WpfSpyAgent.dll").exists()
        assert (aut_dir / "net461" / "WpfSpyAgent.FrameworkHook.dll").exists()
        assert (aut_dir / "net461" / "Newtonsoft.Json.dll").exists()


def test_stage_dlls_framework_target_with_no_source_is_noop():
    """When the target is Framework but no Framework build is on disk, the
    stage call is a safe no-op (returns empty list, creates no net461\\)."""
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        aut_dir = td / "aut"
        aut_dir.mkdir()
        aut_exe = aut_dir / "Legacy.exe"
        aut_exe.write_bytes(b"MZ" + b"\x00" * 100)

        injector = RuntimeInjector(startup_hook_path=None)
        injector._find_framework_agent_dir = lambda: None
        injector._detect_target_framework = lambda _p: "framework"

        copied = injector.stage_dlls(str(aut_exe))
        assert copied == []
        assert not (aut_dir / "net461").exists()


def test_detect_target_framework_by_pid_falls_back_to_modern():
    """Calling _detect_target_framework_by_pid against the test process (which
    is a normal Python process — neither coreclr.dll nor mscoree.dll is loaded
    into it) must fall back to 'modern'."""
    injector = RuntimeInjector()
    pid = os.getpid()
    result = injector._detect_target_framework_by_pid(pid)
    assert result in ("modern", "framework")
    # For a CPython process, no .NET runtime is loaded, so it must be 'modern'.
    assert result == "modern", f"expected 'modern' fallback, got {result!r}"


def test_find_framework_agent_dir_resolves_against_real_repo():
    """Regression: the framework source dir lookup must resolve to the
    actual <repo>\\bin\\Debug\\net461 build output on this machine. The
    previous version had a wrong relative path that pointed to
    <repo>\\bin\\Debug\\net9.0-windows\\bin\\Debug\\net461 (which does
    not exist), causing staging to silently no-op."""
    injector = RuntimeInjector()
    found = injector._find_framework_agent_dir()
    repo_root = Path(__file__).resolve().parent.parent
    expected = repo_root / "bin" / "Debug" / "net461"
    assert expected.is_dir(), f"sanity: expected dir does not exist: {expected}"
    assert found is not None, f"_find_framework_agent_dir returned None; expected {expected}"
    assert found.resolve() == expected.resolve(), (
        f"resolved framework source dir mismatch:\n"
        f"  found:    {found.resolve()}\n"
        f"  expected: {expected.resolve()}"
    )
    # And the framework build must be there with the right shape.
    assert (found / "WpfSpyAgent.dll").exists()
    assert (found / "WpfSpyAgent.FrameworkHook.dll").exists()
    assert (found / "Newtonsoft.Json.dll").exists()


def test_stage_dlls_with_target_pid_uses_process_modules():
    """When target_pid is supplied, staging must use process-module detection
    rather than the PE-header heuristic. This test invokes stage_dlls against
    the test process's PID (Python: no .NET runtime) and confirms that the
    modern-only path is taken."""
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        modern_dir = td / "framework_modern"
        aut_dir = td / "aut"
        for d in (modern_dir, aut_dir):
            d.mkdir()
        for name in ("WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"):
            _make_fake_dll(modern_dir / name, mtime=time.time())
        aut_exe = aut_dir / "SomeApp.exe"
        aut_exe.write_bytes(b"X")  # No MZ header — would otherwise classify as "framework"

        injector = RuntimeInjector(startup_hook_path=str(modern_dir / "WpfSpyAgent.StartupHook.dll"))
        # Pass the test process PID (Python: no coreclr/mscoree -> "modern")
        copied = injector.stage_dlls(str(aut_exe), target_pid=os.getpid())
        assert set(copied) == {"WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"}
        assert (aut_dir / "WpfSpyAgent.dll").exists()
        assert (aut_dir / "WpfSpyAgent.StartupHook.dll").exists()
        assert not (aut_dir / "net461").exists()


def test_unstage_dlls_removes_net461_subfolder_when_empty():
    """unstage_dlls should clean up the net461\\ subfolder when it becomes empty."""
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        aut_dir = td / "aut"
        aut_dir.mkdir()
        net461 = aut_dir / "net461"
        net461.mkdir()
        # Pretend we staged these earlier
        (net461 / "WpfSpyAgent.dll").write_bytes(b"X")
        (net461 / "Newtonsoft.Json.dll").write_bytes(b"X")
        aut_exe = aut_dir / "SomeApp.exe"
        aut_exe.write_bytes(b"X")

        injector = RuntimeInjector()
        injector.unstage_dlls(str(aut_exe), [
            "net461/WpfSpyAgent.dll",
            "net461/Newtonsoft.Json.dll",
        ])
        assert not (net461 / "WpfSpyAgent.dll").exists()
        assert not (net461 / "Newtonsoft.Json.dll").exists()
        assert not net461.exists(), f"net461\\ subfolder should be removed when empty, but still exists"


def test_unstage_dlls_keeps_net461_subfolder_with_user_files():
    """unstage_dlls should NOT delete net461\\ if it still contains non-staged files."""
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        aut_dir = td / "aut"
        aut_dir.mkdir()
        net461 = aut_dir / "net461"
        net461.mkdir()
        (net461 / "WpfSpyAgent.dll").write_bytes(b"X")  # staged by us
        (net461 / "UserPlugin.dll").write_bytes(b"X")   # user file — must not be removed
        aut_exe = aut_dir / "SomeApp.exe"
        aut_exe.write_bytes(b"X")

        injector = RuntimeInjector()
        injector.unstage_dlls(str(aut_exe), ["net461/WpfSpyAgent.dll"])
        assert not (net461 / "WpfSpyAgent.dll").exists()
        assert (net461 / "UserPlugin.dll").exists(), "unstage must not touch user files"
        assert net461.is_dir(), "net461\\ subfolder must remain because user file still there"


def test_stage_dlls_works_when_no_framework_source_available():
    """When no .NET Framework build is present in the framework tree, only the
    modern pair is staged. This is the common case for a typical workstation."""
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        modern_dir = td / "framework_modern"
        aut_dir = td / "aut"
        for d in (modern_dir, aut_dir):
            d.mkdir()
        for name in ("WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"):
            _make_fake_dll(modern_dir / name, mtime=time.time())
        aut_exe = aut_dir / "App.exe"
        aut_exe.write_bytes(b"MZ" + b"\x00" * 100)
        (aut_dir / "App.runtimeconfig.json").write_text("{}")

        injector = RuntimeInjector(startup_hook_path=str(modern_dir / "WpfSpyAgent.StartupHook.dll"))
        # Force the framework-source search to return None regardless of what
        # happens to exist on the test machine.
        injector._find_framework_agent_dir = lambda: None

        copied = injector.stage_dlls(str(aut_exe))
        assert set(copied) == {"WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"}
        assert not (aut_dir / "net461").exists(), "no net461\\ subfolder should be created when no source"


if __name__ == "__main__":
    test_stage_dll_set_copies_newer_source()
    test_stage_dll_set_skips_when_destination_is_newer_or_equal()
    test_stage_dll_set_skips_missing_source()
    test_stage_dll_set_only_copies_listed()
    test_unstage_dlls_removes_only_listed()
    test_detect_target_framework_modern_vs_framework()
    test_stage_dlls_end_to_end_modern()
    test_stage_dlls_modern_target_only_stages_modern()
    test_stage_dlls_framework_target_only_stages_net461()
    test_stage_dlls_framework_target_with_no_source_is_noop()
    test_unstage_dlls_removes_net461_subfolder_when_empty()
    test_unstage_dlls_keeps_net461_subfolder_with_user_files()
    test_stage_dlls_works_when_no_framework_source_available()
    test_detect_target_framework_by_pid_falls_back_to_modern()
    test_stage_dlls_with_target_pid_uses_process_modules()
    test_find_framework_agent_dir_resolves_against_real_repo()
    print("\nAll DLL staging tests passed.")
