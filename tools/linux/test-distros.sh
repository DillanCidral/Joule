#!/usr/bin/env bash
# Runs joule-cli inside the userland of several Linux distributions, on this
# machine's kernel, /sys and /dev/hidraw* (bubblewrap, no root, no VM: a
# Docker Desktop VM would not see the host's devices).
#
#   tools/linux/test-distros.sh            all distributions below
#   tools/linux/test-distros.sh ubuntu;noble
#
# Needs: bwrap, curl, xz, the .NET 8 SDK. Rootfs images come from
# images.linuxcontainers.org and are cached in $JOULE_DISTROS (default
# ~/.cache/joule-distros).
set -euo pipefail

repo=$(cd "$(dirname "$0")/../.." && pwd)
cache=${JOULE_DISTROS:-$HOME/.cache/joule-distros}
distros=("$@")
[ ${#distros[@]} -gt 0 ] || distros=("ubuntu;noble" "ubuntu;jammy" "debian;bookworm" "fedora;44" "opensuse;16.0" "alpine;3.22")

for cmd in bwrap curl xz; do command -v "$cmd" >/dev/null || { echo "missing: $cmd" >&2; exit 1; }; done
mkdir -p "$cache"

for rid in linux-x64 linux-musl-x64; do
  dotnet publish "$repo/src/JouleCli" -c Release -r "$rid" --self-contained -p:PublishSingleFile=true -o "$cache/bin/$rid" >/dev/null
done

index=$(curl -fsSL https://images.linuxcontainers.org/meta/1.0/index-system)
hidraw=(); for h in /dev/hidraw*; do [ -e "$h" ] && hidraw+=(--dev-bind "$h" "$h"); done

run() { # run <rootfs> <binary> <LANG> [args...]
  local root=$1 bin=$2 lang=$3; shift 3
  bwrap --bind "$root" / --proc /proc --dev /dev "${hidraw[@]}" --ro-bind /sys /sys --tmpfs /tmp \
    --ro-bind "$bin" /joule-cli --setenv HOME /tmp --setenv LANG "$lang" --unshare-all --die-with-parent /joule-cli "$@"
}

failed=0
for d in "${distros[@]}"; do
  name=${d//;/-}; root="$cache/$name"
  if [ ! -d "$root/usr" ]; then
    path=$(grep "^$d;amd64;default;" <<<"$index" | cut -d';' -f6 | tail -1)
    [ -n "$path" ] || { echo "== $name: no image"; failed=1; continue; }
    mkdir -p "$root"
    curl -fsSL "https://images.linuxcontainers.org${path}rootfs.tar.xz" | tar -xJ -C "$root" 2>/dev/null || true
    chmod u+w "$root" && touch "$root/joule-cli" # Fedora ships / as 555
    if [[ $d == alpine* ]]; then # .NET on musl needs these two (they are in the Alpine notes of the README)
      bwrap --bind "$root" / --proc /proc --dev /dev --tmpfs /tmp --ro-bind /etc/resolv.conf /etc/resolv.conf \
        --unshare-user --uid 0 --gid 0 --share-net /sbin/apk add --no-cache -q libgcc libstdc++
    fi
  fi
  bin="$cache/bin/linux-x64/joule-cli"; [[ $d == alpine* ]] && bin="$cache/bin/linux-musl-x64/joule-cli"

  echo "== $name"
  if run "$root" "$bin" en_US.UTF-8 --selftest >/dev/null; then echo "   selftest ok"; else echo "   selftest FAILED"; failed=1; fi
  if out=$(run "$root" "$bin" "${JOULE_LANG:-en_US.UTF-8}" 2>&1); then sed 's/^/   /' <<<"$out"; else echo "   reading FAILED: $out"; failed=1; fi
done
exit $failed
