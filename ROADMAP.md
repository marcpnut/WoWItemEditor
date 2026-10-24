# Next editing work

Item, set-bonus and weapon-proc editors now support drafts, side-by-side WoW tooltips, filtered spell selection, validation and transactional saves. Items support named subclasses, a Show original comparison and repeated edits. Fixed set-bonus values use private cloned spell definitions and client tooltip text; shared source spells stay unchanged. Set/proc history retains immutable original and modified assignments with revert/reapply and inactive removal/restoration. Shared spell definitions and original item effects are preserved.

The user verified item revert and inactive history removal in game. Server-supplied item definitions without an original are excluded from item History and its displayed counts, while remaining in the catalog.

- Add optional per-weapon proc chance/PPM editing only after verifying the server rules. Current editors configure item-effect triggers/cooldowns/charges and preserve global spell_proc rules.
- Add set membership/name editing if requested; current set editor edits bonuses of existing sets.
- Extend history conflict resolution for overlapping successive edits. Current operations block automatic restore when newer rows/registrations or external references differ.
- Resolve unsupported instance bonus lists, random properties, conditional/nonlinear scaling and maximum durability using verified server/client rules.
- Continue in-game checks of set bonuses, proc behavior and level previews as those features expand. Database verification alone does not test live server behavior.
