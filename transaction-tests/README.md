# Native SPT transaction checks

```powershell
dotnet run --project transaction-tests -c Release -p:SptRoot='C:\Games\SPT' -- 'C:\Games\SPT\SPT_Runtime'
```

Uses installed SPT 4.1.6 assemblies with isolated synthetic profiles, never real saves. Checks profile replacement failures, cached-hash invalidation, aggregate read-back, asynchronous profile gates, table escrow/liability and idempotent recovery, wallet/cashier contracts, native currency placement and rollback. Includes withdrawals from zero destination balance, full-stash rejection and preservation of incomplete reservation evidence.

See [development](../docs/development.md) and [installer verification](../tools/Test-Installation.ps1) for package and data-preservation checks. These tests do not render Unity UI or measure frame time.
