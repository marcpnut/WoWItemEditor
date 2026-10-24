# Planned history actions

The history viewer is read-only. Revert and reapply are planned; no action buttons or database writes exist yet.

- Revert must disable a selected change while retaining an immutable original and modified snapshot. It must never delete the only copy of a change or erase its activity.
- An inactive change must retain its modified item, record IDs, build provenance, registration state, dependent item/effect/link rows, actor/provenance when known, and operation IDs so it can be reapplied later.
- Reapply must restore the retained operation, not reconstruct it by guessing fields from the current catalog.
- Both actions require a difference preview, conflict checks against current data, validation and a single database transaction containing the game changes and a new audit event.
- Keep the original event and snapshots immutable. Record revert/reapply as additional linked events. Distinguish active, inactive, superseded, conflicted and unavailable states rather than overwriting history.
- Preserve the existing Studio audit/backup databases and unknown original save times. A hotfix alone does not prove authorship or edit time.
- The current inactive tooltip previews retained item fields; referenced set/spell definitions use current server tables. Full historical dependency snapshots must be resolved before a reapply implementation can promise complete reproduction.

## Remaining tooltip accuracy work

- Compare representative items with this specific AnoCore client in game, including armor, weapons, ratings, procs and character-level behavior.
- Resolve unsupported instance bonus lists, random properties, conditional/nonlinear scaling and maximum durability using verified server/client rules.
- Keep missing originals and unsupported calculations explicit. Never silently substitute zero, raw allocation values or current state as the original.
