# Garmin activity CLI

Retrieves one activity through Garmin Connect Personal API and prints its available summary. Requires .NET SDK 11.0.100 to build; a published Native AOT executable needs no .NET runtime.

## Use

Create a Personal API key in Garmin Connect web: **Profile & Account → Settings → Personal API Key**. Copy the activity ID from an activity URL such as `https://connect.garmin.com/modern/activity/123456789`.

From the repository root, enter the key in zsh without putting it in command history:

```zsh
read -rs 'GARMIN_PERSONAL_API_KEY?Personal API key: '; echo
export GARMIN_PERSONAL_API_KEY
dotnet run --project apps/backup-tool/src/Garmin.Cli -- 123456789
unset GARMIN_PERSONAL_API_KEY
```

`--help` explains setup without requiring credentials or contacting Garmin. The secret is accepted only through `GARMIN_PERSONAL_API_KEY`. The CLI writes summary data to stdout and sanitized errors to stderr. Exit codes are 0 for success/help, 2 for invalid input/configuration, 1 for API/network failure and 130 for cancellation.

Synthetic output:

```text
Activity ID: 42
Name: Synthetic run
Type: running
Start: 2026-01-02T09:30:00.000Z (UTC)
Distance: 5000.25 m
Duration: 1800.5 s
```

Distance is in metres and duration in seconds. UTC is preferred; a local-only timestamp is explicitly marked as having an unknown UTC offset. Missing fields say `unavailable`, while zero measurements display zero. Output is independent of the host culture/time zone. Keys and tokens stay in memory; no activity or credential files are written. Detailed metrics, verbosity options, JSON export and downloads are outside this ticket.

## Build, test and package

```sh
dotnet build apps/backup-tool/Garmin.Backup.slnx -c Release
dotnet test apps/backup-tool/Garmin.Backup.slnx -c Release
dotnet pack apps/backup-tool/src/Garmin.PersonalApi -c Release -o artifacts/packages
dotnet format apps/backup-tool/Garmin.Backup.slnx --verify-no-changes
```

Production projects use only framework HTTP/JSON and source-generated serialization. AOT compatibility analyzers and warnings-as-errors are enabled. The test project uses xUnit and the .NET test SDK. Tests launch the real CLI process against a short-lived local HTTP service. Fixtures contain only synthetic credentials and activity data. They check the exact authentication headers and activity route, nullable values, timestamps, failures and sanitized output. Library tests cover token reuse/renewal, typed data and cancellation/timeouts.

The packable library has its own [consumer README](src/Garmin.PersonalApi/README.md), which is included in the local NuGet package. The CLI uses a project reference.

## Native AOT on Debian Trixie ARM64 / Raspberry Pi 4

The simplest build uses .NET SDK 11.0.100 on an ARM64 Debian Trixie machine, with `clang` and the native development toolchain installed. On Debian, prepare the build host with:

```sh
sudo apt-get update
sudo apt-get install --no-install-recommends clang build-essential zlib1g-dev
dotnet publish apps/backup-tool/src/Garmin.Cli -c Release -r linux-arm64 --self-contained true -o artifacts/linux-arm64
```

`PublishAot=true` is set in the project. Preserve `Garmin.Cli.dbg` separately if debugging symbols are needed; deploy `Garmin.Cli`. On Debian Trixie ARM64 the runtime requirements are `libc6`, `libgcc-s1`, `libssl3t64`, `ca-certificates` and network access to Garmin. Invariant globalization avoids an ICU dependency. Native AOT is self-contained with respect to .NET, but still uses native system libraries. Validate actual binary dependencies with `ldd` on the target.

Cross-compilation from Linux x64 also needs a target-capable compiler/linker, ARM64 C runtime development libraries and target-compatible objcopy/strip. See [Microsoft's cross-compilation guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile). Build against a glibc version no newer than the target.

To run the process tests against a published executable, set `GARMIN_TEST_EXECUTABLE` to its absolute path, then run `dotnet test` as above. An executable wrapper can invoke QEMU with a Debian Trixie ARM64 root filesystem; that wrapper must forward the inherited environment and arguments. The test host may remain x64. The controlled service binds loopback only. `GARMIN_PERSONAL_API_BASE_URL` overrides the origin for testing; HTTP is accepted only on loopback and redirects are disabled. Normal use needs no endpoint setting.

Actual deployment evidence and any remaining acceptance gaps are recorded in [verification.md](verification.md).

## Contract references

Verified against the [official Swagger page](https://developer.garmin.com/gc-developer-program/personal/swagger-docs/) and [authentication FAQ](https://developer.garmin.com/gc-developer-program/personal/personal-faq/):

- POST `/personalapitoken-service/personal-api/personal-api-key/token` with `Authorization: ApiKey …` returns a plain-text bearer JWT. Garmin's [reference authenticator](https://github.com/garmin/connect-personal-api-python/blob/main/src/garmin_personal_api/auth.py) reads its `exp` claim.
- GET `/activity-service/activity/{activityId}` with `Authorization: Bearer …` returns `ActivitySummaryDTO`. Type is under `activityTypeDTO`; measurements and timestamps are under `summaryDTO`.
- Swagger documents `startTimeGMT` as UTC without an offset suffix, `startTimeLocal` as local time without an offset, distance in metres and duration in seconds.

No live Garmin account or credentials are required for verification.
