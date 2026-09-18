using System;
using UnityEngine;

namespace Unity.FPS.Game
{
    public enum GameLanguage
    {
        English = 0,
        Dutch = 1,
    }

    /// <summary>Small, persistent runtime language source shared by gameplay and UI assemblies.</summary>
    public static class GameLocalization
    {
        const string LanguagePreference = "ZombieTown.Language";

        public static event Action LanguageChanged;

        public static GameLanguage Language { get; private set; } =
            (GameLanguage)Mathf.Clamp(PlayerPrefs.GetInt(LanguagePreference, 0), 0, 1);

        public static bool IsDutch => Language == GameLanguage.Dutch;

        public static string Text(string english, string dutch) => IsDutch ? dutch : english;

        public static void SetDutch(bool dutch)
        {
            GameLanguage next = dutch ? GameLanguage.Dutch : GameLanguage.English;
            if (Language == next) return;
            Language = next;
            PlayerPrefs.SetInt(LanguagePreference, (int)Language);
            PlayerPrefs.Save();
            LanguageChanged?.Invoke();
        }
    }
}
