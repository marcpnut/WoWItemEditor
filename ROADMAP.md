# Next editing work

History supports reviewed, transactional item-field revert/reapply with immutable retained payloads, conflict detection and linked audit events. Inactive entries can be removed from the normal history list and restored through Show removed entries. Removal never deletes the audit trail or stored payload.

Spell and Proc catalogs are read-only references. A future editor can use their IDs to build set bonuses and weapon procs. Assignment needs validation of trigger, spell requirements, cooldowns, server proc rules and dependencies. Showing a spell candidate does not guarantee it is suitable for every item.

- Add set-bonus editing with before/after tooltips and recorded set operations.
- Add weapon proc assignment using the reference catalogs, preserving original links and effects.
- Extend historical operations to set/spell/effect definitions before promising complete historical dependency reproduction. Current history actions restore item fields and retain shared definitions.
- Resolve unsupported instance bonus lists, random properties, conditional/nonlinear scaling and maximum durability using verified server/client rules.
- The user confirmed the initial in-game comparison is correct. Continue checking representative armor, weapons, ratings, procs and level behavior as those features expand.
- Keep unavailable originals and unsupported calculations explicit.
