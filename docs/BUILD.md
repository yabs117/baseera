# Build and test Baseera

Install the .NET 10 SDK on Windows and run these commands from the repository root.

```powershell
./build.ps1 -Test -Smoke
```

`-Test` runs the core test suite. `-Smoke` runs the WPF integration checks in an isolated data directory and saves screenshots under `artifacts/screenshots/`.

## Package a Windows build

```powershell
./build.ps1 -Test -Publish
```

For Preview source, this produces `artifacts/Baseera.exe`, `artifacts/Baseera.zip`, `artifacts/Baseera-Browser-Extension.zip`, and `artifacts/SHA256SUMS.txt`. Stable packaging requires a trusted, timestamped Authenticode signature. See [release channels and signing](RELEASE-CHANNELS.md) for configuring the publisher certificate and verifying packages. Use `-Smoke -Publish` to check the packaged executable as well.

## Run focused checks

| Check | Command |
| --- | --- |
| Browser extension protocol (Node.js 22+) | `node --test extension/test/protocol.test.js` |
| Self-update replacement, restart, and rollback on isolated copies (after packaging) | `./tests/self-update.e2e.ps1` |
| Windows diagnostic sources using isolated data | `./tests/windows/Test-ReadOnlySystem.ps1` |
| Read-only diagnostics plus optional DNS and ICMP checks against the test host | `./tests/windows/Test-ReadOnlySystem.ps1 -AllowNetworkCheck` |

The diagnostic source check reports availability and counts. It does not send readings to an AI provider or run a repair. See [contribution guidance](../CONTRIBUTING.md) and [third-party notices](../THIRD-PARTY-NOTICES.md).
