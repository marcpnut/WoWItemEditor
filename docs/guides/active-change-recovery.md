# Recover active changes after a server upgrade

An upgrade can replace live game tables without changing the editor's separate backup database. The history entries may still say **Active** even though the server no longer contains the edited definitions.

1. Open **Change history**, then **Item, set-bonus & weapon-proc edits**.
2. Select **Reapply all active changes...**.
3. Select **Scan / review active changes**. Wait for the checks and review the entries and notes. The scan rolls back its trial changes.
4. Select **Apply reviewed recovery**. It restores all retained active item edits, duplicated items, created sets, set membership, set bonuses, weapon procs and managed drop locations. Search filters do not limit recovery. Inactive history entries stay inactive.
5. Restart the worldserver and verify the result in game.

Recovery replays saved changes in order, so later edits determine the final configuration. Superseded private bonus versions that violate current safety limits remain in history but are not activated when the final assignments no longer use them. Final item values and referenced effects are checked before committing. Managed drop eligibility is rebuilt against the current world when necessary.

The entire recovery is one database transaction: a failure rolls it back. Incompatible table schemas, conflicting custom IDs, unsafe final values or changed data since review block recovery. Resolve the reported issue and scan again.

Each successful recovery retains the previous live snapshots and original history metadata in `item_editor_recovery_batches` in the configured backup database. Existing history remains available and active. Keep an independent server backup as well. Quest/NPC records and character inventories are outside this recovery action.
