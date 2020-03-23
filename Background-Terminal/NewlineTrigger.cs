using System;
using System.Collections.Generic;
using System.Text;

namespace Background_Terminal
{
    public class NewlineTrigger
    {
        public string TriggerCommand { get; set; }
        public string ExitCommand { get; set; }
        public string NewlineString { get; set; }

        public NewlineTrigger(string triggerCommand, string exitCommand, string newlineString)
        {
            TriggerCommand = triggerCommand;
            ExitCommand = exitCommand;
            NewlineString = newlineString;
        }
    }
}
