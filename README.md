# AnoWoW Item Editor

A fresh WPF desktop catalog. Charcoal panels, blue accents, and WoW rarity colors.

## Run

Double-click `START_EDITOR.bat`, or run `dotnet run --project AnoWowItemEditor.csproj -c Release` with the .NET 10 SDK installed.

Uses the previous editor's Windows DPAPI-encrypted MariaDB login from `%LOCALAPPDATA%/AnoCoreItemEditorStudio/mariadb-login.dat`. Credentials stay outside this repository. The original app and its data are preserved.

## Browsing and character previews

- Left navigation: Item catalog, Characters, Spell catalog, Proc catalog. Spell and Proc pages are placeholders.
- Sortable, virtualized item table with rarity-colored names, required levels, and IDs.
- Name or exact ID search; rarity, slot, type, required-level, and equipment-set filters. Advanced filters collapse to leave room for the tooltip.
- Resizable WoW-style tooltip panel, actual source labels, loading, empty, and connection failure states.
- Set names, all member pieces, and piece-count bonuses from server DB2 plus active MariaDB overrides. Click a set name to browse its pieces, or a piece to select it. Catalog previews show 0 equipped; character previews count saved equipped pieces and highlight matching thresholds (respecting specialization and skill restrictions).
- Armor/weapon types, bindings, requirements, item effects, sockets and socket bonuses, and sell prices. Fixed spell amounts (including references to another spell) are read from effect records.
- Read-only queries against `foreverclassic_hotfixes` and `foreverclassic_characters`; no database schema, character, or item changes.
- Search the character list, load all owned item instances (equipment, bag/bank storage, mail, void storage, and other owned instances), and filter storage. Item stacks, duplicates, instance IDs, durability, enchantments, and socketed gem references remain distinct. Storage locations retain raw bag/slot information rather than guessing this custom server's bank slot boundaries.
- Change preview level from 1–60; Reset restores the selected character's saved level, or 60 in the catalog. The preview cannot save a level change. An online character's database snapshot may lag unsaved in-game changes.

The complete catalog uses installed server DB2 files in `C:/AnoCore/AnoCore-Server/data/dbc/enUS` for build `7.3.5.70450`. Active MariaDB `item_sparse` overrides replace DB2 values, including custom items. Build 70450 takes precedence over generic build 0. As in the previous app, DBCD obtains table definitions with GithubDBDProvider (internet access or its cache is required).

Base stats, armor, and weapon damage are calculated from the installed RandPropPoints, armor, damage, curve and socket-cost tables, with active hotfixes. Supported linear level curves use the preview level, subject to supported content tuning and saved fixed scaling levels. Fixed-level gear does not receive an artificial character-level multiplier. Original Dreadnaught Breastplate at item level 92 calculates to 1027 armor, 21 Strength, 43 Stamina, 13 defense, 12 dodge, and 20 hit rating.

`[scales]` remains for unsupported instance bonus lists, random properties, conditional/nonlinear scaling and spell-context calculations. Missing definitions are listed explicitly and never disappear from a character's inventory. `[client value]` marks unavailable maximum durability; owned items show their actual saved current durability. Enchants and gems are shown separately; their totals, character buffs/talents, rating percentages, and custom AnoCore behavior are not simulated. Values are a server-table base preview, not a claim of full client parity; an in-game comparison is still required for the custom server. Item icons remain future work.

Formula references: [TrinityCore Item.cpp](https://github.com/TrinityCore/TrinityCore/blob/master/src/server/game/Entities/Item/Item.cpp), [ItemTemplate.cpp](https://github.com/TrinityCore/TrinityCore/blob/master/src/server/game/Entities/Item/ItemTemplate.cpp), and [ItemEnchantmentMgr.cpp](https://github.com/TrinityCore/TrinityCore/blob/master/src/server/game/Entities/Item/ItemEnchantmentMgr.cpp). These upstream formulas do not establish parity with AnoCore's unavailable custom implementation.

The original Dreadnaught Breastplate (22416), set 523, is the reference example: nine pieces including the ring and bonuses at 2/4/6/8 pieces. Set lists and bonuses are loaded generically for all sets, without hardcoded Dreadnaught descriptions.

## Verification

`dotnet run --project AnoWowItemEditor.csproj -c Release -- --verify`

Loads real data and verifies catalog/set filters, member navigation, stat/armor calculations, fixed-level behavior, curve interpolation, spell descriptions, scaling fallbacks, and tooltip generation for every catalog item. Checks real character loading and instance counts, equipped set thresholds, and preview-level validation without changing the saved level. Renders `artifacts/catalog-preview.png`, `artifacts/dreadnaught-tooltip.png`, and (when characters exist) `artifacts/character-preview.png`. Results are in `artifacts/verification.json` and `artifacts/character-verification.json`; failures use a nonzero exit code and `artifacts/verification-error.txt`. Generated artifacts are ignored by Git.

If an open app locks the normal build output, build a preview with `dotnet build -c Release -p:BaseOutputPath=artifacts/preview/`, then run `dotnet artifacts/preview/Release/net10.0-windows/AnoWowItemEditor.dll --verify`. Close and restart the normal app to load updates.
