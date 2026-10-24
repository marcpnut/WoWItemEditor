# Next editing work

Set creator now drafts a new set from copied base items, with independent piece editing, private bonus values and default world drops at 0.01% per piece. Creation and managed item-level/drop changes have transactional history and revert/reapply. Terrain-derived zones and conservative level/creature filters keep high-level copies out of starter-area loot.

Gear designer now includes read-only character replacement comparisons, server-allocation budgets and exact peers, a set-piece/bonus workspace and reviewed item duplication with appearance/effect links and independent operation history. Named effect/aura filters and a trigger filter are available in the weapon spell/template picker.

Item, set-bonus and weapon-proc editors now support drafts, side-by-side WoW tooltips, filtered spell selection, validation and transactional saves. Items support named subclasses, a Show original comparison and repeated edits. Fixed set-bonus values use private cloned spell definitions and client tooltip text; shared source spells stay unchanged. Set/proc history retains immutable original and modified assignments with revert/reapply and inactive removal/restoration. Shared spell definitions and original item effects are preserved.

The user verified item revert and inactive history removal in game. Server-supplied item definitions without an original are excluded from item History and its displayed counts, while remaining in the catalog.

- Add optional per-weapon proc chance/PPM editing only after verifying the server rules. Current editors configure item-effect triggers/cooldowns/charges and preserve global spell_proc rules.
- Consider membership/name editing for native sets if requested; Modify created sets already supports created-set membership and names.
- Extend history conflict resolution for overlapping successive edits. Current operations block automatic restore when newer rows/registrations or external references differ.
- Resolve unsupported instance bonus lists, random properties, conditional/nonlinear scaling and maximum durability using verified server/client rules.
- Continue in-game checks of set bonuses, proc behavior and level previews as those features expand. Database verification alone does not test live server behavior.

- Extend comparisons with verified enchant/gem totals, equip proficiencies and talent-specific hand rules. Current comparisons use base definitions and mark unsupported instance modifiers unresolved.
- Validate newly duplicated item appearance, equip behavior and server reload in game before expanding into custom model editing.

- Instance/boss-only drop assignment is implemented with reviewed loot changes and reversible history. Continue live testing of boss drops.
- Verify newly created sets, privately edited bonuses and world drops in the running custom server; current checks verify persisted definitions and rollback, not live loot rolls.

Implemented after the crash review: effective calculation-row safety gates, immediate stat draft updates, named material filters, readable allowed classes, instance/boss-only drops, searchable aura/effect choices, explained and bounded private bonus values, and Modify created sets with duplicate additions, non-destructive member removal, rename and transactional history. See SERVER_SAFETY.md for observed table bounds and the limits of crash diagnosis without matching symbols.

Set workspaces now include an equipment-slot layout with selection tooltips and double-click item editing. Live character model rendering and actual item icon artwork remain future work.

Completed: selected-slot drop editing, searchable instance/boss destinations with retained selections, readable saved loot sources/rates, explicit draft-copy plans, complete-set character simulation and reference-set balance review. Numeric comparisons default to common slots and report base stats separately from bonus/proc power. Next validation remains in-game equip/tooltip and actual boss-loot behavior when WoW is available. Future simulation work includes verified enchant/gem totals, class proficiencies, talent-specific hand rules and proc/set-effect power.

Completed: complete saved-set testing checklist with editor links, scoped saved-drop reviews, compatible-slot bonus thresholds and explicit live-test requirements. Bonus/proc explanations now show amounts, units, targets, durations/ticks, activation/cooldown conditions and stored proc chance/PPM rules without inferring final combat outcomes. Next candidate: an embedded 3D viewer compatibility prototype using matching client assets; see MODEL_VIEWER_PLAN.md.
