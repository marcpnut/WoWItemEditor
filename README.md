# AnoWoW Item Editor

A fresh WPF desktop catalog. Charcoal panels, blue accents, and WoW rarity colors.

## Run

Double-click `START_EDITOR.bat`, or run `dotnet run --project AnoWowItemEditor.csproj -c Release` with the .NET 10 SDK installed.

Uses the previous editor's Windows DPAPI-encrypted MariaDB login from `%LOCALAPPDATA%/AnoCoreItemEditorStudio/mariadb-login.dat`. Credentials stay outside this repository. The original app and its data are preserved.

## First version

- Left navigation: Item catalog, Spell catalog, Proc catalog. Spell and Proc pages are placeholders.
- Sortable, virtualized item table with rarity-colored names, required levels, and IDs.
- Name or exact ID search; rarity, slot, type, and required-level filters.
- Resizable WoW-style tooltip panel, actual source labels, loading, empty, and connection failure states.
- Set names, all member pieces, and piece-count bonuses from server DB2 plus active MariaDB overrides. Catalog previews show 0 equipped; no character state is assumed.
- Armor/weapon types, bindings, requirements, item effects, sockets and socket bonuses, and sell prices. Fixed spell amounts (including references to another spell) are read from effect records.
- Read-only queries against `foreverclassic_hotfixes`; no database schema or item changes.

The complete catalog uses installed server DB2 files in `C:/AnoCore/AnoCore-Server/data/dbc/enUS` for build `7.3.5.70450`. Active MariaDB `item_sparse` overrides replace DB2 values, including custom items. Build 70450 takes precedence over generic build 0. As in the previous app, DBCD obtains table definitions with GithubDBDProvider (internet access or its cache is required).

The tooltip uses `[scales]` for stat amounts, armor, damage, and spell values requiring client or character scaling. It never presents raw stat allocation as a displayed stat. `[client value]` marks durability, which is absent from this build's item record. Unsupported spell-context expressions and unavailable descriptions remain explicitly marked. It does not run the WoW client or claim to reproduce its full calculation engine. Item icons and character-specific equipped/active state remain future work.

The original Dreadnaught Breastplate (22416), set 523, is the reference example: nine pieces including the ring and bonuses at 2/4/6/8 pieces. Set lists and bonuses are loaded generically for all sets, without hardcoded Dreadnaught descriptions.

## Verification

`dotnet run --project AnoWowItemEditor.csproj -c Release -- --verify`

Loads real data, checks catalog identity, source counts, filters, validation, empty states, Dreadnaught membership and bonus text, cross-spell references, scaling markers, unavailable data, inactive bonuses, and tooltip generation for every catalog item. Renders `artifacts/catalog-preview.png` and a complete `artifacts/dreadnaught-tooltip.png`. Results are in `artifacts/verification.json`; failures use a nonzero exit code and `artifacts/verification-error.txt`. Generated artifacts are ignored by Git.

If an open app locks the normal build output, build a preview with `dotnet build -c Release -p:BaseOutputPath=artifacts/preview/`, then run `dotnet artifacts/preview/Release/net10.0-windows/AnoWowItemEditor.dll --verify`. Close and restart the normal app to load updates.
