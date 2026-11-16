# Issue #2: retrieve one activity

The approved scope is [issue #2](https://github.com/sakisk/garmin/issues/2), with project layout and deployment decisions in #1. Implement on `issue-2-activity-cli`.

Use `apps/backup-tool/Garmin.Backup.slnx`, with a packable `src/Garmin.PersonalApi` library, `src/Garmin.Cli` consumer and `tests/Garmin.Tests` xUnit project. Use only framework dependencies in production and source-generated JSON. Keep HTTP and typed models in the library; credentials discovery, formatting and exit codes belong to the CLI. Prefer this small explicit wrapper over generating the entire Swagger client or adding HTTP/CLI frameworks.

- [x] Write process-level CLI tests using a local HTTP listener and synthetic data. Verify failure before implementing. Assert POST ApiKey exchange, plain-text token, GET Bearer activity request and readable summary.
- [x] Implement the library and CLI to pass success and nullable-field tests. Model `activityTypeDTO` and `summaryDTO`; interpret offset-free `startTimeGMT` as UTC; label local time as offset unknown. Use metres and seconds without guessing units or timestamps.
- [x] Add failing tests then sanitized failures: invalid ID, absent/malformed key, 401/403, 404, 429/5xx, malformed responses, timeouts and connection failure. Do not echo arguments, response bodies or exception details. Test reuse/expiry and cancellation at the library boundary.
- [x] Add complete help, package metadata/readme and build/deployment documentation. Pack locally and inspect the package.
- [ ] Publish self-contained `linux-arm64` Native AOT and run the same process tests on Debian Trixie ARM64 (emulation is acceptable). Obtain approval before installing native tools, per the user's sysadmin profile. Record actual evidence and remaining limitations.
- [x] Review the diff against the ticket, run the full suite and format/build checks, then commit the feature branch. Do not close the issue unless all acceptance checks are satisfied.

User steering: prepare ARM64 build instructions only; do not install the missing native toolchain/emulation dependencies. Keep the target publication/runtime check explicitly unverified.

Contract evidence: Garmin's Personal API FAQ documents POST `/personalapitoken-service/personal-api/personal-api-key/token` with `Authorization: ApiKey …`. Garmin's reference Python authenticator reads the token as plain text and uses its JWT `exp`. The Swagger embedded in the official Swagger page documents GET `/activity-service/activity/{activityId}` returning `ActivitySummaryDTO`, containing `activityTypeDTO` and `summaryDTO`. `SummaryDTO.distance` is metres, `duration` seconds, and `startTimeGMT` is UTC without an offset suffix.
