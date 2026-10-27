using System;
using System.Drawing;
using System.Numerics;
using ExileCore2;
using Graphics = ExileCore2.Graphics;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace CraftUntilHighlighted;

/// <summary>
/// The last message, written under the mouse cursor for Cursor message duration and faded over its last second. One at a
/// time: a new message replaces the one showing. Copied from AutoExpedition's CursorWarning, which moved its warnings
/// beside the cursor because players did not notice them anywhere else, with the same placement, fade and red, and two
/// more kinds beside the error. Messages are a few words, not sentences.
/// </summary>
internal static class CursorMessage
{
    public enum Kind
    {
        Error,
        Success,
        Info,
    }

    /// <summary>AutoExpedition's warning red.</summary>
    private static readonly Color ErrorColour = Color.FromArgb(255, 235, 90, 90);

    /// <summary>AutoExpedition's purple, the same as the craft square's highlighted colour by default.</summary>
    private static readonly Color SuccessColour = Color.FromArgb(255, 190, 120, 255);

    /// <summary>AutoExpedition's white (its common chest colour).</summary>
    private static readonly Color InfoColour = Color.FromArgb(255, 245, 245, 245);

    /// <summary>How long a ShowWhile message stays up after its last call: a few ticks, so it does not flicker.</summary>
    private const double WhileGraceSeconds = 0.1;

    private static (string Text, Kind Kind, DateTime At, bool While) _shown = ("", Kind.Info, DateTime.MinValue, false);

    /// <summary>
    /// Shows a message for Cursor message duration, replacing any before it. For what a run did, which the player should
    /// get to read after it has happened.
    /// </summary>
    public static void Show(Kind kind, string text) => _shown = (text ?? "", kind, DateTime.UtcNow, false);

    /// <summary>
    /// Shows a message only for as long as it is called every tick, replacing any before it, and gone as soon as the
    /// calls stop, with no fade. For a condition that is only true while something lasts, such as why a run will not
    /// start while the cursor is in the craft square.
    /// </summary>
    public static void ShowWhile(Kind kind, string text) => _shown = (text ?? "", kind, DateTime.UtcNow, true);

    /// <summary>Draws the message showing, if any, and returns where it went; empty when nothing was drawn.</summary>
    public static RectangleF Draw(Graphics graphics, GameController gc, int seconds)
    {
        if (_shown.Text.Length == 0)
            return default;

        var age = (DateTime.UtcNow - _shown.At).TotalSeconds;

        if (_shown.While ? age >= WhileGraceSeconds : age >= Math.Max(1, seconds))
            return default;

        var cursor = CraftingSlot.MouseOf(gc);
        var window = Safe.Read(gc, static g => g.Window.GetWindowRectangle(), default);

        if (cursor.X < 0f || window.Width <= 0f)
            return default;

        var size = graphics.MeasureText(_shown.Text);

        // Below and right of the pointer, kept inside the window so a message raised near an edge is not cut off.
        var at = new Vector2(
            Math.Clamp(cursor.X + 16f, 0f, Math.Max(0f, window.Width - size.X - 4f)),
            Math.Clamp(cursor.Y + 44f, 0f, Math.Max(0f, window.Height - size.Y - 4f)));

        // Fades over its last second rather than vanishing mid-read; a ShowWhile message is at full strength until it goes.
        var alpha = _shown.While ? 255 : (int)(255 * Math.Clamp(Math.Max(1, seconds) - age, 0d, 1d));

        var colour = _shown.Kind switch
        {
            Kind.Error => ErrorColour,
            Kind.Success => SuccessColour,
            _ => InfoColour,
        };

        graphics.DrawTextWithBackground(_shown.Text, at, Color.FromArgb(alpha, colour), Color.FromArgb(alpha * 200 / 255, Color.Black));

        return new RectangleF(at.X, at.Y, size.X, size.Y);
    }
}
