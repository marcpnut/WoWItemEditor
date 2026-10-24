# AnoWoW Item Editor

A fresh WPF desktop catalog. Charcoal panels, blue accents, and WoW rarity colors.

## Run

Double-click `START_EDITOR.bat`, or run `dotnet run --project AnoWowItemEditor.csproj -c Release` with the .NET 10 SDK installed.

Uses the previous editor's Windows DPAPI-encrypted MariaDB login from `%LOCALAPPDATA%/AnoCoreItemEditorStudio/mariadb-login.dat`. Credentials stay outside this repository. The original app and its data are preserved.

## First version

- Left navigation: Item catalog, Spell catalog, Proc catalog. Spell and Proc pages are placeholders.
- Sortable, virtualized item table with rarity-colored names, required levels, and IDs.
- Name or exact ID search; rarity, slot, type, and required-level filters.
- Resizable tooltip panel, actual source labels, loading, empty, and connection failure states.
- Read-only queries against `foreverclassic_hotfixes`; no database schema or item changes.

The complete catalog uses installed server DB2 files in `C:/AnoCore/AnoCore-Server/data/dbc/enUS` for build `7.3.5.70450`. Active MariaDB `item_sparse` overrides replace DB2 values, including custom items. Build 70450 takes precedence over generic build 0. As in the previous app, DBCD obtains table definitions with GithubDBDProvider (internet access or its cache is required).

The tooltip shows known item fields. Stat allocations are explicitly labeled; scaled stats, armor, damage, icons, resolved spell effects, and set bonus descriptions are future work.

## Verification

`dotnet run --project AnoWowItemEditor.csproj -c Release -- --verify`

Loads real data, checks catalog identity, source counts, filters, validation, and empty states, and renders `artifacts/catalog-preview.png`. Results are in `artifacts/verification.json`; failures use a nonzero exit code and `artifacts/verification-error.txt`. Generated artifacts are ignored by Git.
