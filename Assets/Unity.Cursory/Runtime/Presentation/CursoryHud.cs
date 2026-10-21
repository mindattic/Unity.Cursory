using System;
using MindAttic.Components.Unity;
using MindAttic.Components.Unity.Core;
using TMPro;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.Cursory
{
    /// <summary>
    /// The in-game HUD (UC-T11, Cursory's room HUD), built entirely from MindAttic Components: a top
    /// <see cref="MaToolbar"/> with the level dropdown (<see cref="MaSelect"/>, picking a level starts a level vote),
    /// Reset (starts a reset vote), the connection pill (<see cref="MaStatusPill"/>) and a pause button; the level banner (a
    /// <see cref="MaBanner"/> with a level's signpost for <see cref="HudText.BannerSeconds"/> when the level changes); and a toast when
    /// the level is won. The vote itself (question, tally, Yes/No, result toast) is Unity.Multiplayer's ready-made
    /// <see cref="VoteOverlay"/> on the room's <see cref="VoteBoard"/> (UM-T54).
    /// </summary>
    /// <remarks>
    /// It only reads replicated state and sends intents: <see cref="Bind(VoteBoard)"/> to the room's board and
    /// <see cref="Bind(LevelStateSync)"/> to its level state, and <see cref="Refresh"/> (called every frame by
    /// <see cref="CursoryClient"/>) mirrors them and the session's state.
    /// </remarks>
    public sealed class CursoryHud
    {
        private readonly NetSession _session;
        private VoteBoard _board;
        private LevelStateSync _level;
        private int _shownLevel;
        private int _levelCount;

        private CursoryHud(NetSession session)
        {
            _session = session;
        }

        /// <summary>The HUD's canvas (sorting order 10, under the pause menu).</summary>
        public Canvas Canvas { get; private set; }

        public MaToolbar Toolbar { get; private set; }

        /// <summary>The level dropdown: option i is level i + 1; it follows the room's level.</summary>
        public MaSelect LevelSelect { get; private set; }

        public MaButton ResetButton { get; private set; }

        /// <summary>The connection pill: Live, Hosting, Connecting… (pulsing), Offline.</summary>
        public MaStatusPill ConnectionPill { get; private set; }

        public MaIconButton PauseButton { get; private set; }

        /// <summary>The library's vote overlay on the bound board (below the toolbar); null until a board is bound.</summary>
        public VoteOverlay VoteOverlay { get; private set; }

        /// <summary>The level banner; showing for a few seconds after the level changes.</summary>
        public MaBanner Banner { get; private set; }

        /// <summary>The board this HUD shows and sends votes to; null until bound.</summary>
        public VoteBoard Board => _board;

        /// <summary>The level state this HUD shows; null until bound.</summary>
        public LevelStateSync LevelState => _level;

        /// <summary>The level the HUD currently shows (0 before any is known).</summary>
        public int ShownLevel => _shownLevel;

        /// <summary>Raised when the pause button is pressed.</summary>
        public event Action PauseRequested;

        /// <summary>Builds the HUD on a new canvas, with <paramref name="levelCount"/> entries in the level dropdown.</summary>
        public static CursoryHud Create(NetSession session, int levelCount)
        {
            var hud = new CursoryHud(session);
            hud.Build(Math.Max(1, levelCount));
            return hud;
        }

        private void Build(int levelCount)
        {
            _levelCount = levelCount;
            Canvas = MaCanvas.Create("Cursory HUD", 10);

            Toolbar = MaToolbar.Create(Canvas);
            Toolbar.AddText("Cursory", TextStyle.Heading);
            LevelSelect = Toolbar.Add(MaSelect.Create(Toolbar, HudText.LevelOptions(levelCount), 0, OnLevelPicked));
            LevelSelect.LayoutElement.preferredWidth = 280f;
            ResetButton = Toolbar.AddButton("Reset", OnReset, ButtonVariant.Secondary);
            ResetButton.WithTooltip("Start a vote to reset the level");
            Toolbar.AddFlexibleSpace();
            ConnectionPill = Toolbar.Add(MaStatusPill.Create(Toolbar, "Connecting…", StatusKind.Warn));
            PauseButton = Toolbar.AddIconButton(MaIcon.Pause, () => PauseRequested?.Invoke(), "Menu (Esc)");

            Banner = MaBanner.Create(Canvas, 1100f);
            Banner.name = "Level Banner";
            RectTransform rt = Banner.RectTransform;
            rt.pivot = new Vector2(0.5f, 1f);   // its top at mid-screen, below the vote overlay
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Starts votes on <paramref name="board"/> and shows them through its <see cref="VoteOverlay"/> (null unbinds).</summary>
        public void Bind(VoteBoard board)
        {
            if (_board == board) return;
            if (_board != null) _board.VoteEnded -= OnVoteEnded;
            if (VoteOverlay != null) VoteOverlay.Close();
            VoteOverlay = null;
            _board = board;
            if (_board != null)
            {
                _board.VoteEnded += OnVoteEnded;
                VoteOverlay = VoteOverlay.Create(_board);   // the one the front end wired, if it already did
            }
        }

        /// <summary>Shows <paramref name="level"/>'s level and win (null unbinds).</summary>
        public void Bind(LevelStateSync level)
        {
            if (_level == level) return;
            if (_level != null) _level.Won -= OnWon;
            _level = level;
            if (_level != null) _level.Won += OnWon;
        }

        /// <summary>Mirrors the bound state now: connection pill, level dropdown and banner (the vote overlay refreshes itself).</summary>
        public void Refresh()
        {
            var (pill, kind) = ConnectionPillText.Describe(_session != null ? _session.State : SessionState.Idle, _session != null && _session.IsHost);
            ConnectionPill.Set(pill, kind);

            bool live = _session != null && _session.IsClient;
            ResetButton.Interactable = live && _board != null && !_board.IsVoteOpen;
            LevelSelect.Interactable = live && _board != null && !_board.IsVoteOpen;

            int level = _level != null && _level.IsSpawned ? _level.LevelNumber : 0;
            if (level > 0 && level != _shownLevel)
            {
                _shownLevel = level;
                if (level <= LevelSelect.Count) LevelSelect.SetValueWithoutNotify(level - 1);
                ShowBanner(level);   // on joining, and on every load or switch
            }
        }

        /// <summary>Shows level <paramref name="number"/>'s banner for <see cref="HudText.BannerSeconds"/> (in place of any already up).</summary>
        public void ShowBanner(int number)
        {
            LevelData data = CursoryClient.LevelDefinition(number);
            var (title, subtitle) = HudText.Banner(data, number);
            Banner.Show(title, subtitle, HudText.BannerSeconds, replace: true);
        }

        /// <summary>Destroys the HUD's canvas.</summary>
        public void Destroy()
        {
            Bind((VoteBoard)null);
            Bind((LevelStateSync)null);
            if (Canvas != null) UnityEngine.Object.Destroy(Canvas.gameObject);
            Canvas = null;
        }

        private void OnLevelPicked(int index)
        {
            int level = index + 1;
            if (_board != null && _board.IsSpawned && level != _shownLevel && level >= 1 && level <= _levelCount)
            {
                _board.StartVote(LevelVotes.LevelKind, level);
            }

            // The dropdown shows the room's level; the overlay shows the proposal.
            if (_shownLevel > 0) LevelSelect.SetValueWithoutNotify(_shownLevel - 1);
        }

        private void OnReset()
        {
            if (_board != null && _board.IsSpawned) _board.StartVote(LevelVotes.ResetKind);
        }

        private void OnVoteEnded(VoteResult result)
        {
            // The overlay toasts the result. A level switch shows its banner when the new level number arrives; a reset
            // keeps the number, so show it here.
            if (result.Passed && result.Kind == LevelVotes.ResetKind && _shownLevel > 0) ShowBanner(_shownLevel);
        }

        private void OnWon()
        {
            MaToast.Show("Level " + (_level != null ? _level.LevelNumber : _shownLevel) + " solved!", ToastKind.Success);
        }
    }

    /// <summary>The connection pill's words and kind for a session state (Cursory's Live / Reconnecting… / Offline / Connecting…).</summary>
    public static class ConnectionPillText
    {
        /// <summary>What the pill says for <paramref name="state"/>; a Host says "Hosting". A Warn pill pulses.</summary>
        public static (string Text, StatusKind Kind) Describe(SessionState state, bool isHost)
        {
            switch (state)
            {
                case SessionState.Running: return (isHost ? "Hosting" : "Serving", StatusKind.Ok);
                case SessionState.Connected: return ("Live", StatusKind.Ok);
                case SessionState.Starting:
                case SessionState.Connecting: return ("Connecting…", StatusKind.Warn);
                case SessionState.Stopping: return ("Leaving…", StatusKind.Warn);
                default: return ("Offline", StatusKind.Error);
            }
        }
    }
}
