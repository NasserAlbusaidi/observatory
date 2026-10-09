namespace FogOfWar
{
    public partial class OpenSystemScene
    {
        // the fitted hull in km: the ship model scales to it, and the free camera clears it. 52 m outside a career (no build).
        float HullKm() => (float)((career?.Build.Hull.LengthM ?? 52) / 1000);
    }
}
