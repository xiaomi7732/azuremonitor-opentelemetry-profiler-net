#!/usr/bin/env bash
# enable-linux-appservice.sh
# Codeless-enable the Azure Monitor Profiler on a Linux Azure App Service (blessed .NET stack).
#
# Linux App Service has no Kudu site-extension gallery and no applicationHost.xdt, so this script does what
# the Windows site extension does, manually: it stages the portable payload under the app's persistent
# /home and sets the three injection environment variables as App Settings (append + de-duplicate, so it
# coexists with the platform Application Insights agent or user-set hooks). Setting App Settings restarts the
# app, after which the profiler activates codelessly.
#
# Dependencies (all cross-platform; no Windows, no .NET SDK required to RUN this):
#   - Azure CLI (az), logged in:  az login
#   - curl
# The payload zip is produced by the repo build (Build-SiteExtension.ps1 -> Out/Linux/AzureMonitorProfiler.<version>.zip).
#
# Usage:
#   ./enable-linux-appservice.sh -g <resource-group> -n <app-name> [--slot <slot>] \
#        [--payload-zip <path>] [--payload-version <version>] [--debug]
#   ./enable-linux-appservice.sh -g <resource-group> -n <app-name> [--slot <slot>] --disable
#
#   --debug   Also set SP_STARTUP_LOG=1 so the injection writes a diagnostic file under
#             /home/LogFiles/AzureMonitorProfiler/ (readable via Kudu / log streaming).

set -euo pipefail

HOSTINGSTARTUP_ASSEMBLY="Azure.Monitor.OpenTelemetry.Profiler.HostingStartup"
REMOTE_BASE="/home/AzureMonitorProfiler"

RG=""; APP=""; SLOT=""; PAYLOAD_ZIP=""; PAYLOAD_VERSION=""; DISABLE=0; DEBUGLOG=0

die() { echo "ERROR: $*" >&2; exit 1; }
usage() { sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    -g|--resource-group) RG="$2"; shift 2;;
    -n|--name) APP="$2"; shift 2;;
    --slot) SLOT="$2"; shift 2;;
    --payload-zip) PAYLOAD_ZIP="$2"; shift 2;;
    --payload-version) PAYLOAD_VERSION="$2"; shift 2;;
    --debug) DEBUGLOG=1; shift;;
    --disable) DISABLE=1; shift;;
    -h|--help) usage 0;;
    *) die "Unknown argument: $1 (see --help)";;
  esac
done

[[ -n "$RG" && -n "$APP" ]] || { echo "Missing -g/--resource-group and/or -n/--name." >&2; usage 1; }
command -v az  >/dev/null 2>&1 || die "Azure CLI 'az' not found. Install it and run 'az login'."
command -v curl >/dev/null 2>&1 || die "'curl' not found."
az account show >/dev/null 2>&1 || die "Not logged in. Run 'az login' (and 'az account set --subscription <id>')."

SLOT_ARGS=(); [[ -n "$SLOT" ]] && SLOT_ARGS=(--slot "$SLOT")

# --- Resolve the SCM (Kudu) host and an AAD token (works even when SCM basic auth is disabled) ---
DEFAULT_HOST=$(az webapp show -g "$RG" -n "$APP" "${SLOT_ARGS[@]}" --query defaultHostName -o tsv) \
  || die "Could not find app '$APP' in resource group '$RG'."
# name.azurewebsites.net -> name.scm.azurewebsites.net (also works for sovereign clouds and slots)
SCM_HOST=$(printf '%s' "$DEFAULT_HOST" | sed 's/\./.scm./')
TOKEN=$(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)

# --- List helpers. DOTNET_STARTUP_HOOKS is delimited by Path.PathSeparator (":" on Linux!), while
#     ASPNETCORE_HOSTINGSTARTUPASSEMBLIES is ";"-delimited on all platforms - so pass the right separator. ---
append_dedup() { # $1 = current list, $2 = value to ensure present, $3 = separator -> prints new list
  local current="$1" value="$2" sep="$3" out="" item
  local IFS="$sep"
  for item in $current; do
    [[ -z "$item" || "$item" == "$value" ]] && continue
    out="${out:+$out$sep}$item"
  done
  out="${out:+$out$sep}$value"
  printf '%s' "$out"
}

remove_from_list() { # $1 = current list, $2 = value to remove, $3 = separator -> prints new list
  local current="$1" value="$2" sep="$3" out="" item
  local IFS="$sep"
  for item in $current; do
    [[ -z "$item" || "$item" == "$value" ]] && continue
    out="${out:+$out$sep}$item"
  done
  printf '%s' "$out"
}

