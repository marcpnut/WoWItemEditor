# AnoWoW Item Editor

A fresh WPF desktop catalog. Charcoal panels, blue accents, and WoW rarity colors.

## Run

Double-click `START_EDITOR.bat`, or run `dotnet run --project AnoWowItemEditor.csproj -c Release` with the .NET 10 SDK installed.

Uses the previous editor's Windows DPAPI-encrypted MariaDB login from `%LOCALAPPDATA%/AnoCoreItemEditorStudio/mariadb-login.dat`. Credentials stay outside this repository. The original app and its data are preserved.

## Browsing and character previews

- Left navigation: Characters, Item catalog, Spell catalog, Proc catalog, and a separate Change history category.
- Sortable, virtualized item table with rarity-colored names, required levels, and IDs.
- Name or exact ID search; rarity, slot, type, required-level, and equipment-set filters. Advanced filters collapse to leave room for the tooltip.
- Resizable WoW-style tooltip panel, actual source labels, loading, empty, and connection failure states.
- Set names, all member pieces, and piece-count bonuses from server DB2 plus active MariaDB overrides. Click a set name to browse its pieces, or a piece to select it. Catalog previews show 0 equipped; character previews count saved equipped pieces and highlight matching thresholds (respecting specialization and skill restrictions).
- Armor/weapon types, bindings, requirements, item effects, sockets and socket bonuses, and sell prices. Fixed spell amounts (including references to another spell) are read from effect records.
- Catalogs and character previews are read-only. Reviewed History actions can change item-field overrides in `foreverclassic_hotfixes`; characters are never modified.
- Search the character list, load all owned item instances (equipment, bag/bank storage, mail, void storage, and other owned instances), and filter storage. Item stacks, duplicates, instance IDs, durability, enchantments, and socketed gem references remain distinct. Storage locations retain raw bag/slot information rather than guessing this custom server's bank slot boundaries.
- Enter a preview level from 1–60, then click **Apply level** (or press Enter) to recalculate the selected tooltip. Pending and applied levels are shown explicitly. Fixed-level items retain their base stats and say why. Reset immediately applies the selected character's saved level, or 60 in the catalog. The preview cannot save a level change. An online character's database snapshot may lag unsaved in-game changes.

The complete catalog uses installed server DB2 files in `C:/AnoCore/AnoCore-Server/data/dbc/enUS` for build `7.3.5.70450`. Valid registered MariaDB payloads replace DB2 values, including definitions verified in older builds. A payload's VerifiedBuild is provenance and need not equal its hotfix registration build. Latest registration status is determined by push ID for each table hash/record ID. A generic build-0 payload takes priority, otherwise the highest verified payload is selected. Native item identities are preserved even if the item name/details arrive later from SQL. DBCD obtains table definitions with GithubDBDProvider (internet access or its cache is required).

## Change history

The separate History category lists item-sparse database payloads and retained item snapshots from recorded activity. Filter Active/Inactive/All, search by name, exact ID or changed field, and optionally filter to items with recorded app activity. Rarity, slot, type, required-level range and equipment-set filters match the Item catalog. The active-override count in the footer opens this page.

Selecting a record shows two independent tooltips at the same applied preview level: original installed DB2 (or an explicitly identified preserved original) and current active item. For inactive entries the right pane shows the retained modified version, clearly marked inactive, rather than pretending it is live. Different tooltip lines are highlighted and a field table reports exact known differences. Missing original definitions are explicit; unknown values are not fabricated as zero. Original spell/set/calculation tables are loaded separately from current overrides.

Existing item activity is read from the Studio audit table and its backup mirror. New actions append events to both in one transaction; previous events remain unchanged. Recorded app events retain actor, recorded UTC time, provenance, and raw before/after snapshots. Database registrations alone establish neither authorship nor the original save time. The viewer distinguishes records with activity from unattributed database content.

The old count of 474 was incomplete: it excluded valid payloads whose verification build differed from their registration build. Correcting that check also recovers previously missing item names. Counts include server-supplied data as well as recorded edits; they are not a count of personal changes.

**Revert** and **Reapply** open a review of the actual before/after item fields. A first revert removes the selected SQL sparse payload so the installed native definition takes effect; the exact modified payload stays in an immutable archive. Reapply restores it. An existing inactive SQL payload can also be activated, preserving its previous inactive state for reversal. Missing native originals, ambiguous multiple payload versions, and concurrent changes block unsafe actions. The app checks current rows, saved versions and installed definitions again when applying. Writes, snapshots and linked audit events commit together. Item identities, links and shared effects are retained. These actions cover item fields, not historical set/spell definitions. Reload the server and verify in game after a committed action.

