namespace KeepersCompound.Lightmapper;

class Program
{
    static void Main(string[] args)
    {
        Timing.Reset();

        // TODO: Read this from args
        var installPath = "/stuff/Games/thief/drive_c/GOG Games/TG ND 1.27 (MAPPING)/";
        var campaignName = "JAYRUDE_Tests";
        var missionName = "lm_test.cow";

        // campaignName = "JAYRUDE_1MIL_Mages";
        // campaignName = "TDP20AC_a_burrick_in_a_room";
        // campaignName = "AtdV";
        // missionName = "miss20.mis";

        var lightMapper = new LightMapper(installPath, campaignName, missionName);
        lightMapper.Light(false);
        lightMapper.Save("kc_lit");

        Timing.LogAll();
    }
}