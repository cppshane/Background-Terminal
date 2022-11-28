using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;

namespace Background_Terminal
{
    public class BackgroundTerminalSettings
    {
        [DefaultValue("cmd.exe")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string ProcessPath { get; set; }
        public int Key1 { get; set; }
        public int Key2 { get; set; }
        public double FontSize { get; set; }
        public string FontColor { get; set; }
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string RegexFilter { get; set; }
        public List<NewlineTrigger> NewlineTriggers { get; set; }
    }
}
