using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ExileCore2;
using ExileCore2.PoEMemory.Models;

namespace CraftUntilHighlighted;

/// <summary>
/// Which currency is right-clicked for use, and whether it is on the Allowed currencies list.
///
/// ExileCore2 does not name the selection. A right-clicked stack stays where it is and the Cursor1 inventory stays
/// empty; IngameUi.Cursor.ActionString is per kind of action, the same "reroll" for Chaos, Greater Chaos and Perfect
/// Chaos Orbs; and IngameUi.Cursor.Action holds the selected stack's ItemVersion, not its kind (dumps 2026-10-26 and
/// 2026-10-27). So the selection comes from the selected stack's item type hash, read off the cursor element at the offset
/// in Offsets (as OffsetRepair has it this session), looked up with Files.BaseItemTypes.GetByHash and taken only when it
/// names a currency (metadata under Metadata/Items/Currency/). A selection that cannot be named is never allowed.
/// </summary>
internal static class SelectedCurrency
{
    private const string CurrencyPath = "Metadata/Items/Currency/";

    /// <summary>
    /// How long one Allowed currencies pattern may take to match before it counts as no match. Chosen: far longer than a
    /// name or a metadata path takes, short enough that a pattern that backtracks badly cannot stall a frame.
    /// </summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(20);

    /// <summary>Each Allowed currencies line as a compiled pattern, or null where it is not a valid regular expression.</summary>
    private static readonly Dictionary<string, Regex> PatternOfLine = new(StringComparer.Ordinal);

    /// <summary>The item type of the currency right-clicked for use, or null when none is selected or it cannot be named.</summary>
    public static BaseItemType ItemTypeIfSelected(GameController gc)
    {
        if (string.IsNullOrEmpty(Safe.Read(gc, static g => g.IngameState.IngameUi.Cursor?.ActionString, "")))
            return null;

        // Naming no currency while one is selected is how a moved offset shows; OffsetRepair looks for it once per selection.
        return CurrencyOfHash(gc, ItemTypeHashOnCursor(gc)) ??
               CurrencyOfHash(gc, OffsetRepair.FindCursorSelectedItemTypeHash(gc,
                   h => Safe.Read(CurrencyOfHash(gc, h), static t => t?.Metadata, null)));
    }

    /// <summary>Whether a currency is right-clicked for use and it is on the Allowed currencies list.</summary>
    public static bool AllowedIsSelected(GameController gc, CraftUntilHighlightedSettings settings) =>
        IsAllowed(ItemTypeIfSelected(gc), settings);

    /// <summary>
    /// Whether an item type is on the Allowed currencies list: some line, as a regular expression ignoring case, matches
    /// the whole of its base name or the whole of its metadata path. Whole, so "Chaos Orb" does not also allow the Perfect
    /// Chaos Orb; ".* Catalyst" allows every catalyst. A line that is not a valid regular expression matches nothing.
    /// </summary>
    public static bool IsAllowed(BaseItemType itemType, CraftUntilHighlightedSettings settings)
    {
        var name = Safe.Read(itemType, static t => t?.BaseName, null);
        var metadata = Safe.Read(itemType, static t => t?.Metadata, null);

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(metadata))
            return false;

        foreach (var line in settings.AllowedCurrencies.Content)
        {
            if (PatternOf(line?.Value) is not { } pattern)
                continue;

            if (WholeMatch(pattern, name) || WholeMatch(pattern, metadata))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The uint at OffsetRepair.CursorSelectedItemTypeHashOffset in the cursor element: the selected stack's item type
    /// hash while a currency is selected (see Offsets). 0 when it cannot be read.
    /// </summary>
    public static uint ItemTypeHashOnCursor(GameController gc)
    {
        var memory = Safe.Read(gc, static g => g.Memory, null);
        var cursorAddress = Safe.Read(gc, static g => g.IngameState.IngameUi.Cursor.Address, 0L);

        return memory == null || cursorAddress == 0L
            ? 0u
            : Safe.Read(() => memory.Read<uint>(cursorAddress + OffsetRepair.CursorSelectedItemTypeHashOffset), 0u);
    }

    /// <summary>The item type a BaseItemTypes hash names, or null.</summary>
    public static BaseItemType ItemTypeOfHash(GameController gc, uint hash) =>
        hash == 0u ? null : Safe.Read(() => gc.Files.BaseItemTypes.GetByHash(hash), null);

    /// <summary>The currency item type a hash names, or null when it names nothing or something that is not currency.</summary>
    private static BaseItemType CurrencyOfHash(GameController gc, uint hash) =>
        ItemTypeOfHash(gc, hash) is { } itemType &&
        Safe.Read(itemType, static t => t.Metadata, "").StartsWith(CurrencyPath, StringComparison.Ordinal)
            ? itemType
            : null;

    /// <summary>A line's pattern, anchored to match whole, compiled once and kept; null for a blank or invalid line.</summary>
    private static Regex PatternOf(string line)
    {
        var text = line?.Trim();

        if (string.IsNullOrEmpty(text))
            return null;

        if (PatternOfLine.TryGetValue(text, out var known))
            return known;

        Regex pattern;

        try
        {
            pattern = new Regex($"^(?:{text})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternTimeout);
        }
        catch (ArgumentException)
        {
            pattern = null;
        }

        PatternOfLine[text] = pattern;
        return pattern;
    }

    private static bool WholeMatch(Regex pattern, string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
