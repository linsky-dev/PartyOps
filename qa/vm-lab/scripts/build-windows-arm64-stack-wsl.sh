#!/usr/bin/env bash
# 仅构建本地基础组件；由 Windows 控制器先检查磁盘、停机与宿主内存，不创建/启动 Guest。
set -euo pipefail
component=${1:?component required}
jobs=${2:-2}
[[ "$jobs" =~ ^[12]$ ]] || { echo 'BUILD_JOBS_MUST_BE_ONE_OR_TWO' >&2; exit 1; }
root=/mnt/e/codex/PartyOps/.partyops-vm-lab/tools/windows-arm64-stack
config=/mnt/e/codex/PartyOps/.publish-github/qa/vm-lab/config/windows-arm64-stack.json
case "$component" in libtpms|swtpm|qemu|edk2) ;; *) echo 'UNKNOWN_BUILD_COMPONENT' >&2; exit 1 ;; esac
source_dir="$root/sources/$component"
expected=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sources"][sys.argv[2]]["commit"])' "$config" "$component")
test "$(git -C "$source_dir" rev-parse HEAD)" = "$expected"
test -z "$(git -C "$source_dir" diff --name-only HEAD)"
test "$(uname -m)" = x86_64
prefix="$root/prefix"
mkdir -p "$prefix" "$root/tmp" "$root/build/$component" "$root/firmware"
export TMPDIR="$root/tmp"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
export LD_LIBRARY_PATH="$prefix/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export GIT_TERMINAL_PROMPT=0
cd "$source_dir"
case "$component" in
  libtpms)
    ./autogen.sh --prefix="$prefix" --libdir="$prefix/lib" --with-openssl --with-tpm2
    make -j"$jobs"
    make check
    make install
    ;;
  swtpm)
    ./autogen.sh --prefix="$prefix" --libdir="$prefix/lib" --with-openssl --without-selinux --without-cuse
    make -j"$jobs"
    make check
    make install
    "$prefix/bin/swtpm" --version
    ;;
  qemu)
    cd "$root/build/qemu"
    "$source_dir/configure" --prefix="$prefix" --target-list=aarch64-softmmu --enable-tcg --enable-tpm --enable-slirp --disable-kvm --disable-docs --disable-gtk --disable-sdl --disable-werror
    ninja -j"$jobs"
    ninja install
    "$prefix/bin/qemu-system-aarch64" --version
    "$prefix/bin/qemu-system-aarch64" -tpmdev help
    ;;
  edk2)
    git -c credential.helper= submodule update --init --recursive --depth 1
    git submodule status --recursive
    make -C BaseTools -j"$jobs"
    export WORKSPACE="$source_dir"
    export EDK_TOOLS_PATH="$source_dir/BaseTools"
    export GCC_AARCH64_PREFIX=aarch64-linux-gnu-
    # edksetup 读取可选变量；仅在载入官方设置时允许未设变量。
    set +u
    source edksetup.sh
    set -u
    build -a AARCH64 -b RELEASE -t GCC -p ArmVirtPkg/ArmVirtQemu.dsc -n "$jobs" \
      -D SECURE_BOOT_ENABLE=TRUE -D TPM2_ENABLE=TRUE -D TPM2_CONFIG_ENABLE=TRUE
    cp Build/ArmVirtQemu-AArch64/RELEASE_GCC/FV/QEMU_EFI.fd "$root/firmware/QEMU_EFI.fd"
    cp Build/ArmVirtQemu-AArch64/RELEASE_GCC/FV/QEMU_VARS.fd "$root/firmware/QEMU_VARS.fd"
    ;;
esac
printf 'COMPONENT_BUILD_FINISHED:%s\n' "$component"
