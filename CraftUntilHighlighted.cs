using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.Shared.Nodes;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace CraftUntilHighlighted;

/// <summary>
/// Applies a currency orb to the item in the currency tab's crafting slot until the game's stash search highlights it.
///
/// The intended use: type a regex for the wanted modifiers into the stash search, right-click a currency on the Allowed
/// currencies list, and move the cursor into the craft square at the bottom-left of the item. The plugin then holds shift
/// and left-clicks, waits for the item to change and a randomised item changed delay, and clicks again, until the search
/// highlights the item. All input goes through ExileInput2.
///
/// The run itself is CraftLoop. How the search highlight, a selected orb and an applied orb show in the client was read
/// off dumps first; see Dump and STATE.md.
/// </summary>
public class CraftUntilHighlighted : BaseSettingsPlugin<CraftUntilHighlightedSettings>
{
    /// <summary>
    /// The ExileInput2 bridge version this plugin needs (ExileInput2.BridgeVersion). 1 is the first with a version and
    /// has Hold and Click, which are all the craft loop will use.
    /// </summary>
    public const int ExileInput2VersionNeeded = 1;

    /// <summary>How long after loading ExileInput2 has to answer before its absence is reported.</summary>
    private static readonly TimeSpan InputMissingReportedAfter = TimeSpan.FromSeconds(15);

    private ExileInput2Client _input;
    private CraftLoop _craftLoop;
    private CraftingSlotCache _craftingSlotCache;
    private readonly HoveredItemTooltip _hoveredItemTooltip = new();
    private readonly CallTimeAverage _tickTime = new();
    private readonly CallTimeAverage _renderTime = new();
    private DateTime _loadedAt;
    private bool _inputCheckSaid;

    public override bool Initialise()
    {
        _loadedAt = DateTime.UtcNow;
        DumpFolder.ConfigDirectory = ConfigDirectory;
        _input = new ExileInput2Client(GameController, "CraftUntilHighlighted");
        _craftingSlotCache = new CraftingSlotCache();
        _craftLoop = new CraftLoop(_input, _craftingSlotCache);

        // ListNode choices are not saved, so they are supplied on every start. Selecting a preset sets the ranges.
        var preset = Settings.Crafting.TimingPreset;
        preset.SetListValues(TimingPresetRanges.Names);
        preset.OnValueSelected += name => TimingPresetRanges.Named(name)?.ApplyTo(Settings.Crafting);

        RegisterHotkey(Settings.Debug.DumpHotkey);
        ApplySettingsChanges();

        return true;
    }

    /// <summary>The ConfigVersion a saved file reaches once every step below has run.</summary>
    private const int CurrentConfigVersion = 0;

    /// <summary>
    /// Brings a saved settings file up to date with changed defaults, once per change. No default has changed since the
    /// plugin was published, so there are no steps yet; the first adds one and raises CurrentConfigVersion to 1.
    /// </summary>
    private void ApplySettingsChanges()
    {
        if (Settings.ConfigVersion >= CurrentConfigVersion)
            return;

        Settings.ConfigVersion = CurrentConfigVersion;
    }

    /// <summary>
    /// Tells Input about a hotkey so PressedOnce answers, and again whenever it is rebound in the menu. A key left unbound
    /// is not registered, since registering None would claim a key nobody pressed.
    /// </summary>
    private static void RegisterHotkey(HotkeyNodeV2 hotkey)
    {
        Bind(hotkey);
        hotkey.OnValueChanged += () => Bind(hotkey);
    }

    private static void Bind(HotkeyNodeV2 hotkey)
    {
        if (Safe.Read(hotkey, static h => h.Value.Key, Keys.None) != Keys.None)
            Input.RegisterKey(hotkey.Value);
    }

