namespace ZombieTown.Multiplayer
{
    public static class RelayJoinCodeUtility
    {
        const string AllowedCharacters = "6789BCDFGHJKLMNPQRTW";

        public static bool TryNormalize(string value, out string normalized)
        {
            normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
            if (normalized.Length < 6 || normalized.Length > 12) return false;
            for (int i = 0; i < normalized.Length; i++)
                if (AllowedCharacters.IndexOf(normalized[i]) < 0) return false;
            return true;
        }

        public static char ValidateCharacter(string current, int index, char added)
        {
            char upper = char.ToUpperInvariant(added);
            return AllowedCharacters.IndexOf(upper) >= 0 ? upper : '\0';
        }
    }
}
