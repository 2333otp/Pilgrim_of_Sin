using System;
using System.Collections.Generic;

namespace PilgrimOfSin
{
    [Serializable]
    public class SaveData
    {
        public List<string> defeatedBosses = new List<string>();
        public float masterVolume = 1f;
        public float musicVolume  = 1f;
        public float sfxVolume    = 1f;
    }
}
