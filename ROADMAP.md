# Next editing work

Set creator now drafts a new set from copied base items, with independent piece editing, private bonus values and default world drops at 0.01% per piece. Creation and managed item-level/drop changes have transactional history and revert/reapply. Terrain-derived zones and conservative level/creature filters keep high-level copies out of starter-area loot.

Gear designer now includes read-only character replacement comparisons, server-allocation budgets and exact peers, a set-piece/bonus workspace and reviewed item duplication with appearance/effect links and independent operation history. Named effect/aura filters and a trigger filter are available in the weapon spell/template picker.

Item, set-bonus and weapon-proc editors now support drafts, side-by-side WoW tooltips, filtered spell selection, validation and transactional saves. Items support named subclasses, a Show original comparison and repeated edits. Fixed set-bonus values use private cloned spell definitions and client tooltip text; shared source spells stay unchanged. Set/proc history retains immutable original and modified assignments with revert/reapply and inactive removal/restoration. Shared spell definitions and original item effects are preserved.

The user verified item revert and inactive history removal in game. Server-supplied item definitions without an original are excluded from item History and its displayed counts, while remaining in the catalog.

- Add optional per-weapon proc chance/PPM editing only after verifying the server rules. Current editors configure item-effect triggers/cooldowns/charges and preserve global spell_proc rules.
- Add membership/name changes for existing sets if requested; the new set creator already creates independent membership and names.
- Extend history conflict resolution for overlapping successive edits. Current operations block automatic restore when newer rows/registrations or external references differ.
- Resolve unsupported instance bonus lists, random properties, conditional/nonlinear scaling and maximum durability using verified server/client rules.
- Continue in-game checks of set bonuses, proc behavior and level previews as those features expand. Database verification alone does not test live server behavior.

- Extend comparisons with verified enchant/gem totals, equip proficiencies and talent-specific hand rules. Current comparisons use base definitions and mark unsupported instance modifiers unresolved.
- Validate newly duplicated item appearance, equip behavior and server reload in game before expanding into custom model editing.

- Next requested iteration: choose specific bosses as item-drop sources, with reviewed boss loot changes and reversible history.
- Verify newly created sets, privately edited bonuses and world drops in the running custom server; current checks verify persisted definitions and rollback, not live loot rolls.
