using Unity.FPS.Game;

namespace ZombieTown.Finale
{
    public sealed class BusEscapeObjective : Objective
    {
        protected override void Start()
        {
            Title = GameLocalization.Text("LAST EXIT", "LAATSTE UITWEG");
            Description = GameLocalization.Text("Prepare the bus and escape together.", "Maak de bus klaar en ontsnap samen.");
            base.Start();
        }
    }
}
