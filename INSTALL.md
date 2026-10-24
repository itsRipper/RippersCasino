# Installation

RippersCasino 1.0.0 supports **SPT 4.1.6**. Casino access requires **Intelligence Center Level 1** and **Generator Level 1**.

1. Download `RippersCasino-1.0.0.zip` from [GitHub Releases](https://github.com/itsRipper/RippersCasino/releases).
2. Close Tarkov and the SPT server.
3. Extract the archive directly into the main SPT installation folder, beside `EscapeFromTarkov.exe`. Merge `BepInEx/` and `SPT_Runtime/`.
4. Start SPT and use the Intelligence Center laptop or its **Rippers Casino** interaction.

## Troubleshooting and data

If the casino interaction is missing, check the hideout requirements and confirm that both included directories were extracted into the SPT root. For a loading or transaction error, close the casino and check the client and server logs.

Before replacing an installation, back up `SPT_Runtime/user/profiles/`, `SPT_Runtime/user/mods/RippersCasino/` and `BepInEx/config/RippersCasino/`. Wallet balances are stored in profiles; table recovery records are stored in the mod's `table-data/` directory. Client recovery requests are stored in the BepInEx configuration directory.

Existing configuration and casino data are preserved. The server creates `ripperscasino.json` from the shipped defaults only when it is missing. Do not delete wallets, pending requests or table recovery records to bypass an error. If saved data cannot be opened, retain the complete backup and seek support before making changes.
