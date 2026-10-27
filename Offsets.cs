namespace CraftUntilHighlighted;

/// <summary>
/// Every offset this plugin reads the client's memory at that ExileCore2 does not expose, and how to repair them after a
/// patch. The same arrangement as AutoExpedition's Offsets. Anything ExileCore2 already reads through a property is read
/// through that property and is not here.
///
/// ExileCore2's Cursor gives Action, Clicks, ActionString and ActionSound, and its internal cursor structure, whose
/// field offsets its protected GameOffsets2 scrambles, surfaces nothing else (checked against ExileCore2_260907,
/// 2026-10-27). Everything else this plugin reads goes through ExileCore2's properties, which its maintainer keeps
/// working across patches; these are ours to maintain.
///
/// ## What they are
///
/// While a currency is right-clicked for use, the cursor element holds the hash of the selected stack's item type, which
/// Files.BaseItemTypes.GetByHash turns into a BaseItemType with a name. Measured in dumps 2026-10-27 10:51 to 10:58: the
/// hash was 0x7353DDF9 for every Chaos Orb stack (one in the currency tab, two in the inventory, before and after the
/// tab's stack changed), 0x4D3DD7C9 for the Greater Chaos Orb and 0xE1F44ED0 for the Perfect Chaos Orb, and GetByHash
/// named each correctly. With a Chaos Orb selected it was the only 4-byte value in the element's first 0x600 bytes that
/// named any item type (dump 2026-10-27 11:13:33). Read by SelectedCurrency.
///
/// ## When one breaks
///
/// OffsetRepair notices a moved offset and, where it can, finds it again for the session and logs the new offset; copy
/// it here so the next session starts with it. By hand: right-click a currency, press the dump key, and read "Raw cursor
/// memory" (RawCursorProbe), which lists every 4 bytes of the cursor element that name an item type; the offset whose
/// line names the selected currency, and changes when another is selected, is CursorSelectedItemTypeHash.
/// </summary>
internal static class Offsets
{
    /// <summary>The cursor element + this is a uint: the selected stack's BaseItemType hash, for BaseItemTypes.GetByHash.</summary>
    public const int CursorSelectedItemTypeHash = 0x484;
}
