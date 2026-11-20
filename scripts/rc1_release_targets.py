"""PartyOps 1.4.6-rc.1: exactly ten installers and distinct native acceptance targets.

Keep package identity separate from OS verification. A single signed package can
require multiple native install scenarios (e.g. Windows 10 and 11); no scenario
may be dropped or declared satisfied simply because its package exists.
"""

from __future__ import annotations

INSTALLER_VALIDATION_TARGETS: dict[str, tuple[str, ...]] = {
    "windows-x64": ("windows-10-x64", "windows-11-x64"),
    "windows-7-x64": ("windows-7-x64",),
    "windows-7-x86": ("windows-7-x86",),
    "deb-amd64": ("uos-deb-x64",),
    "deb-arm64": ("uos-deb-arm64", "kylin-deb-arm64"),
    "deb-loong64": ("deepin-deb-loong64",),
    "rpm-x86_64": ("openeuler-rpm-x86_64",),
    "rpm-aarch64": ("openeuler-rpm-aarch64",),
    "macos-intel": ("macos-intel",),
    "macos-apple-silicon": ("macos-apple-silicon",),
}


def expected_native_targets() -> dict[str, str]:
    """Map each native environment to the exact installer that must be tested."""

    return {
        environment: installer
        for installer, environments in INSTALLER_VALIDATION_TARGETS.items()
        for environment in environments
    }
