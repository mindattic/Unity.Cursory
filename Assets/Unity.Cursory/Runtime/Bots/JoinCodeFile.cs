using System;
using System.IO;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Server side of UC-T14's bot run: a server launched with <c>-joincodefile &lt;path&gt;</c> writes the join code
    /// it gets from the rendezvous service to that file (and again if it ever registers anew), so bot clients
    /// started with the same path (<see cref="BotDriver.JoinCodeFileArg"/>) can join by it. The nightly compose
    /// smoke shares the file through a volume, as Unity.Multiplayer's own smoke test does. Ignored on a bot.
    /// </summary>
    public static class JoinCodeFile
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string path = BotDriver.Arg(args, BotDriver.JoinCodeFileArg);
            if (path == null || BotDriver.Arg(args, BotDriver.BotArg) != null) return;

            NetBootstrap bootstrap = NetBootstrap.Instance;
            if (bootstrap == null || bootstrap.Session == null)
            {
                Debug.LogError("[Unity.Cursory] " + BotDriver.JoinCodeFileArg + " given, but the scene has no NetBootstrap.");
                return;
            }

            Attach(bootstrap.Session, path);
        }

        /// <summary>Writes every code <paramref name="session"/> publishes to <paramref name="path"/>, replacing what was there.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="path"/> is null.</exception>
        public static void Attach(NetSession session, string path)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (path == null) throw new ArgumentNullException(nameof(path));
            session.JoinCodePublished += code => Write(path, JoinCodeFormat.Format(code.Value));
            if (!string.IsNullOrEmpty(session.PublishedCodeText)) Write(path, session.PublishedCodeText);
        }

        private static void Write(string path, string code)
        {
            try
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                // Write beside it and move into place, so a bot polling the file never reads half a code.
                string temp = path + ".tmp";
                File.WriteAllText(temp, code + Environment.NewLine);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                Debug.Log("[Unity.Cursory] Join code " + code + " written to " + path);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Unity.Cursory] Could not write the join code to " + path + ": " + ex.Message);
            }
        }
    }
}