    public override void Tick()
    {
        // The dump answers with the plugin disabled too: what it is for is reading the client before the plugin does
        // anything with it.
        if (Settings.Debug.DumpHotkey.PressedOnce() || Dump.AskedForInSettings)
        {
            Dump.AskedForInSettings = false;
            Dump.Write(this, _input);
        }

        var startedAt = Stopwatch.GetTimestamp();

        ShowCustomWhenRangesEdited();

        if (!Settings.Enable)
        {
            if (_craftLoop.Running)
                _craftLoop.Stop(CursorMessage.Kind.Info, "Stopped: plugin disabled");

            return;
        }

        ReportInputOnce();

        // Between runs the square comes from the cache; a run reads the item itself every tick.
        if (!_craftLoop.Running)
            _craftingSlotCache.RefreshIfDue(GameController, Settings);

        _craftLoop.Tick(GameController, Settings);

        // Read only while a craft square is drawn, which is the only thing it is for.
        if (_craftLoop.Running || _craftingSlotCache.Square.Width > 0f)
            _hoveredItemTooltip.Refresh(GameController);
        else
            _hoveredItemTooltip.Clear();

        _tickTime.AddSince(startedAt);
    }

    /// <summary>Shows Custom in the preset dropdown once the ranges no longer match the preset it names.</summary>
    private void ShowCustomWhenRangesEdited()
    {
        var crafting = Settings.Crafting;

        if (TimingPresetRanges.Named(crafting.TimingPreset.Value) is { } named && !named.Matches(crafting))
            crafting.TimingPreset.Value = TimingPresetRanges.Custom;
    }

    public override void AreaChange(AreaInstance area) => _craftLoop?.Stop(CursorMessage.Kind.Info, "Stopped: area changed");

    /// <summary>
    /// Says once in the log when ExileInput2 is missing or older than this plugin needs. Waits InputMissingReportedAfter
    /// from loading, because the two plugins load in an order nobody controls.
    /// </summary>
    private void ReportInputOnce()
    {
        if (_inputCheckSaid || DateTime.UtcNow - _loadedAt < InputMissingReportedAfter)
            return;

        _inputCheckSaid = true;

        if (!_input.Available)
        {
            LogError("CraftUntilHighlighted: ExileInput2 is not running. Install and enable it - this plugin sends no " +
                     "input of its own.", 30f);
            CursorMessage.Show(CursorMessage.Kind.Error, "CraftUntilHighlighted: ExileInput2 not found");
            return;
        }

        if (_input.Version() is var version and >= 0 && version < ExileInput2VersionNeeded)
        {
            LogError($"CraftUntilHighlighted: ExileInput2 is out of date: bridge version {version}, this plugin needs " +
                     $"{ExileInput2VersionNeeded} or later.", 30f);
            CursorMessage.Show(CursorMessage.Kind.Error, "CraftUntilHighlighted: ExileInput2 is out of date");
        }
    }

    public override void Render()
    {
        // Before the early return, so a message raised as the plugin is disabled is still seen.
        var message = CursorMessage.Draw(Graphics, GameController, Settings.Display.CursorMessageSeconds.Value);

        if (Settings.Enable)
            DrawFirstUseCountdown(message);

        if (Settings.Enable)
        {
            var startedAt = Stopwatch.GetTimestamp();
            DrawCraftSquare();
            _renderTime.AddSince(startedAt);
        }

        if (Settings.Debug.ShowOverlay)
            RenderDebug();
    }

    /// <summary>
    /// The first-use countdown's bar, under its message: AutoExpedition's hold bar (80 by 6 px, a black edge, full when it
    /// starts and empty when it ends), in its green.
    /// </summary>
    private void DrawFirstUseCountdown(RectangleF message)
    {
        var left = _craftLoop.FirstUseCountdownLeft;

        if (left < 0d || message.Width <= 0f)
            return;

        const float wide = 80f;
        const float high = 6f;
        var at = new Vector2(message.X, message.Bottom + 4f);

        Graphics.DrawBox(new RectangleF(at.X - 1f, at.Y - 1f, wide + 2f, high + 2f), Color.Black);
        Graphics.DrawBox(new RectangleF(at.X, at.Y, wide * (float)left, high), Color.FromArgb(230, 90, 255, 120));
    }

