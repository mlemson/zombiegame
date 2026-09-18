using Unity.FPS.Game;

namespace ZombieTown.LevelThree
{
    public sealed class HarborEvacuationObjective : Objective
    {
        protected override void Start()
        {
            Title = GameLocalization.Text("HARBOR EVACUATION", "HAVENEVACUATIE");
            Description = GameLocalization.Text(
                "Restore power, signal the ferry and escape the harbor.",
                "Herstel de stroom, sein de veerboot en ontsnap uit de haven.");
            base.Start();
        }
    }
}
