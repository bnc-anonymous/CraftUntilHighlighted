using System;
using ExileCore2;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace CraftUntilHighlighted;

/// <summary>
/// What the craft square needs between runs - where it goes and whether the item is highlighted - read at most once per
/// RefreshInterval instead of every frame. Drawing and the idle craft loop use it; a run reads the item itself every tick
/// and does not use this.
///
/// Each refresh reads only as far as it needs: with the stash closed, whether it is open; on another tab, the tab type;
/// on a currency tab, the item, the search text, the selected currency and the highlight. The cached square is empty when
/// no square should be drawn: no crafting item, an empty search box, or no currency from the Allowed currencies list
/// right-clicked for use (see SelectedCurrency).
///
/// Nothing a click depends on is taken from here. CraftLoop reads everything afresh before it starts a run.
/// </summary>
internal sealed class CraftingSlotCache
{
    /// <summary>
    /// How stale the cache may get. Chosen, not measured: 133 ms is 8 frames at 60 fps, short enough that the square
    /// appearing or changing colour after a search or a tab change looks immediate.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(133);

    private DateTime _refreshedAt = DateTime.MinValue;

    /// <summary>The craft square to draw, or empty when none should be drawn.</summary>
    public RectangleF Square { get; private set; }

    /// <summary>Whether the stash search highlighted the crafting item at the last refresh.</summary>
    public bool Highlighted { get; private set; }

    public void RefreshIfDue(GameController gc, CraftUntilHighlightedSettings settings)
    {
        if (DateTime.UtcNow - _refreshedAt >= RefreshInterval)
            Refresh(gc, settings);
    }

    /// <summary>Makes the next RefreshIfDue refresh, so the square reflects a run that has just ended.</summary>
    public void Expire() => _refreshedAt = DateTime.MinValue;

    private void Refresh(GameController gc, CraftUntilHighlightedSettings settings)
    {
        _refreshedAt = DateTime.UtcNow;
        Square = default;
        Highlighted = false;

        var item = CraftingSlot.Find(gc);

        if (item == null || string.IsNullOrEmpty(CraftingSlot.SearchTextOf(gc)) ||
            !SelectedCurrency.AllowedIsSelected(gc, settings))
            return;

        Square = CraftingSlot.CraftSquareOf(Safe.Read(item, static i => i.GetClientRectCache, default(RectangleF)),
            settings.Display.CraftSquareSizePercent.Value);
        Highlighted = CraftingSlot.SearchHighlightOf(item).Highlighted;
    }
}
