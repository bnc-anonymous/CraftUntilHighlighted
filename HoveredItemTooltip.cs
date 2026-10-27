using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace CraftUntilHighlighted;

/// <summary>
/// Where the item description panel the game is showing sits on screen, so the craft square is not drawn over it.
///
/// The panel is the hovered item's Tooltip, with the hovered item read from UIHoverElement rather than UIHover:
/// UIHover reads nothing while Alt is held for the advanced description, though the panel is still shown. While Alt is
/// held the game keeps the description of the item it showed open wherever the cursor goes, including over other items,
/// while UIHoverElement follows the cursor, so the item shown when Alt went down is kept until Alt is let go. Read in
/// NinjaPricer with the stash open, 2026-10-27; the panel element itself still reads visible after the hover ends, so
/// its own IsVisible cannot say whether it is drawn.
/// </summary>
internal sealed class HoveredItemTooltip
{
    private NormalInventoryItem _itemShownWhileAltHeld;

    /// <summary>The panel's rectangle at the last Refresh, or empty when no item description is shown.</summary>
    public RectangleF Rect { get; private set; }

    public void Refresh(GameController gc)
    {
        var altHeld = Input.IsKeyDown(Keys.Menu);
        var item = altHeld && _itemShownWhileAltHeld != null ? _itemShownWhileAltHeld : HoveredItemOf(gc);

        _itemShownWhileAltHeld = altHeld ? item : null;
        Rect = Safe.Read(item, static i => i?.Tooltip is { Address: not 0, IsVisible: true } tooltip
            ? tooltip.GetClientRectCache
            : default, default(RectangleF));
    }

    public void Clear()
    {
        _itemShownWhileAltHeld = null;
        Rect = default;
    }

    /// <summary>Whether a rectangle overlaps the item description panel shown at the last Refresh.</summary>
    public bool Covers(RectangleF rect) => Rect.Width > 0f && Rect.Intersects(rect);

    private static NormalInventoryItem HoveredItemOf(GameController gc)
    {
        var item = Safe.Read(gc, static g => g.IngameState.UIHoverElement?.AsObject<NormalInventoryItem>(), null);

        return Safe.Read(item, static i => i != null && i.Address != 0 && i.Item is { Address: not 0, IsValid: true }, false)
            ? item
            : null;
    }
}
