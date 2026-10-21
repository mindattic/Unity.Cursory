using System;
using MindAttic.Components.Unity;
using MindAttic.Components.Unity.Core;

namespace Unity.Cursory
{
    /// <summary>
    /// The local player's own Cursory settings as pause-menu items (UC-T8's per-player toggles): "Cursor collision"
    /// and "Segmented tether", each calling the owner's <c>[ToServer]</c> setter on their <see cref="CursorAvatar"/>.
    /// Add them to any <see cref="MenuScreen"/>: <see cref="CursoryPauseMenu"/>'s own, or a generic front end's pause
    /// screen (UC-T12 adopts the library's UM-T48 front end and plugs these in as its game-supplied items).
    /// </summary>
    /// <example>
    /// <code>
    /// var items = PlayerSettingsItems.AddTo(pauseScreen, () => CursorAvatar.Local(session));
    /// </code>
    /// </example>
    public sealed class PlayerSettingsItems
    {
        private readonly Func<CursorAvatar> _local;

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
            var items = new PlayerSettingsItems(local);
            screen.AddSection("Your cursor");
            items.CursorCollision = screen.AddToggle("Cursor collision", false, on => local()?.SetCursorCollision(on));
            items.SegmentedTether = screen.AddToggle("Segmented tether", false, on => local()?.SetSegmentedTether(on));
            screen.Shown += items.Refresh;
            items.Refresh();
            return items;
        }

        /// <summary>Shows the server's current values for this player's settings (without calling the server).</summary>
        public void Refresh()
        {
            CursorAvatar me = _local();
            CursorCollision.SetValueWithoutNotify(me != null && me.IsSpawned && me.CursorCollision);
            SegmentedTether.SetValueWithoutNotify(me != null && me.IsSpawned && me.SegmentedTether);
        }
    }

    /// <summary>
    /// Cursory's in-game Esc menu (UC-T11): a MindAttic Components <see cref="MenuStack"/> configured with
    /// <see cref="MenuStack.ConfigureAsPauseMenu"/>, so Esc (or gamepad Start/B) opens and closes it through the
    /// package's back router. Its root holds Resume and the <see cref="PlayerSettingsItems"/>. The game keeps running
    /// underneath (it is multiplayer); while it is open the world ignores the pointer.
    /// </summary>
    /// <remarks>
    /// Kept small on purpose: when the library's drop-in front end brings its own pause menu (UM-T48, adopted by
    /// UC-T12), Cursory drops this class and adds <see cref="PlayerSettingsItems"/> to that menu instead.
    /// </remarks>
    public sealed class CursoryPauseMenu
    {
        private CursoryPauseMenu()
        {
        }

        /// <summary>The menu stack (closed until Esc).</summary>
        public MenuStack Stack { get; private set; }

        /// <summary>The root screen ("Paused").</summary>
        public MenuScreen Root { get; private set; }

        /// <summary>The Resume button.</summary>
        public MaButton Resume { get; private set; }

        /// <summary>The local player's settings switches.</summary>
        public PlayerSettingsItems Settings { get; private set; }

        /// <summary>True while the menu shows.</summary>
        public bool IsOpen => Stack != null && Stack.IsOpen;

        /// <summary>Builds the menu on its own canvas (sorting order 200, above the HUD), closed.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="local"/> is null.</exception>
        public static CursoryPauseMenu Create(Func<CursorAvatar> local)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));
            var menu = new CursoryPauseMenu();
            menu.Stack = MenuStack.Create(MaCanvas.Create("Cursory Pause", 200), backdrop: true);
            menu.Root = menu.Stack.CreateScreen("Paused", "The room keeps playing");
            menu.Resume = menu.Root.AddButton("Resume", () => menu.Stack.Close(), ButtonVariant.Primary, MaIcon.Play);
            menu.Settings = PlayerSettingsItems.AddTo(menu.Root, local);
            menu.Stack.ConfigureAsPauseMenu(menu.Root);
            return menu;
        }

        /// <summary>Opens the menu (as Esc does).</summary>
        public void Open() => Stack.Open();

        /// <summary>Closes the menu (as Resume does).</summary>
        public void Close() => Stack.Close();

        /// <summary>Destroys the menu and its canvas.</summary>
        public void Destroy()
        {
            if (Stack != null)
            {
                var canvas = Stack.GetComponentInParent<UnityEngine.Canvas>();
                UnityEngine.Object.Destroy(canvas != null ? canvas.gameObject : Stack.gameObject);
            }

            Stack = null;
        }
    }
}
