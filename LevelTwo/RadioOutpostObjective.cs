using Unity.FPS.Game;

namespace ZombieTown.LevelTwo
{
    public sealed class RadioOutpostObjective : Objective
    {
        protected override void Start()
        {
            Title = GameLocalization.Text("OPERATION RADIO OUTPOST", "OPERATIE RADIOPOST");
            Description = GameLocalization.Text(
                "Capture the building, activate the radio and defend the outpost.",
                "Verover het gebouw, activeer de radio en verdedig de buitenpost.");
            base.Start();
        }
    }
}
