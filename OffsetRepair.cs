using System;
using ExileCore2;

namespace CraftUntilHighlighted;

/// <summary>
/// The offsets in Offsets as this session uses them, and the search that finds one again after a patch has moved it, so
/// the plugin keeps working until Offsets is updated.
///
/// A moved offset reads some other number, which is very unlikely to be the hash of any currency. So when a currency is
/// selected and CursorSelectedBaseItemTypeHashOffset names none, the cursor element is searched for 4-byte values that name
/// a currency, once per selection. If they all name one currency, the first offset it was found at becomes
/// CursorSelectedBaseItemTypeHashOffset for the rest of the session, and the move is logged once. If they name two, or none,
/// nothing changes, the selection goes unnamed, and the craft square does not appear. The dump's Cursor section shows
/// the offset in use, what it named, and what the last search did.
/// </summary>
internal static class OffsetRepair
{
    /// <summary>
    /// How much of the cursor element is searched. Chosen, not measured: the same window as the dump's RawCursorProbe,
    /// which covers the hash's offset in ExileCore2_260907's client (0x484) with about 380 bytes after it. How large the
    /// cursor element is ExileCore2 does not say; a window past its end reads other memory, where a second currency hash
    /// would make the search refuse rather than guess.
    /// </summary>
    private const int SearchBytes = 0x600;

    /// <summary>The Cursor.Action value last searched for, so one selection is searched once, not on every read.</summary>
    private static int _searchedForAction = int.MinValue;

    /// <summary>
    /// Where the selected base item type hash is read: Offsets.CursorSelectedBaseItemTypeHash, or where the last search found it
    /// this session.
    /// </summary>
    public static int CursorSelectedBaseItemTypeHashOffset { get; private set; } = Offsets.CursorSelectedBaseItemTypeHash;

    /// <summary>What the last search did, for the dump. Empty until one has run.</summary>
    public static string LastSearch { get; private set; } = "";

    /// <summary>
    /// Searches the cursor element, 4 bytes at a time, for the selected stack's base item type hash, after
    /// CursorSelectedBaseItemTypeHashOffset read none. When every value found names the same currency, moves
    /// CursorSelectedBaseItemTypeHashOffset to the first and returns its hash; otherwise, or when this selection was searched
    /// already, returns 0. Up to SearchBytes / 4 dictionary lookups. currencyMetadataOfHash returns the metadata of the
    /// currency a hash names, or null for anything else.
    /// </summary>
    public static uint FindCursorSelectedBaseItemTypeHash(GameController gc, Func<uint, string> currencyMetadataOfHash)
    {
        var action = Safe.Read(gc, static g => (int)g.IngameState.IngameUi.Cursor.Action, 0);

        if (action == _searchedForAction)
            return 0u;

        _searchedForAction = action;

        var memory = Safe.Read(gc, static g => g.Memory, null);
        var cursorAddress = Safe.Read(gc, static g => g.IngameState.IngameUi.Cursor.Address, 0L);
        var bytes = memory == null || cursorAddress == 0L
            ? null
            : Safe.Read(() => memory.ReadBytes(cursorAddress, SearchBytes), null);

        if (bytes == null || bytes.Length < SearchBytes)
            return 0u;

        var failedOffset = CursorSelectedBaseItemTypeHashOffset;
        string foundMetadata = null;
        var foundHash = 0u;
        var foundAt = -1;

        for (var offset = 0; offset < SearchBytes; offset += 4)
        {
            var value = BitConverter.ToUInt32(bytes, offset);

            if (currencyMetadataOfHash(value) is not { } metadata)
                continue;

            if (foundMetadata == null)
            {
                foundMetadata = metadata;
                foundHash = value;
                foundAt = offset;
            }
            else if (metadata != foundMetadata)
            {
                LastSearch = $"+0x{failedOffset:X} named no currency; the cursor holds more than one " +
                             $"(+0x{foundAt:X} and +0x{offset:X}), so nothing was taken";
                return 0u;
            }
        }

        if (foundMetadata == null)
        {
            LastSearch = $"+0x{failedOffset:X} named no currency, and nothing else in the cursor did";
            return 0u;
        }

        CursorSelectedBaseItemTypeHashOffset = foundAt;
        LastSearch = $"+0x{failedOffset:X} named no currency; moved to +0x{foundAt:X} ({foundMetadata})";
        DebugWindow.LogMsg($"[CraftUntilHighlighted] The selected currency's offset looked moved by a patch: {LastSearch}. " +
                           "Update Offsets.CursorSelectedBaseItemTypeHash.", 30f);

        return foundHash;
    }
}
