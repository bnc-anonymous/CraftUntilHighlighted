using System;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace CraftUntilHighlighted;

/// <summary>
/// Applies the selected orb to the crafting item until the stash search highlights it.
///
/// A run starts when every StartBlocker condition is clear: the plugin is enabled, ExileInput2 is running, the item is in
/// the crafting slot and not highlighted, the search box has text, a currency on the Allowed currencies list is
/// right-clicked for use (SelectedCurrency), the cursor is in the craft square, and the cursor has been outside the square
/// since the last run ended. The first run of a new install waits out the first-use countdown before it starts. A run
/// takes the cursor through ExileInput2, holds shift, waits a drawn shift lead, and clicks. Each click waits for
/// NormalInventoryItem.ItemVersion to change, which it did on every orb measured, including when the modifiers come out
/// the same, which a Chaos Orb can do. Then it checks the highlight, waits a drawn item changed delay, and clicks again.
///
/// The cursor is never moved. ExileInput2's own "player moved the mouse" stop compares the cursor with where ExileInput2
/// put it, and with no move sent it never fires (MousePointer.Interfered returns false with no sends). So the stop the
/// player reaches for is the craft square: the cursor leaving it ends the run and lets go of shift. That is also what
/// lets the player click by hand while shift is held, as the highlighted gate below relies on.
///
/// Never clicks without an allowed currency selected, checked again before every click: a shift-click on the item with
/// nothing selected moves the item out of the slot.
/// </summary>
internal sealed class CraftLoop
{
    private enum Phase
    {
        Idle,
        FirstUseCountdown,
        ShiftLead,
        AwaitingItemChange,
        ItemChangedDelay,
        HighlightedGate,
    }

    private static readonly Random Pick = new();

    /// <summary>
    /// Range of the time between pressing shift and a run's first click; each run draws a value in it. Chosen, not
    /// measured: long enough for the game to see shift held before the click.
    /// </summary>
    private const int ShiftLeadMinMs = 100;

    private const int ShiftLeadMaxMs = 200;

    /// <summary>How long the first-use countdown runs before the first run of a new install starts. Chosen.</summary>
    private static readonly TimeSpan FirstUseCountdownLength = TimeSpan.FromSeconds(5);

    private const string FirstUseMessage = "CraftUntilHighlighted: First time use, about to use currency...";


    private readonly ExileInput2Client _input;
    private readonly CraftingSlotCache _cache;

    private Phase _phase = Phase.Idle;
    private DateTime _phaseEndsAt;
    private DateTime _clickedAt;
    private int _versionBeforeClick;
    private int _orbsThisRun;
    /// <summary>The craft square as it was when the run started, used for the whole run. See TickRunning.</summary>
    private RectangleF _runSquare;

    /// <summary>Whether the cursor has been outside the square since the last run ended, so a run is not restarted in place.</summary>
    private bool _cursorLeftSinceLastRun;

    /// <summary>When the start conditions are next read while the cursor waits in the square. See TickIdle.</summary>
    private DateTime _nextStartCheckAt;

    /// <summary>Why the last start check did not start a run; empty when there was nothing worth saying.</summary>
    private string _startBlocker = "";

    public CraftLoop(ExileInput2Client input, CraftingSlotCache cache)
    {
        _input = input;
        _cache = cache;
    }

    public bool Running => _phase != Phase.Idle;

    /// <summary>The craft square the running run uses; empty when no run is running.</summary>
    public RectangleF RunSquare => Running ? _runSquare : default;

    /// <summary>Whether a run is holding shift on a highlighted item, waiting for the cursor to leave the square.</summary>
    public bool RunHighlighted => _phase == Phase.HighlightedGate;

    /// <summary>Whether the cursor was in the craft square at the last tick; false while no square is drawn.</summary>
    public bool CursorInSquare { get; private set; }

    /// <summary>The share of the first-use countdown still to run, from 1 to 0, or negative when it is not running.</summary>
    public double FirstUseCountdownLeft => _phase == Phase.FirstUseCountdown
        ? Math.Clamp((_phaseEndsAt - DateTime.UtcNow).TotalMilliseconds / FirstUseCountdownLength.TotalMilliseconds, 0d, 1d)
        : -1d;

    public void Tick(GameController gc, CraftUntilHighlightedSettings settings)
    {
        if (_phase == Phase.Idle)
            TickIdle(gc, settings);
        else
            TickRunning(gc, settings);
    }

