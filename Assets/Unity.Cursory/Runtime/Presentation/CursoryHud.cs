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
    /// Reset (starts a reset vote), the connection pill (<see cref="MaTag"/>) and a pause button; the level banner (a
    /// panel that shows a level's signpost for <see cref="HudText.BannerSeconds"/> when the level changes); and a toast when
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
        private float _bannerUntil;
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

        /// <summary>The connection pill: Live, Hosting, Connecting…, Offline.</summary>
        public MaTag ConnectionPill { get; private set; }

        public MaIconButton PauseButton { get; private set; }

        /// <summary>The library's vote overlay on the bound board (below the toolbar); null until a board is bound.</summary>
        public VoteOverlay VoteOverlay { get; private set; }

        /// <summary>The level banner; visible for a few seconds after the level changes.</summary>
        public MaPanel Banner { get; private set; }

        public MaText BannerTitle { get; private set; }

        public MaText BannerSubtitle { get; private set; }

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
            ConnectionPill = Toolbar.Add(MaTag.Create(Toolbar, "Connecting…", TagTone.Warning));
            PauseButton = Toolbar.AddIconButton(MaIcon.Pause, () => PauseRequested?.Invoke(), "Menu (Esc)");

            Banner = MaPanel.Create(Canvas, PanelStyle.Surface, "Level Banner");
            Place(Banner.RectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 1100f);   // mid-screen, below the vote overlay
            Column(Banner.gameObject);
            BannerTitle = MaText.Create(Banner, "", TextStyle.Title, TextAlignmentOptions.Center);
            BannerSubtitle = MaText.Create(Banner, "", TextStyle.Muted, TextAlignmentOptions.Center);
            Banner.Visible = false;
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
            var (pill, tone) = ConnectionPillText.Describe(_session != null ? _session.State : SessionState.Idle, _session != null && _session.IsHost);
            if (ConnectionPill.Text != pill) ConnectionPill.Text = pill;
            if (ConnectionPill.Tone != tone) ConnectionPill.Tone = tone;

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
            if (Banner.Visible && Time.unscaledTime >= _bannerUntil) Banner.Visible = false;
        }

        /// <summary>Shows level <paramref name="number"/>'s banner for <see cref="HudText.BannerSeconds"/>.</summary>
        public void ShowBanner(int number)
        {
            LevelData data = CursoryClient.LevelDefinition(number);
            var (title, subtitle) = HudText.Banner(data, number);
            BannerTitle.Text = title;
            BannerSubtitle.Text = subtitle;
            BannerSubtitle.Visible = !string.IsNullOrEmpty(subtitle);
            Banner.Visible = true;
            _bannerUntil = Time.unscaledTime + HudText.BannerSeconds;
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

        /// <summary>Anchors a panel at <paramref name="anchor"/> (pivot top-centre), <paramref name="width"/> wide, sized to its content.</summary>
        private static void Place(RectTransform rt, Vector2 anchor, Vector2 offset, float width)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(width, 0f);
        }

        /// <summary>A padded vertical stack that grows to fit its children.</summary>
        private static void Column(GameObject go)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 32, 32);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    /// <summary>The connection pill's words and tone for a session state (Cursory's Live / Reconnecting… / Offline / Connecting…).</summary>
    public static class ConnectionPillText
    {
        /// <summary>What the pill says for <paramref name="state"/>; a Host says "Hosting".</summary>
        public static (string Text, TagTone Tone) Describe(SessionState state, bool isHost)
        {
            switch (state)
            {
                case SessionState.Running: return (isHost ? "Hosting" : "Serving", TagTone.Success);
                case SessionState.Connected: return ("Live", TagTone.Success);
                case SessionState.Starting:
                case SessionState.Connecting: return ("Connecting…", TagTone.Warning);
                case SessionState.Stopping: return ("Leaving…", TagTone.Warning);
                default: return ("Offline", TagTone.Danger);
            }
        }
    }
}
