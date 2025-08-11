using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Terraria.GameContent.UI
{
    public class WiresUI
    {
        public class Settings
        {
            public enum MultiToolMode : byte
            {
                Red = 1,
                Green = 2,
                Blue = 4,
                Yellow = 8,
                Actuator = 16,
                Cutter = 32
            }
        }
    }
}
