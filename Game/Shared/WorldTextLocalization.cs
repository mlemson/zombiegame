namespace Unity.FPS.Game
{
    public static class WorldTextLocalization
    {
        static readonly string[] Dutch={"NOG VERGRENDELD","VERZAMEL BIJ DE FERRY","HAVENBAKEN","STROOMPUNT","WAPENPUNT","MATERIALEN","MUNITIE","GEZONDHEID","BOUWPLEK","VERDEDIG HIER","RADIOPOST","APPARTEMENTEN","WATERKANT","ZIEKENBOEG","LUCHTSLUIS","COMMANDOBRUG","REACTOR","VRACHTRUIM"};
        static readonly string[] English={"LOCKED","ASSEMBLE AT THE FERRY","HARBOR BEACON","POWER SWITCH","WEAPON POINT","MATERIALS","AMMO","HEALTH","BUILD SUPPLIES","DEFEND HERE","RADIO OUTPOST","APARTMENTS","WATERFRONT","MED BAY","AIRLOCK","COMMAND BRIDGE","REACTOR","CARGO HOLD"};
        public static string Translate(string text){
            if(string.IsNullOrEmpty(text)||GameLocalization.IsDutch)return text;
            text=text.Replace("DE VRACHTRUIMTE","CARGO BAY").Replace("DE REACTORKERN","REACTOR CORE")
                .Replace("DE ZIEKENBOEG","MED BAY").Replace("DE COMMANDOBRUG","COMMAND BRIDGE").Replace("DE LUCHTSLUIS","AIRLOCK")
                .Replace("STADSKANTOOR","CITY HALL").Replace("CENTRAAL PARK","CENTRAL PARK").Replace("ZUIDELIJK STADSBLOK","SOUTH CITY BLOCK")
                .Replace("POLITIEBUREAU","POLICE STATION").Replace("CENTRAAL PLEIN","CENTRAL PLAZA");
            for(int i=0;i<Dutch.Length;i++)text=text.Replace(Dutch[i],English[i]);
            return text;
        }
    }
}
