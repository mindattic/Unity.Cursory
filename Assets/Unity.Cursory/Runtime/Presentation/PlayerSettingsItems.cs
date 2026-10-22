using System;
using MindAttic.Components.Unity;
using MindAttic.Components.Unity.Core;

namespace Unity.Cursory
{
    /// <summary>
    /// The local player's own Cursory setting as a menu item (UC-T8's per-player toggle): "Cursor collision", calling the
    /// owner's <c>[ToServer]</c> setter on their <see cref="CursorAvatar"/>. (UC-T8's "Segmented tether" switch went with
    /// UC-T33: every rope wraps.) <see cref="CursoryClient"/> adds it to the library front end's in-game Esc menu
    /// (<c>NetFrontEnd.AddPauseMenuItems</c>, UC-T12); it works on any <see cref="MenuScreen"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// frontEnd.AddPauseMenuItems(s => PlayerSettingsItems.AddTo(s, () => CursorAvatar.Local(session)));
    /// </code>
    /// </example>
    public sealed class PlayerSettingsItems
    {
        private readonly Func<CursorAvatar> _local;

        private PlayerSettingsItems(Func<CursorAvatar> local)
        {
            _local = local;
        }

        /// <summary>The "Cursor collision" switch: on, your pointer is a disc walls and shapes push out.</summary>
        public MaToggle CursorCollision { get; private set; }

        /// <summary>
        /// Adds the section and the switch to <paramref name="screen"/>. <paramref name="local"/> returns this peer's
        /// own cursor (null before it has spawned, when the switch does nothing). The switch re-reads the synced setting
        /// every time the screen shows. The front end runs its in-game menu callbacks once (UM-T64), so this is added once
        /// and listens to the screen once.
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static PlayerSettingsItems AddTo(MenuScreen screen, Func<CursorAvatar> local)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (local == null) throw new ArgumentNullException(nameof(local));
            var items = new PlayerSettingsItems(local);
            screen.AddSection("Your cursor");
            items.CursorCollision = screen.AddToggle("Cursor collision", false, on => local()?.SetCursorCollision(on));
            screen.Shown += items.Refresh;
            items.Refresh();
            return items;
        }

        /// <summary>Shows the server's current value for this player's setting (without calling the server).</summary>
        public void Refresh()
        {
            CursorAvatar me = _local();
            CursorCollision.SetValueWithoutNotify(me != null && me.IsSpawned && me.CursorCollision);
        }
    }
}
