using System;
using MindAttic.Components.Unity;
using MindAttic.Components.Unity.Core;
using TMPro;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.Cursory
{
    /// <summary>
    /// The in-game HUD (UC-T11, Cursory's room HUD), built entirely from MindAttic Components: a top
    /// <see cref="MaToolbar"/> with the level dropdown (<see cref="MaSelect"/>, picking a level starts a level vote),
    /// Reset (starts a reset vote), the connection pill (<see cref="MaTag"/>) and a pause button; the vote overlay (a
    /// <see cref="MaPanel"/> with the question, the tally and Yes/No buttons); the level banner (a panel that shows a
    /// level's signpost for <see cref="HudText.BannerSeconds"/> when the level changes); and toasts when a vote ends or
    /// the level is won.
    /// </summary>
    /// <remarks>
    /// It only reads replicated state and sends intents: <see cref="Bind(VoteBoard)"/> to the room's board and
    /// <see cref="Bind(LevelStateSync)"/> to its level state, and <see cref="Refresh"/> (called every frame by
    /// <see cref="CursoryClient"/>) mirrors them and the session's state.
    /// </remarks>
    public sealed class CursoryHud
    {
        /// <summary>Gap between the toolbar and the vote overlay, in canvas units.</summary>
        private const float OverlayTop = 128f;

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

        /// <summary>The vote overlay; visible only while a vote runs.</summary>
        public MaPanel VoteOverlay { get; private set; }

        public MaText VoteTitle { get; private set; }

        public MaText VoteTally { get; private set; }

        public MaText VoteStanding { get; private set; }

        public MaButton YesButton { get; private set; }

        public MaButton NoButton { get; private set; }

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

            VoteOverlay = MaPanel.Create(Canvas, PanelStyle.Surface, "Vote Overlay");
            Place(VoteOverlay.RectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -OverlayTop), 760f);
            Column(VoteOverlay.gameObject);
            VoteTitle = MaText.Create(VoteOverlay, "", TextStyle.Heading, TextAlignmentOptions.Center);
            VoteTally = MaText.Create(VoteOverlay, "", TextStyle.Label, TextAlignmentOptions.Center);
            VoteStanding = MaText.Create(VoteOverlay, "", TextStyle.Caption, TextAlignmentOptions.Center);
            var buttons = MaRow.Create(VoteOverlay);
            YesButton = buttons.AddButton("Yes", () => Cast(true), ButtonVariant.Primary, MaIcon.Check);
            NoButton = buttons.AddButton("No", () => Cast(false), ButtonVariant.Danger, MaIcon.Close);
            VoteOverlay.Visible = false;

            Banner = MaPanel.Create(Canvas, PanelStyle.Surface, "Level Banner");
            Place(Banner.RectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 1100f);   // below the vote overlay
            Column(Banner.gameObject);
            BannerTitle = MaText.Create(Banner, "", TextStyle.Title, TextAlignmentOptions.Center);
            BannerSubtitle = MaText.Create(Banner, "", TextStyle.Muted, TextAlignmentOptions.Center);
            Banner.Visible = false;
        }

        /// <summary>Shows and votes on <paramref name="board"/> (null unbinds).</summary>
        public void Bind(VoteBoard board)
        {
            if (_board == board) return;
            if (_board != null) _board.VoteEnded -= OnVoteEnded;
            _board = board;
            if (_board != null) _board.VoteEnded += OnVoteEnded;
        }

        /// <summary>Shows <paramref name="level"/>'s level and win (null unbinds).</summary>
        public void Bind(LevelStateSync level)
        {
            if (_level == level) return;
            if (_level != null) _level.Won -= OnWon;
            _level = level;
            if (_level != null) _level.Won += OnWon;
        }

        /// <summary>Mirrors the bound state now: connection pill, level dropdown and banner, vote overlay.</summary>
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

            VoteStatus vote = _board != null && _board.IsSpawned ? _board.Status : null;
            VoteOverlay.Visible = vote != null;
            if (vote != null)
            {
                VoteChoice mine = _board.LocalChoice;
                VoteTitle.Text = HudText.VoteTitle(vote);
                VoteTally.Text = HudText.VoteTally(vote);
                VoteStanding.Text = HudText.VoteStanding(mine);
                bool canVote = HudText.CanVote(mine);
                YesButton.Interactable = canVote;
                NoButton.Interactable = canVote;
            }
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
                _board.StartLevelVote(level);
            }

            // The dropdown shows the room's level; the overlay shows the proposal.
            if (_shownLevel > 0) LevelSelect.SetValueWithoutNotify(_shownLevel - 1);
        }

        private void OnReset()
        {
            if (_board != null && _board.IsSpawned) _board.StartResetVote();
        }

        private void Cast(bool yes)
        {
            if (_board != null && _board.IsSpawned) _board.CastVote(yes);
        }

        private void OnVoteEnded(VoteKind kind, int targetLevel, VoteEnding ending)
        {
            MaToast.Show(HudText.VoteEnded(kind, targetLevel, ending), ending == VoteEnding.Passed ? ToastKind.Success : ToastKind.Info);
            // A level switch shows its banner when the new level number arrives; a reset keeps the number, so show it here.
            if (ending == VoteEnding.Passed && kind == VoteKind.Reset && _shownLevel > 0) ShowBanner(_shownLevel);
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