    /// <summary>
    /// Between runs. The cursor is read every tick while the cached square is drawn, so the square's hover colour and a
    /// run's start follow the cursor at once; with no square drawn nothing is read. The start conditions are read afresh
    /// when the cursor enters the square, and again each CraftingSlotCache.RefreshInterval while it stays in it without
    /// starting, with the last reason kept on screen in between.
    /// </summary>
    private void TickIdle(GameController gc, CraftUntilHighlightedSettings settings)
    {
        var square = _cache.Square;
        var wasInSquare = CursorInSquare;

        CursorInSquare = square.Width > 0f && CraftingSlot.Contains(square, CraftingSlot.MouseOf(gc));

        if (!CursorInSquare)
        {
            _cursorLeftSinceLastRun = true;
            return;
        }

        var now = DateTime.UtcNow;

        if (!wasInSquare || now >= _nextStartCheckAt)
        {
            _nextStartCheckAt = now + CraftingSlotCache.RefreshInterval;

            // The cursor not having left the square since the last run is not said: the message the run ended with is
            // showing, and saying this would replace it.
            _startBlocker = !_cursorLeftSinceLastRun ? "" : TryStart(gc, settings) ?? "";
        }

        if (_phase == Phase.Idle && _startBlocker.Length > 0)
            CursorMessage.ShowWhile(CursorMessage.Kind.Error, _startBlocker);
    }

    /// <summary>
    /// During a run, every tick: the cursor, ExileInput2's stop, and whatever the phase waits on. A delay reads nothing
    /// else until it ends.
    ///
    /// A run uses the square it started with, not one read from the item each tick. The item element is rebuilt when an
    /// orb lands, and the rebuilt element can read as missing or with a rectangle somewhere else for a moment; either
    /// moved the square away from the cursor and stopped the run as "the cursor left the square" with the cursor still
    /// inside it (run of 2026-10-26 01:42:56, stopped 18 ms before that orb's ItemVersion change was seen; the dump after
    /// it had the cursor inside the square). The slot does not move while the stash is open.
    /// </summary>
    private void TickRunning(GameController gc, CraftUntilHighlightedSettings settings)
    {
        CursorInSquare = CraftingSlot.Contains(_runSquare, CraftingSlot.MouseOf(gc));

        if (_phase == Phase.FirstUseCountdown)
        {
            TickFirstUseCountdown(gc, settings);
            return;
        }

        // Every phase of a run ends on these.
        if (!CursorInSquare)
        {
            if (_phase == Phase.HighlightedGate)
                Stop(CursorMessage.Kind.Success, $"Highlighted: {_orbsThisRun} orbs");
            else
                Stop(CursorMessage.Kind.Info, $"Stopped: {_orbsThisRun} orbs");
            return;
        }

        if (_input.Stopped() is { Length: > 0 } stopped)
        {
            Stop(CursorMessage.Kind.Error, $"ExileInput2 stopped: {stopped}");
            return;
        }

        var crafting = settings.Crafting;

        switch (_phase)
        {
            case Phase.ShiftLead:
            case Phase.ItemChangedDelay:
                if (DateTime.UtcNow < _phaseEndsAt)
                    return;

                ClickWhenSafe(gc, settings, CraftingSlot.Find(gc));
                return;

            case Phase.AwaitingItemChange:
            {
                // The item element is rebuilt when an orb lands, so it can read as missing for a moment.
                var item = CraftingSlot.Find(gc);
                var version = Safe.Read(item, static i => i.ItemVersion, _versionBeforeClick);

                if (item == null || version == _versionBeforeClick)
                {
                    if (DateTime.UtcNow - _clickedAt > TimeSpan.FromMilliseconds(crafting.ItemChangedTimeoutMs.Value))
                        Stop(CursorMessage.Kind.Error, ItemDidNotChangeMessage(_orbsThisRun));

                    return;
                }

                _orbsThisRun++;
                AfterItemChanged(settings, item);
                return;
            }

            case Phase.HighlightedGate:
            {
                // Shift is still held. A click of the player's own may roll the highlighted modifier away, in which case
                // the run carries on as if that orb had been its own.
                var item = CraftingSlot.Find(gc);

                if (item != null && !CraftingSlot.SearchHighlightOf(item).Highlighted)
                {
                    CursorMessage.Show(CursorMessage.Kind.Info, "Highlight gone - continuing");
                    BeginItemChangedDelay(crafting);
                }
                else
                {
                    // Kept up while shift is held, so the player knows a click of theirs will apply the orb.
                    CursorMessage.ShowWhile(CursorMessage.Kind.Success, $"Highlighted: {_orbsThisRun} orbs - shift held");
                }

                return;
            }
        }
    }

