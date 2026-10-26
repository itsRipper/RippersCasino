# Development

## Requirements

- .NET 10 SDK and PowerShell 7 (7.4 for `Prepare-OfflineFixture.ps1`).
- .NET Framework 4.7.2 developer/targeting pack for the client.
- Local SPT 4.1.6. Game, Unity and SPT assemblies are external references; do not redistribute them.

## Build and tests

```powershell
pwsh -File tools/Build.ps1 -SptRoot 'C:\Games\SPT'
```

This builds the solution, runs the automated and native transaction tests, and creates `artifacts/RippersCasino-1.0.1.zip`. `-SkipTests` skips test execution. For direct builds, pass `-p:SptRoot=...` or set `SPT_ROOT`.

Build the complete solution before running checks separately: client lifecycle tests inspect the compiled client DLL without starting Unity.

```powershell
dotnet run --project tests -c Release
dotnet run --project transaction-tests -c Release -p:SptRoot='C:\Games\SPT' -- 'C:\Games\SPT\SPT_Runtime'
pwsh -File tools/Test-Installation.ps1 -SptRoot 'C:\Games\SPT'
dotnet list core/RippersCasino.Core.csproj package --vulnerable --include-transitive
```

Transaction and installer tests use synthetic profiles in disposable directories. They do not modify real saves. Unity rendering, audio and input require an in-game check.

## Packaging

`tools/runtime-files.json` defines the complete runtime payload for packaging and the optional maintainer installer. The player ZIP contains only `BepInEx/` and `SPT_Runtime/`. The build consolidates the project MIT licence, HoldemPoker Apache-2.0 licence and notices, and asset provenance into `SPT_Runtime/user/mods/RippersCasino/NOTICE.txt`, preserving their complete text.

`artifacts/SHA256SUMS.txt` contains the ZIP checksum. `artifacts/RippersCasino-1.0.1.files.sha256` contains per-file checksums for `tools/Install.ps1`. Neither verification file is installed. The package ships defaults, never a live configuration. Update the runtime manifest when adding runtime files.

The server bundles HoldemPoker.Evaluator 1.0.1 and HoldemPoker.Cards 0.0.1 under Apache-2.0. Client references are provided by the existing game, Unity, BepInEx and SPT installation.

## Persistence

Wallet schema 4, cashier protocol 3 and table schema 2 are distinct from the product version. Preserve request IDs, sequences, fingerprints, profile gates, escrow/liability checks and durable journals. Money uses integer accounting: 1 chip = 1,000 RUB, 1 USD = 136 RUB and 1 EUR = 158 RUB, with residual RUB retained. Blackjack requires even wagers for exact 3:2 payouts.

Back up the complete profile and casino `table-data/` directory, including its receipt archive. Archived committed receipts reject duplicate requests and must not expire by age. Recover pending journals before applying new actions. Table saves require the schema-2 `Solo` marker and at most one human seat; invalid state fails without settlement or conversion.

Casino mutations stage a PMC clone, then replace and durably verify the complete profile under the native item-event gate. An interrupted save must remain blocked until its original receipt is durable; never rerun the wager to recover it. Missing configuration creates defaults without touching profiles. Incompatible configuration or save schemas fail explicitly without resetting data.

## Optional tools

`Prepare-OfflineFixture.ps1` creates an isolated loopback backend without copying profiles. Start `SPT.Server.dll` from its returned backend directory and register a disposable account. Its database/image junctions reference the local installation; do not modify their targets or recursively delete through them.

`Render-Pixel-Art.py` and `Render-Table-Art.py` maintain authored assets. Both use Python 3; the table-art tool also requires Pillow and locally installed Windows Georgia fonts. Committed assets build without regeneration. Previews go to `artifacts/`.
