using Unity.FPS.Game;

namespace ZombieTown.LevelFour
{
    public sealed class RetreatDefenseObjective : Objective
    {
        protected override void Start()
        {
            Title = GameLocalization.Text("FALLBACK PROTOCOL", "TERUGTREKPROTOCOL");
            Description = GameLocalization.Text(
                "Hold each defense line, buy the fallback gates and survive the last stand.",
                "Houd elke linie, koop de terugtrekpoorten en overleef het laatste gevecht.");
            base.Start();
        }
    }
}