    /// <summary>
    /// The first run of a new install waits FirstUseCountdownLength with a warning and a bar under the cursor, holding no
    /// input, so a player who did not know what the square does can move away before anything is clicked. Leaving the
    /// square cancels it; once it runs out, Debug > Enable first time use warning is turned off so it is not shown again,
    /// and the run starts.
    /// </summary>
    private void TickFirstUseCountdown(GameController gc, CraftUntilHighlightedSettings settings)
    {
        if (!CursorInSquare)
        {
            _phase = Phase.Idle;
            _cache.Expire();
            CursorMessage.Show(CursorMessage.Kind.Info, "Cancelled");
            return;
        }

        if (DateTime.UtcNow < _phaseEndsAt)
        {
            CursorMessage.ShowWhile(CursorMessage.Kind.Info, FirstUseMessage);
            return;
        }

        settings.Debug.EnableFirstUseWarning.Value = false;
        _phase = Phase.Idle;
        _startBlocker = TryStart(gc, settings) ?? "";
    }

    /// <summary>
    /// Reads the start conditions afresh and starts a run if they are met. Returns null when a run started, otherwise why
    /// not (empty for a reason not worth saying), which TickIdle shows while the cursor stays in the square.
    /// </summary>
    private string TryStart(GameController gc, CraftUntilHighlightedSettings settings)
    {
        var item = CraftingSlot.Find(gc);

        if (StartBlocker(gc, settings, item) is { } blocker)
            return blocker;

        var square = CraftingSlot.CraftSquareOf(Safe.Read(item, static i => i.GetClientRectCache, default(RectangleF)),
            settings.Display.CraftSquareSizePercent.Value);

        if (!CraftingSlot.Contains(square, CraftingSlot.MouseOf(gc)))
            return "";

        if (settings.Debug.EnableFirstUseWarning)
        {
            _runSquare = square;
            _cursorLeftSinceLastRun = false;
            _phase = Phase.FirstUseCountdown;
            _phaseEndsAt = DateTime.UtcNow + FirstUseCountdownLength;
            CursorMessage.ShowWhile(CursorMessage.Kind.Info, FirstUseMessage);
            return null;
        }

        // The text cursor is usually still in the search box after typing the search, which ExileInput2 treats as the
        // game taking typed input and refuses. Focus inside the search bar is allowed for this plugin: it sends shift,
        // which types nothing, and clicks on the item, not the box. Focus anywhere else, chat included, still blocks it.
        // Named afresh on every start because the bar's element can be rebuilt when the stash is reopened.
        if (CraftingSlot.SearchBarAddressOf(gc) is var searchBar and not 0L)
            _input.AllowFocusUnder([searchBar]);

        if (!_input.Take())
        {
            var why = _input.Stopped();

            // A refusal with a stop reason (the game taking typed input, say, while the search box still has focus)
            // stands until it is acknowledged, so it is released here and the next tick asks again.
            if (why.Length > 0)
                _input.Release();

            return why.Length > 0 ? $"ExileInput2: {why}" : "ExileInput2 busy";
        }

        if (!_input.Hold(Keys.LShiftKey))
        {
            _input.Release();
            return "ExileInput2 would not hold shift";
        }

        _orbsThisRun = 0;
        _runSquare = square;
        _cursorLeftSinceLastRun = false;
        _phase = Phase.ShiftLead;
        _phaseEndsAt = DateTime.UtcNow + DrawMs(ShiftLeadMinMs, ShiftLeadMaxMs);
        CursorMessage.Show(CursorMessage.Kind.Info, "Crafting");
        return null;
    }

    /// <summary>
    /// Why a run cannot start now, or null when it can; empty for a reason not worth saying. Checked in this order.
    /// </summary>
    private string StartBlocker(GameController gc, CraftUntilHighlightedSettings settings, NormalInventoryItem item)
    {
        if (!settings.Enable)
            return "";

        if (item == null)
            return "No crafting item";

        // Not said: the craft square is not drawn without a search, so the cursor is not plainly in it.
        if (string.IsNullOrEmpty(CraftingSlot.SearchTextOf(gc)))
            return "";

        if (CraftingSlot.SearchHighlightOf(item).Highlighted)
            return "Already highlighted";

        // Not said: the craft square is not drawn without an allowed currency selected either.
        if (!SelectedCurrency.AllowedIsSelected(gc, settings))
            return "";

        if (!_input.Available)
            return "ExileInput2 not running";

        return null;
    }

