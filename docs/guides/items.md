# Edit and duplicate items

[Back to README](../../README.md) · [Gear sets](gear-sets.md) · [Weapon procs](weapon-procs.md)

## Open an item

1. Open **Item catalog** and search by item name, ID, stats or other fields. Narrow results with rarity, equipment slot, item type or armor subtype.
2. Double-click the item to open **Item editor**. You can also double-click an owned item or equipped slot in **Characters**, or use **Edit selected item** in **Gear designer**.
3. Confirm the name and item ID in the editor heading. Editing an existing definition changes that item for everyone on the server; it does not create a personal copy for one character.

![Item fields and before/draft tooltips](../screenshots/item-editor.png)

## Change fields and stats

1. Use **Item fields** for name, rarity, item level, required level, equipment slot, class, subclass/material, allowed classes and set membership.
2. Use **Stats** to select each stat type and edit its amount. For example, choose Intellect instead of Strength or critical strike rating instead of defense. These controls update the draft immediately.
3. Enter a **Preview level** and press **Apply level** to recalculate supported level-dependent values. A fixed-level item may keep the same base values. Unsupported contexts remain marked in the tooltip.
4. Compare **CURRENT · BEFORE SAVE** with **DRAFT · AFTER SAVE**. Enable **Show original** to compare against the native original when available.
5. Use **Check draft balance** for additional comparison. Item level affects native armor/damage/stat calculations; unsupported weapon-damage rows are blocked by the safety checks.
6. Press **Review & save**, inspect the change summary and confirm the reviewed save. **Reload / discard draft** returns to the saved definition.

## Make a separate item

Select a base and choose **Duplicate as new item**. Edit the duplicate and review/save it to create a separate server item ID. The base is preserved. Set creator also makes duplicates automatically; see the [set guide](gear-sets.md).

## Drop locations and history

Save or discard the item draft before **Edit drop locations…**. Choose world drops or instance/boss targets and review the plan before saving. Selected boss drops replace that item's configured world-drop plan.

Open **Change history** to inspect original and retained modified versions, revert a change or reapply it. Inactive entries can be removed from the visible history. Review any conflict message instead of overwriting a newer server change.

After saving, follow the editor's server reload instructions and verify the result in game. Character try-on is only a visual preview; it does not grant, move or equip real inventory items.

Screenshots show verification fixtures and drafts, not recommended item balance values.