# Rebuild the DOTNET_STARTUP_HOOKS list: drop any prior version of OUR StartupHook (upgrades stage into a new
# /home/AzureMonitorProfiler/<version>/ folder and App Settings persist, so a plain dedup would leave the old
# hook, which runs first and loads the stale payload), then append $2 when non-empty. Pure bash (no grep), so
# it can't trip `set -e` on the disable path where nothing matches.
rebuild_hook_list() { # $1 = current list, $2 = new hook path ("" to only remove), $3 = separator -> prints new list
  local current="$1" newpath="$2" sep="$3" out="" item
  local IFS="$sep"
  for item in $current; do
    [[ -z "$item" || "$item" == "$newpath" ]] && continue
    case "$item" in
      */AzureMonitorProfiler/*/Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll) continue;;
    esac
    out="${out:+$out$sep}$item"
  done
  [[ -n "$newpath" ]] && out="${out:+$out$sep}$newpath"
  printf '%s' "$out"
}

get_setting() { # $1 = name -> prints current value ("" if unset)
  az webapp config appsettings list -g "$RG" -n "$APP" "${SLOT_ARGS[@]}" \
    --query "[?name=='$1'].value | [0]" -o tsv 2>/dev/null || true
}

CUR_HOOKS=$(get_setting DOTNET_STARTUP_HOOKS)
CUR_HSA=$(get_setting ASPNETCORE_HOSTINGSTARTUPASSEMBLIES)

if [[ "$DISABLE" -eq 1 ]]; then
  echo "Disabling the codeless profiler on $APP${SLOT:+ (slot: $SLOT)}..."
  # Remove only OUR entries (any staged version); leave anything else other agents added.
  NEW_HOOKS=$(rebuild_hook_list "$CUR_HOOKS" "" ":")
  NEW_HSA=$(remove_from_list "$CUR_HSA" "$HOSTINGSTARTUP_ASSEMBLY" ";")
  SETTINGS=()
  [[ -n "$NEW_HOOKS" ]] && SETTINGS+=("DOTNET_STARTUP_HOOKS=$NEW_HOOKS") || az webapp config appsettings delete -g "$RG" -n "$APP" "${SLOT_ARGS[@]}" --setting-names DOTNET_STARTUP_HOOKS >/dev/null
  [[ -n "$NEW_HSA" ]]   && SETTINGS+=("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=$NEW_HSA") || az webapp config appsettings delete -g "$RG" -n "$APP" "${SLOT_ARGS[@]}" --setting-names ASPNETCORE_HOSTINGSTARTUPASSEMBLIES >/dev/null
  az webapp config appsettings delete -g "$RG" -n "$APP" "${SLOT_ARGS[@]}" --setting-names SP_UPLOADER_PATH SP_STARTUP_LOG >/dev/null || true
  [[ ${#SETTINGS[@]} -gt 0 ]] && az webapp config appsettings set -g "$RG" -n "$APP" "${SLOT_ARGS[@]}" --settings "${SETTINGS[@]}" >/dev/null
  echo "Disabled. The staged payload under $REMOTE_BASE was left in place (delete manually via Kudu if desired)."
  exit 0
fi

# --- Resolve the payload zip and version ---
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
if [[ -z "$PAYLOAD_ZIP" ]]; then
  # repo default: <repo>/Out/Linux/AzureMonitorProfiler.<version>.zip (newest)
  PAYLOAD_ZIP=$(ls -t "$SCRIPT_DIR/../../../Out/Linux/"AzureMonitorProfiler.*.zip 2>/dev/null | head -1 || true)
fi
[[ -n "$PAYLOAD_ZIP" && -f "$PAYLOAD_ZIP" ]] || die "Payload zip not found. Build it (Build-SiteExtension.ps1) or pass --payload-zip <path>."
if [[ -z "$PAYLOAD_VERSION" ]]; then
  base=$(basename "$PAYLOAD_ZIP"); PAYLOAD_VERSION="${base#AzureMonitorProfiler.}"; PAYLOAD_VERSION="${PAYLOAD_VERSION%.zip}"
fi
[[ -n "$PAYLOAD_VERSION" ]] || die "Could not determine payload version; pass --payload-version <version>."

REMOTE_DIR="$REMOTE_BASE/$PAYLOAD_VERSION"
HOOK_PATH="$REMOTE_DIR/Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll"
UPLOADER_PATH="$REMOTE_DIR/Uploader/Microsoft.ApplicationInsights.Profiler.Uploader.dll"

echo "Staging payload to $REMOTE_DIR on $APP${SLOT:+ (slot: $SLOT)} ..."
echo "  zip: $PAYLOAD_ZIP"
# Kudu extracts the zip contents into the target path (version-stamped folder mirrors the Windows layout).
HTTP=$(curl -sS -o /dev/null -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $TOKEN" \
  --data-binary @"$PAYLOAD_ZIP" \
  "https://$SCM_HOST/api/zip$REMOTE_DIR/")
[[ "$HTTP" =~ ^2 ]] || die "Kudu zip upload failed (HTTP $HTTP)."
echo "  staged (HTTP $HTTP)."

NEW_HOOKS=$(rebuild_hook_list "$CUR_HOOKS" "$HOOK_PATH" ":")
NEW_HSA=$(append_dedup "$CUR_HSA" "$HOSTINGSTARTUP_ASSEMBLY" ";")

echo "Setting App Settings (this restarts the app)..."
SETTINGS=(
  "DOTNET_STARTUP_HOOKS=$NEW_HOOKS"
  "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=$NEW_HSA"
  "SP_UPLOADER_PATH=$UPLOADER_PATH"
)
[[ "$DEBUGLOG" -eq 1 ]] && SETTINGS+=("SP_STARTUP_LOG=1")
az webapp config appsettings set -g "$RG" -n "$APP" "${SLOT_ARGS[@]}" --settings "${SETTINGS[@]}" >/dev/null

cat <<EOF

Done. The codeless profiler is enabled on '$APP'${SLOT:+ (slot: $SLOT)}.
  DOTNET_STARTUP_HOOKS                 -> $HOOK_PATH
  ASPNETCORE_HOSTINGSTARTUPASSEMBLIES  -> (…;) $HOSTINGSTARTUP_ASSEMBLY
  SP_UPLOADER_PATH                     -> $UPLOADER_PATH

Ensure APPLICATIONINSIGHTS_CONNECTION_STRING is set on the app. Then tail the log stream and look for:
  [Azure.Monitor.OpenTelemetry.Profiler.StartupHook] Detected telemetry stack: ...
  [Azure.Monitor.OpenTelemetry.Profiler.HostingStartup] info: Enabling the ... profiler.
To remove: re-run with --disable.
EOF