**Remove from history** is available only for inactive entries and changes list visibility without deleting the retained payload or activity. **Show removed entries** lets you restore one to the list. Active entries cannot be hidden, including when an external change activates an entry after it was loaded. Reapply makes the entry visible again.

The first history action creates compatible audit/archive/visibility tables in `foreverclassic_hotfixes` and `anocore_studio_backup`, if needed. The existing Studio backup and protection records are preserved. Inactive previews retain item fields; referenced spell/set dependencies use current tables and are labelled accordingly.

## Spell and Proc catalogs

Search all installed spells plus active MariaDB overrides by name, exact spell/effect ID, description, or trigger. The Proc catalog includes existing item-effect definitions and all remaining spell candidates, explicitly marked as not configured. Candidates still require suitability checks when a future set/weapon editor assigns them. No editing or assignment controls exist here.

Details include resolved descriptions, raw spell/effect fields, cooldowns, proc/aura flags, server `spell_proc` rules when available, and existing set-bonus/item usage. Copy spell and item-effect IDs for reference. All difficulties are retained in the detail fields. Reference tables are loaded independently from item-tooltip snapshots and refreshed together.

Base stats, armor, and weapon damage are calculated from the installed RandPropPoints, armor, damage, curve and socket-cost tables, with active hotfixes. Supported linear level curves use the preview level, subject to supported content tuning and saved fixed scaling levels. Fixed-level gear does not receive an artificial character-level multiplier. Original Dreadnaught Breastplate at item level 92 calculates to 1027 armor, 21 Strength, 43 Stamina, 13 defense, 12 dodge, and 20 hit rating.

`[scales]` remains for unsupported instance bonus lists, random properties, conditional/nonlinear scaling and spell-context calculations. Missing definitions are listed explicitly and never disappear from a character's inventory. `[client value]` marks unavailable maximum durability; owned items show their actual saved current durability. Enchants and gems are shown separately; their totals, character buffs/talents, rating percentages, and custom AnoCore behavior are not simulated. Values are a server-table base preview, not a claim of full client parity; an in-game comparison is still required for the custom server. Item icons remain future work.

Formula references: [TrinityCore Item.cpp](https://github.com/TrinityCore/TrinityCore/blob/master/src/server/game/Entities/Item/Item.cpp), [ItemTemplate.cpp](https://github.com/TrinityCore/TrinityCore/blob/master/src/server/game/Entities/Item/ItemTemplate.cpp), and [ItemEnchantmentMgr.cpp](https://github.com/TrinityCore/TrinityCore/blob/master/src/server/game/Entities/Item/ItemEnchantmentMgr.cpp). These upstream formulas do not establish parity with AnoCore's unavailable custom implementation.

The original Dreadnaught Breastplate (22416), set 523, is the reference example: nine pieces including the ring and bonuses at 2/4/6/8 pieces. Set lists and bonuses are loaded generically for all sets, without hardcoded Dreadnaught descriptions.

## Verification

`dotnet run --project AnoWowItemEditor.csproj -c Release -- --verify`

Loads real data and verifies catalog/set filters, member navigation, stat/armor calculations, fixed-level behavior, curve interpolation, spell descriptions, scaling fallbacks, and tooltip generation for every catalog item. Checks real character loading, preview levels, definition recovery, history filters and synchronized comparisons. Exercises revert/reapply, immutable snapshots, audit mirrors, conflict checks, inactive removal/restoration and active-removal rejection in a transaction that is rolled back. Loads and searches the complete Spell and Proc catalogs. The verification command creates missing history metadata tables but commits no item, registration or audit test changes. Renders `artifacts/catalog-preview.png`, `artifacts/dreadnaught-tooltip.png`, `artifacts/history-preview.png`, and (when characters exist) `artifacts/character-preview.png`. Results are in `artifacts/verification.json`, `artifacts/character-verification.json`, and `artifacts/history-verification.json`; failures use a nonzero exit code and `artifacts/verification-error.txt`. Additional results are in artifacts/action-reference-verification.json with spell/proc page renders. Generated artifacts are ignored by Git.

If an open app locks the normal build output, build a preview with `dotnet build -c Release -p:BaseOutputPath=artifacts/preview/`, then run `dotnet artifacts/preview/Release/net10.0-windows/AnoWowItemEditor.dll --verify`. Close and restart the normal app to load updates.
