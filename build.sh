#!/usr/bin/env bash
# Builds NoteSpin for Linux into ./dist/NoteSpin-linux
# Needs the .NET 10 SDK on the build machine; at runtime uses .NET 10 runtime + system GTK3, WebKit2GTK-4.1, and libsqlite3.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/dist/NoteSpin-linux"

sdk_install_hint() {
  local id=""
  if [ -r /etc/os-release ]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    id="${ID:-} ${ID_LIKE:-}"
  fi
  case " $id " in
    *" fedora "*|*" rhel "*|*" centos "*) echo "  sudo dnf install dotnet-sdk-10.0" ;;
    *" ubuntu "*|*" debian "*|*" mint "*|*" pop "*) echo "  sudo apt update && sudo apt install dotnet-sdk-10.0" ;;
    *" arch "*|*" manjaro "*) echo "  sudo pacman -S dotnet-sdk" ;;
    *" opensuse "*|*" suse "*) echo "  sudo zypper install dotnet-sdk-10.0" ;;
    *) echo "  Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0" ;;
  esac
}

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "Error: NoteSpin needs the .NET 10 SDK to build on Linux." >&2
  echo "Install it with:" >&2
  sdk_install_hint >&2
  exit 1
fi

rm -rf "$OUT"
dotnet publish "$HERE/NoteSpin.csproj" -c Release -o "$OUT" --nologo

rm -f "$OUT"/*.pdb "$OUT"/*.xml "$OUT/NoteSpin.dll.config"

# Wrap the native apphost with a lightweight launcher that checks for the .NET 10 runtime
# and shows a clear install command if it is missing.
mv "$OUT/NoteSpin" "$OUT/NoteSpin.bin"
cat > "$OUT/NoteSpin" <<'LAUNCHER'
#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"

has_dotnet10() {
  local d=""
  if command -v dotnet >/dev/null 2>&1; then
    d="$(command -v dotnet)"
  elif [ -n "${DOTNET_ROOT:-}" ] && [ -x "$DOTNET_ROOT/dotnet" ]; then
    d="$DOTNET_ROOT/dotnet"
  else
    return 1
  fi
  "$d" --list-runtimes 2>/dev/null | grep -q '^Microsoft\.NETCore\.App 10\.'
}

if ! has_dotnet10; then
  id=""
  if [ -r /etc/os-release ]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    id="${ID:-} ${ID_LIKE:-}"
  fi
  case " $id " in
    *" fedora "*|*" rhel "*|*" centos "*) cmd="sudo dnf install dotnet-runtime-10.0" ;;
    *" ubuntu "*|*" debian "*|*" mint "*|*" pop "*) cmd="sudo apt update && sudo apt install dotnet-runtime-10.0" ;;
    *" arch "*|*" manjaro "*) cmd="sudo pacman -S dotnet-runtime" ;;
    *" opensuse "*|*" suse "*) cmd="sudo zypper install dotnet-runtime-10.0" ;;
    *) cmd="https://dotnet.microsoft.com/download/dotnet/10.0" ;;
  esac

  msg="NoteSpin needs the .NET 10 Runtime to run on Linux.

Install it with:
  $cmd"

  echo "$msg" >&2
  if [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
    if command -v zenity >/dev/null 2>&1; then
      zenity --error --title="NoteSpin" --width=440 --text="$msg" 2>/dev/null || true
    elif command -v kdialog >/dev/null 2>&1; then
      kdialog --title="NoteSpin" --error "$msg" 2>/dev/null || true
    fi
  fi
  exit 1
fi

exec "$HERE/NoteSpin.bin" "$@"
LAUNCHER
chmod +x "$OUT/NoteSpin" "$OUT/NoteSpin.bin"

echo "Built Linux NoteSpin -> $OUT"
ls -lh "$OUT"
