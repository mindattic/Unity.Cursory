using System.Collections.Generic;
using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Plays whistles (UC-T11): each player's tone synthesized once from Core's <see cref="WhistleTone.Render"/>
    /// (Cursory's Web Audio sweep, rendered into an <see cref="AudioClip"/>) and played through one 2D
    /// <see cref="AudioSource"/> at Cursory's 0.12 gain.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WhistleAudio : MonoBehaviour
    {
        /// <summary>Cursory's whistle gain.</summary>
        public const float Volume = 0.12f;

        private readonly Dictionary<int, AudioClip> _clips = new Dictionary<int, AudioClip>();
        private AudioSource _source;

        /// <summary>How many whistles this has played (tests and diagnostics).</summary>
        public int Played { get; private set; }

        /// <summary>The last tone played, or -1.</summary>
        public int LastTone { get; private set; } = -1;

        /// <summary>The clip for <paramref name="tone"/>, rendered on first use.</summary>
        public AudioClip ClipFor(int tone)
        {
            tone = ((tone % WhistleTone.Count) + WhistleTone.Count) % WhistleTone.Count;
            if (_clips.TryGetValue(tone, out AudioClip clip) && clip != null) return clip;
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            float[] samples = WhistleTone.Render(tone, rate);
            clip = AudioClip.Create("Whistle " + tone + " (" + WhistleTone.Hz(tone).ToString("0") + " Hz)", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            _clips[tone] = clip;
            return clip;
        }

        /// <summary>Plays <paramref name="tone"/> now.</summary>
        public void Play(int tone)
        {
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
            }

            _source.PlayOneShot(ClipFor(tone), Volume);
            Played++;
            LastTone = tone;
        }

        private void OnDestroy()
        {
            foreach (AudioClip clip in _clips.Values)
            {
                if (clip != null) Destroy(clip);
            }

            _clips.Clear();
        }
    }
}
