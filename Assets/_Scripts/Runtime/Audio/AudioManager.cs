using UnityEngine;
using Zenject;
using System;
using System.Collections.Generic;

namespace BattleshipsVR.Audio
{
    /// <summary>Centralized SFX/music router with simple pitch utilities.</summary>
    public sealed class AudioManager : MonoBehaviour
    {
        /// <summary>All addressable audio events in the game.</summary>
        public enum AudioType
        {
            BigExplosion,
            BoatPlace,
            BoatPlacement2,
            Explosion,
            Failure,
            HoverTick,
            HugeExplosion,
            IslandAmbient,
            LargeExplosion,
            MainSoundtrack,
            MissileFall,
            MissileLockIn,
            OceanAmbient,
            RotateTick,
            Sonar,
            TimerEnd,
            Trumpet,
            Victory,
            Victory2,
            StoneFall,
            PlacementError
        }

        [Serializable]
        private sealed class AudioEntry
        {
            public AudioType type;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
        }

        [Header("Audio Sources")]
        [SerializeField, Tooltip("One-shot SFX source.")]
        private AudioSource _sfxSource;
        [SerializeField, Tooltip("Looping music/ambience source.")]
        private AudioSource _musicSource;

        [Header("Audio Clips")]
        [SerializeField, Tooltip("Mapping of AudioType to clips/volumes.")]
        private List<AudioEntry> _audioEntries = new();

        private readonly Dictionary<AudioType, AudioEntry> _audioLookup = new();

        private void Awake()
        {
            for (int i = 0; i < _audioEntries.Count; i++)
            {
                AudioEntry e = _audioEntries[i];
                if (!_audioLookup.ContainsKey(e.type))
                    _audioLookup.Add(e.type, e);
            }
        }

        /// <summary>
        /// Plays an audio clip by type. Uses music source when loop=true, otherwise SFX one-shot.
        /// </summary>
        public void Play(AudioType type, bool loop = false)
        {
            if (!_audioLookup.TryGetValue(type, out AudioEntry entry) || entry.clip == null)
                return;

            if (loop)
            {
                _musicSource.clip = entry.clip;
                _musicSource.volume = entry.volume;
                _musicSource.loop = true;
                _musicSource.Play();
                return;
            }

            _sfxSource.pitch = 1f;
            _sfxSource.PlayOneShot(entry.clip, entry.volume);
        }

        /// <summary>Stops the looping music/ambience source if playing.</summary>
        public void StopMusic()
        {
            if (_musicSource.isPlaying)
                _musicSource.Stop();
        }

        /// <summary>Plays a one-shot with a random pitch in [minPitch, maxPitch].</summary>
        public void PlayWithRandomPitch(AudioType type, float minPitch = 0.9f, float maxPitch = 1.1f)
        {
            if (!_audioLookup.TryGetValue(type, out AudioEntry entry) || entry.clip == null)
                return;

            _sfxSource.pitch = UnityEngine.Random.Range(minPitch, maxPitch);
            _sfxSource.PlayOneShot(entry.clip, entry.volume);
            _sfxSource.pitch = 1f;
        }

        /// <summary>Plays a one-shot with an explicit pitch value.</summary>
        public void PlayWithSpecificPitch(AudioType type, float pitch = 1.1f)
        {
            if (!_audioLookup.TryGetValue(type, out AudioEntry entry) || entry.clip == null)
                return;

            _sfxSource.pitch = pitch;
            _sfxSource.PlayOneShot(entry.clip, entry.volume);
            _sfxSource.pitch = 1f;
        }

        /// <summary>Picks a random type from the list and plays it with a random pitch.</summary>
        public void PlayRandom(float minPitch = 0.9f, float maxPitch = 1.1f, params AudioType[] types)
        {
            if (types == null || types.Length == 0)
                return;

            AudioType randomType = types[UnityEngine.Random.Range(0, types.Length)];
            if (!_audioLookup.TryGetValue(randomType, out AudioEntry entry) || entry.clip == null)
                return;

            _sfxSource.pitch = UnityEngine.Random.Range(minPitch, maxPitch);
            _sfxSource.PlayOneShot(entry.clip, entry.volume);
            _sfxSource.pitch = 1f;
        }
    }
}
