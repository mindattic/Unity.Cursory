using System;
using MindAttic.Components.Unity;
using MindAttic.Components.Unity.Core;

namespace Unity.Cursory
{
    /// <summary>
    /// The local player's own Cursory settings as menu items (UC-T8's per-player toggles): "Cursor collision" and
    /// "Segmented tether", each calling the owner's <c>[ToServer]</c> setter on their <see cref="CursorAvatar"/>.
    /// <see cref="CursoryClient"/> adds them to the library front end's in-game Esc menu
    /// (<c>NetFrontEnd.AddPauseMenuItems</c>, UC-T12); they work on any <see cref="MenuScreen"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// frontEnd.AddPauseMenuItems(s => PlayerSettingsItems.AddTo(s, () => CursorAvatar.Local(session)));
    /// </code>
    /// </example>
    public sealed class PlayerSettingsItems
    {
        private readonly Func<CursorAvatar> _local;
        private MenuScreen _screen;

        private PlayerSettingsItems(Func<CursorAvatar> local)
        {
            _local = local;
        }

        /// <summary>The "Cursor collision" switch: on, your cursor is a disc walls and shapes push out.</summary>
        public MaToggle CursorCollision { get; private set; }

        /// <summary>The "Segmented tether" switch: on, your rope catches on body corners.</summary>
        public MaToggle SegmentedTether { get; private set; }

        /// <summary>
        /// Adds the section and both switches to <paramref name="screen"/>. <paramref name="local"/> returns this peer's
        /// own cursor (null before it has spawned, when the switches do nothing). The switches re-read the synced settings
        /// every time the screen shows.
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static PlayerSettingsItems AddTo(MenuScreen screen, Func<CursorAvatar> local)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (local == null) throw new ArgumentNullException(nameof(local));
            var items = new PlayerSettingsItems(local) { _screen = screen };
            screen.AddSection("Your cursor");
            items.CursorCollision = screen.AddToggle("Cursor collision", false, on => local()?.SetCursorCollision(on));
            items.SegmentedTether = screen.AddToggle("Segmented tether", false, on => local()?.SetSegmentedTether(on));
            screen.Shown += items.Refresh;
            items.Refresh();
            return items;
        }

        /// <summary>
        /// Shows the server's current values for this player's settings (without calling the server). A front end
        /// rebuilds its menu screen (clearing these switches) and adds fresh items, so items whose switches are gone stop
        /// listening.
        /// </summary>
        public void Refresh()
        {
            if (CursorCollision == null || SegmentedTether == null)
            {
                if (_screen != null) _screen.Shown -= Refresh;
                _screen = null;
                return;
            }

            CursorAvatar me = _local();
            CursorCollision.SetValueWithoutNotify(me != null && me.IsSpawned && me.CursorCollision);
            SegmentedTether.SetValueWithoutNotify(me != null && me.IsSpawned && me.SegmentedTether);
        }
    }
}