    /// <summary>
    /// Clicks if the orb is still selected and the game says the cursor is over the item; otherwise waits for that, and
    /// stops after the item changed timeout. UIHover can name the old element for a moment after an orb rebuilds the item.
    /// </summary>
    private void ClickWhenSafe(GameController gc, CraftUntilHighlightedSettings settings, NormalInventoryItem item)
    {
        var timeout = TimeSpan.FromMilliseconds(settings.Crafting.ItemChangedTimeoutMs.Value);

        string NotReady()
        {
            if (item == null)
                return "No crafting item";

            if (!SelectedCurrency.AllowedIsSelected(gc, settings))
                return OrbSelected(gc) ? "Currency not allowed" : "No orb selected";

            var hovered = Safe.Read(gc, static g => g.IngameState.UIHover?.Address ?? 0L, 0L);

            return hovered != 0L && hovered == Safe.Read(item, static i => i.Address, -1L)
                ? null
                : "Cursor not over the item";
        }

        // Checked again here, at the end of the item changed delay, in case the highlight lagged behind the item changing: a match
        // seen only now must not be clicked past.
        if (item != null && CraftingSlot.SearchHighlightOf(item).Highlighted)
        {
            OnHighlighted();
            return;
        }

        if (NotReady() is { } why)
        {
            if (DateTime.UtcNow - _phaseEndsAt > timeout)
                Stop(CursorMessage.Kind.Error, why);

            return;
        }

        _versionBeforeClick = Safe.Read(item, static i => i.ItemVersion, int.MinValue);

        // Shift is held through Hold, so the click itself names no modifiers; naming shift here would have the stroke
        // press and release it around the click and drop the orb's selection.
        if (!_input.Click())
        {
            if (DateTime.UtcNow - _phaseEndsAt > timeout)
                Stop(CursorMessage.Kind.Error, "ExileInput2 refused the click");

            return;
        }

        _clickedAt = DateTime.UtcNow;
        _phase = Phase.AwaitingItemChange;
    }

    private void AfterItemChanged(CraftUntilHighlightedSettings settings, NormalInventoryItem item)
    {
        var crafting = settings.Crafting;

        if (CraftingSlot.SearchHighlightOf(item).Highlighted)
        {
            OnHighlighted();
            return;
        }

        if (crafting.MaximumOrbsPerRun.Value > 0 && _orbsThisRun >= crafting.MaximumOrbsPerRun.Value)
        {
            Stop(CursorMessage.Kind.Info, $"Maximum reached: {_orbsThisRun} orbs");
            return;
        }

        BeginItemChangedDelay(crafting);
        CursorMessage.Show(CursorMessage.Kind.Info, $"Crafting: {_orbsThisRun} orbs");
    }

    /// <summary>
    /// Keeps shift held, with the orb still selected, until the cursor leaves the craft square, so the player can click
    /// to apply the orb again by hand; if that removes the highlight, the run carries on.
    /// </summary>
    private void OnHighlighted()
    {
        _phase = Phase.HighlightedGate;
        CursorMessage.ShowWhile(CursorMessage.Kind.Success, $"Highlighted: {_orbsThisRun} orbs - shift held");
    }

    private void BeginItemChangedDelay(CraftingSettings crafting)
    {
        _phase = Phase.ItemChangedDelay;
        _phaseEndsAt = DateTime.UtcNow + DrawMs(crafting.ItemChangedMinDelayMs.Value, crafting.ItemChangedMaxDelayMs.Value);
    }

    /// <summary>
    /// Ends the run, letting go of shift and the cursor, and says why at the cursor and in the log. Does nothing when no
    /// run is going, so a stop from outside cannot replace the message a run ended with.
    /// </summary>
    public void Stop(CursorMessage.Kind kind, string why)
    {
        if (_phase == Phase.Idle)
            return;

        // The first-use countdown holds no input, so there is nothing to let go of.
        if (_phase != Phase.FirstUseCountdown)
            Safe.Read(_input, static i => i.Release(), false);
        DebugWindow.LogMsg($"[CraftUntilHighlighted] {why}", 5f);
        CursorMessage.Show(kind, why);

        _phase = Phase.Idle;
        _cache.Expire();
    }

    /// <summary>Whether an orb is selected for use: Cursor.ActionString read "reroll" with a Chaos Orb and "" with nothing.</summary>
    private static bool OrbSelected(GameController gc) =>
        !string.IsNullOrEmpty(Safe.Read(gc, static g => g.IngameState.IngameUi.Cursor.ActionString, ""));

    /// <summary>
    /// Why a run stopped when a click did not change the item. After some orbs, the stack has most likely run out; before
    /// any, the currency most likely cannot be used on this item, which the game refuses without changing it.
    /// </summary>
    private static string ItemDidNotChangeMessage(int orbsUsed) => orbsUsed > 0
        ? $"Item did not change. Used {orbsUsed} {(orbsUsed == 1 ? "orb" : "orbs")}, did we run out?"
        : "Item did not change. Used 0 orbs, is the orb valid for this item?";

    /// <summary>A uniform draw between two values in milliseconds, either way round.</summary>
    private static TimeSpan DrawMs(int a, int b)
    {
        var (low, high) = a <= b ? (a, b) : (b, a);

        return TimeSpan.FromMilliseconds(Pick.Next(low, high + 1));
    }
}
