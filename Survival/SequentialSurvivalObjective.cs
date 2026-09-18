using Unity.FPS.Game;

namespace ZombieTown.Survival
{
    public sealed class SequentialSurvivalObjective : Objective
    {
        protected override void Start()
        {
            if (string.IsNullOrWhiteSpace(Title))
                Title = GameLocalization.Text("SURVIVAL ROUTE", "SURVIVALROUTE");
            if (string.IsNullOrWhiteSpace(Description))
                Description = GameLocalization.Text(
                    "Move between marked positions and survive each assault.",
                    "Ga langs de gemarkeerde posities en overleef iedere aanval.");
            base.Start();
        }
    }
}
