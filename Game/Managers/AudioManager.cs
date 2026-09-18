using UnityEngine;
using UnityEngine.Audio;
using System;

namespace Unity.FPS.Game
{
    public class AudioManager : MonoBehaviour
    {
        public AudioMixer[] AudioMixers;

        public AudioMixerGroup[] FindMatchingGroups(string subPath)
        {
            if (AudioMixers == null) return Array.Empty<AudioMixerGroup>();
            for (int i = 0; i < AudioMixers.Length; i++)
            {
                if (AudioMixers[i] == null) continue;
                AudioMixerGroup[] results = AudioMixers[i].FindMatchingGroups(subPath);
                if (results != null && results.Length != 0)
                {
                    return results;
                }
            }

            return Array.Empty<AudioMixerGroup>();
        }

        public void SetFloat(string name, float value)
        {
            for (int i = 0; AudioMixers != null && i < AudioMixers.Length; i++)
            {
                if (AudioMixers[i] != null)
                {
                    AudioMixers[i].SetFloat(name, value);
                }
            }
        }

        public void GetFloat(string name, out float value)
        {
            value = 0f;
            for (int i = 0; AudioMixers != null && i < AudioMixers.Length; i++)
            {
                if (AudioMixers[i] != null)
                {
                    AudioMixers[i].GetFloat(name, out value);
                    break;
                }
            }
        }
    }
}