    /// <summary>
    /// Draws the craft square without reading the game: between runs from CraftingSlotCache and the cursor test the
    /// loop's tick made, during a run from the run's locked square. The item element is rebuilt by every orb and can read
    /// as missing, or with a rectangle elsewhere, for a frame, so a square read from it during a run would blink or jump.
    /// Not drawn while it overlaps an item description panel (HoveredItemTooltip), which the HUD would draw it over.
    /// </summary>
    private void DrawCraftSquare()
    {
        var display = Settings.Display;

        if (_craftLoop.Running)
        {
            if (_hoveredItemTooltip.Covers(_craftLoop.RunSquare))
                return;

            // The cursor is in the run's square while it runs; leaving it ends the run on the next tick.
            Graphics.DrawFrame(_craftLoop.RunSquare,
                _craftLoop.RunHighlighted ? display.CraftSquareHighlightedColour : display.CraftSquareHoveredColour, 2);
            return;
        }

        var square = _craftingSlotCache.Square;

        if (square.Width <= 0f || _hoveredItemTooltip.Covers(square))
            return;

        var colour = _craftingSlotCache.Highlighted ? display.CraftSquareHighlightedColour
            : _craftLoop.CursorInSquare ? display.CraftSquareHoveredColour
            : display.CraftSquareColour;

        Graphics.DrawFrame(square, colour, 2);
    }

    /// <summary>
    /// Writes the plugin's average Tick and Render times above the cursor, and while the stash is open frames the crafting
    /// item and the element under the cursor with a status line. Reads the game every frame, so it is not part of the
    /// Render time it shows.
    /// </summary>
    private void RenderDebug()
    {
        var mouse = CraftingSlot.MouseOf(GameController);
        var lineAt = mouse + new Vector2(16f, -60f);

        // Above the cursor, so it does not cover the cursor message below it.
        var timing = $"tick {_tickTime.Microseconds:0.0} us | render {_renderTime.Microseconds:0.0} us (averages)";
        lineAt.Y += Graphics.DrawTextWithBackground(timing, lineAt, Color.White, Color.FromArgb(180, 0, 0, 0)).Y + 2f;

        if (!Safe.Read(GameController, static g => g.IngameState.IngameUi.StashElement.IsVisible, false))
            return;

        var item = CraftingSlot.Find(GameController, out var why);
        var itemRect = Safe.Read(item, static i => i.GetClientRectCache, default(RectangleF));

        if (itemRect.Width > 0f)
            Graphics.DrawFrame(itemRect, Color.Cyan, 1);

        var hover = Safe.Read(GameController, static g => g.IngameState.UIHover, null);
        var hoverRect = Safe.Read(hover, static h => h.GetClientRectCache, default(RectangleF));

        if (hoverRect.Width > 0f)
            Graphics.DrawFrame(hoverRect, Color.Magenta, 1);

        var itemLine = item == null
            ? $"no crafting item: {why}"
            : $"search \"{CraftingSlot.SearchTextOf(GameController) ?? "unreadable"}\": {CraftingSlot.SearchHighlightOf(item)}";

        var cursor = Safe.Read(GameController, static g => g.IngameState.IngameUi.Cursor.Action.ToString(), "?");
        var line = $"{itemLine} | cursor {cursor} | ExileInput2 {(_input?.Available == true ? "up" : "missing")}";

        Graphics.DrawTextWithBackground(line, lineAt, Color.White, Color.FromArgb(180, 0, 0, 0));
    }

    public override void OnUnload()
    {
        // Release lets go of shift and every other key this plugin holds through ExileInput2.
        _craftLoop?.Stop(CursorMessage.Kind.Info, "Stopped: plugin unloaded");
        Safe.Read(_input, static i => i?.Release() ?? false, false);
    }
}
