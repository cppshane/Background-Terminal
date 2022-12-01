# Background-Terminal

Visually dynamic terminal to overlay your Windows background, made with <a href="https://www.nuget.org/packages/CoreMeter/">CoreMeter</a>.

![Video Sample](https://s7.gifyu.com/images/wallpaperdemoresultcutgif.gif)

## Controls

Font Family - Input the name of any of the [Microsoft Fonts](https://learn.microsoft.com/en-us/typography/font-list/) (i.e. "Consolas" or "Arial Black")

Regex Filter - Apply a regex filter to specify what to remove from all output. Useful for removing escape characters from other encodings (ANSI etc.).

Newline Trigger - Specify a command that will change the default newline character. For instance setting (`bash`,  `exit`, `\n`) will switch from using Windows `\r\n` newline character when the command `bash` is detected, and will switch back to `\r\n` when `exit` is detected. 

