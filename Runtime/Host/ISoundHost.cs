using UnityEngine;

namespace Demech2
{
    // The game's sound ids (soundfx.c's by name).
    public interface ISoundHost
    {
        // A DOS sound by its name (the running install's id, 0 without one); FindResourceIdByName in the loaded PRJ's
        // sounds.
        int FindSoundByName(string p_name);
        int FindSoundResource(string p_name);
    }
}
