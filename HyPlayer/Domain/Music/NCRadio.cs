using System;
using System.Collections.Generic;
using System.Text;

namespace HyPlayer.Classes
{
    public class NCRadio
    {
        public string Cover { get; set; }
        public string Description { get; set; }
        public NCUser DJ { get; set; }
        public string Id { get; set; }
        public string LastProgramName { get; set; }
        public string Name { get; set; }
        public bool HasSubscribed { get; set; }
    }

}
