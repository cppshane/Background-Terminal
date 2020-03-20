using CoreMeter;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Background_Terminal
{
    public partial class MainWindow : Window
    {
        private static BrushConverter _brushConverter = new BrushConverter();

        private static DirectoryInfo _appDataDirectory = new DirectoryInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BackgroundTerminal"));
        private static string _configFile = "config.json";
        private static string _configPath = System.IO.Path.Combine(_appDataDirectory.FullName, _configFile);

        private CoreMeterUtility _coreMeterUtility;

        private TerminalWindow _terminalWindow;

        private BackgroundTerminalSettings _settings;

        private Process _process;

        private ObservableCollection<string> _terminalData = new ObservableCollection<string>();

        private string _newlineString = Environment.NewLine;

        private int _cmdProcessId;

        private bool _terminalWindowActive = false;

        private bool _awaitingKey1 = false;
        private bool _awaitingKey2 = false;

        private Key? _key1 = null;
        private Key? _key2 = null;

        public MainWindow()
        {
            InitializeComponent();

            // Create TerminalWindow
            _terminalWindow = new TerminalWindow(SendCommand, KillChildren);
            _terminalWindow.Show();

            // Apply changes in terminal data to TerminalWindow
            _terminalData.CollectionChanged += new System.Collections.Specialized.NotifyCollectionChangedEventHandler(
                (object o, System.Collections.Specialized.NotifyCollectionChangedEventArgs target) =>
            {
                string retStr = "";

                if (_terminalData.Count > 1)
                {
                    for (int i = 0; i < _terminalData.Count - 1; i++)
                    {
                        retStr += _terminalData[i];
                        retStr += Environment.NewLine;
                    } 

                    retStr += _terminalData[_terminalData.Count - 1];
                }
                else if (_terminalData.Count == 1)
                {
                    retStr += _terminalData[0];
                }

                Dispatcher.Invoke(() =>
                {
                    _terminalWindow.TerminalData_TextBox.Text = retStr;
                    _terminalWindow.TerminalData_TextBox.ScrollToEnd();
                });
            });

            // Get TerminalWindow handle
            IntPtr hWnd = new WindowInteropHelper(_terminalWindow).Handle;

            // Hide window from alt tab menu
            Win32Interop.HideWindowFromAltTabMenu(hWnd);

            // Initialize CoreMeterUtility for TerminalWindow
            _coreMeterUtility = new CoreMeterUtility(hWnd);

            // Initially lock TerminalWindow
            _coreMeterUtility.Lock();

            // Load settings from json file
            _settings = JsonConvert.DeserializeObject<BackgroundTerminalSettings>(File.ReadAllText(_configPath));

            ApplySettingsToTerminalWindow();

            _key1 = KeyInterop.KeyFromVirtualKey(_settings.Key1);
            _key2 = KeyInterop.KeyFromVirtualKey(_settings.Key2);

            Key1_Button.Content = _key1.ToString();
            Key2_Button.Content = _key2.ToString();
            FontSize_TextBox.Text = _settings.FontSize.ToString();
            FontColor_TextBox.Text = _settings.FontColor.ToString();
            PosX_TextBox.Text = _settings.PosX.ToString();
            PosY_TextBox.Text = _settings.PosY.ToString();
            Width_TextBox.Text = _settings.Width.ToString();
            Height_TextBox.Text = _settings.Height.ToString();

            // Set KeyTriggered callback delegate
            Win32Interop.KeyTriggered = KeyTriggered;

            // Initialize Global Keyhook
            Win32Interop.SetKeyhook();

            // Begin terminal process
            RunTerminalProcessAsync();
        }

        private void ApplySettingsToTerminalWindow()
        {
            _terminalWindow.TerminalData_TextBox.FontSize = _settings.FontSize;
            _terminalWindow.Input_TextBox.FontSize = _settings.FontSize;
            _terminalWindow.TerminalData_TextBox.Foreground = (Brush)_brushConverter.ConvertFromString(_settings.FontColor);
            _terminalWindow.Input_TextBox.Foreground = (Brush)_brushConverter.ConvertFromString(_settings.FontColor);
            _terminalWindow.Left = _settings.PosX;
            _terminalWindow.Top = _settings.PosY;
            _terminalWindow.Width = _settings.Width;
            _terminalWindow.Height = _settings.Height;

            _terminalWindow.UpdateTerminalDataTextBoxMargin();
        }

        private List<Process> GetProcessChildren()
        {
            List<Process> children = new List<Process>();
            ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher(String.Format("Select * From Win32_Process Where ParentProcessID={0}", _process.Id));

            foreach (ManagementObject managementObject in managementObjectSearcher.Get())
            {
                children.Add(Process.GetProcessById(Convert.ToInt32(managementObject["ProcessID"])));
            }

            return children;
        }

        #region Terminal Data Handlers
        private async Task<int> RunTerminalProcessAsync()
        {
            TaskCompletionSource<int> taskCompletionSource = new TaskCompletionSource<int>();

            _process = new Process();

            _process.StartInfo.FileName = "cmd.exe";
            _process.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.CreateNoWindow = true;
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardError = true;

            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += CMD_OutputDataReceived;
            _process.ErrorDataReceived += CMD_ErrorDataReceived;

            _process.Exited += new EventHandler((sender, args) =>
            {
                taskCompletionSource.SetResult(_process.ExitCode);
                _process.Dispose();
            });

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            List<Process> children = GetProcessChildren();
            if (children.Count > 0)
                _cmdProcessId = children[0].Id;

            return await taskCompletionSource.Task;
        }

        private void CMD_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            _terminalData.Add(e.Data);
        }

        private void CMD_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            _terminalData.Add(e.Data);
        }

        private void SendCommand(string command, bool output = true)
        {
            // Background-Terminal application commands
            if (command.StartsWith("bgt"))
            {
                string bgtCommand = command.Split(' ')[1];
                string[] parameters = command.Substring(command.IndexOf(bgtCommand) + bgtCommand.Length + 1).Split(' ');

                if (bgtCommand.Equals("newline"))
                {
                    _newlineString = Regex.Unescape(parameters[0]);
                }
            }
            else
            {
                if (output)
                    _terminalData.Add(command);

                _process.StandardInput.NewLine = _newlineString;
                _process.StandardInput.WriteLine(command);
            }
        }

        private void KillChildren()
        {
            List<Process> children = GetProcessChildren();

            foreach (Process child in children)
            {
                if (!child.Id.Equals(_cmdProcessId))
                {
                    child.Kill();
                }
            }
        }
        #endregion

        #region Event Handlers
        private void TrayIcon_LeftMouseDown(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Show();

                WindowState = WindowState.Normal;

                Topmost = true;
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void Key1Button_Click(object sender, RoutedEventArgs e)
        {
            Key1_Button.Content = "Press Key...";

            if (_awaitingKey2)
            {
                if (_key1 == null)
                    Key1_Button.Content = "";
                else
                    Key1_Button.Content = KeyInterop.VirtualKeyFromKey((Key)_key1).ToString();

                _awaitingKey2 = false;
            }

            _awaitingKey1 = true;
        }

        private void Key2Button_Click(object sender, RoutedEventArgs e)
        {
            Key2_Button.Content = "Press Key...";

            if (_awaitingKey1)
            {
                if (_key2 == null)
                    Key2_Button.Content = "";
                else
                    Key2_Button.Content = KeyInterop.VirtualKeyFromKey((Key)_key2).ToString();

                _awaitingKey1 = false;
            }

            _awaitingKey2 = true;
        }

        private void ApplyChangesButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Media.Brush fontColor;

            double fontSize;
            double posX;
            double posY;
            double width;
            double height;

            try
            {
                fontColor = (System.Windows.Media.Brush)(new BrushConverter()).ConvertFromString(FontColor_TextBox.Text);
            }
            catch
            {
                System.Windows.MessageBox.Show("There was an error interpreting font color input");
                return;
            }

            try
            {
                fontSize = Convert.ToDouble(FontSize_TextBox.Text);
            }
            catch
            {
                System.Windows.MessageBox.Show("There was an error interpreting font size input");
                return;
            }

            try
            {
                posX = Convert.ToDouble(PosX_TextBox.Text);
            }
            catch
            {
                System.Windows.MessageBox.Show("There was an error interpreting X position input");
                return;
            }

            try
            {
                posY = Convert.ToDouble(PosY_TextBox.Text);
            }
            catch
            {
                System.Windows.MessageBox.Show("There was an error interpreting Y position input");
                return;
            }

            try
            {
                width = Convert.ToDouble(Width_TextBox.Text);
            }
            catch
            {
                System.Windows.MessageBox.Show("There was an error interpreting width input");
                return;
            }

            try
            {
                height = Convert.ToDouble(Height_TextBox.Text);
            }
            catch
            {
                System.Windows.MessageBox.Show("There was an error interpreting height input");
                return;
            }

            _settings.Key1 = KeyInterop.VirtualKeyFromKey((Key)_key1);
            _settings.Key2 = KeyInterop.VirtualKeyFromKey((Key)_key2);
            _settings.FontSize = fontSize;
            _settings.FontColor = fontColor.ToString();
            _settings.PosX = posX;
            _settings.PosY = posY;
            _settings.Width = width;
            _settings.Height = height;

            ApplySettingsToTerminalWindow();

            File.WriteAllText(_configPath, JsonConvert.SerializeObject(_settings));
        }

        private void MainWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void MainWindow_Loaded(object sender, EventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                Hide();
            else
                Show();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // End global keyhook
            Win32Interop.DestroyKeyhook();

            // Close terminal window
            _terminalWindow.Close();

            Environment.Exit(0);
        }
        #endregion

        #region Key Detection Callback Delegate
        private void KeyTriggered(int keyCode)
        {
            if (_key1 != null && _key2 != null)
            {
                int vKey1 = KeyInterop.VirtualKeyFromKey((Key)_key1);
                int vKey2 = KeyInterop.VirtualKeyFromKey((Key)_key2);

                if (keyCode == vKey2 && Win32Interop.IsKeyDown(vKey1))
                {
                    _terminalWindow.Input_TextBox.Text = "";

                    if (!_terminalWindowActive)
                    {
                        Win32Interop.ClickSimulateFocus(_terminalWindow);
                        Win32Interop.SetForegroundWindow((new WindowInteropHelper(_terminalWindow)).Handle);
                        Win32Interop.SetActiveWindow((new WindowInteropHelper(_terminalWindow)).Handle);
                        FocusManager.SetFocusedElement(_terminalWindow, _terminalWindow.Input_TextBox);
                        Keyboard.Focus(_terminalWindow.Input_TextBox);

                        _terminalWindowActive = true;
                    }
                    else
                    {
                        _terminalWindowActive = false;

                        Keyboard.ClearFocus();
                    }
                }
            }

            if (_awaitingKey1)
            {
                _key1 = KeyInterop.KeyFromVirtualKey(keyCode);

                _awaitingKey1 = false;

                Key1_Button.Content = _key1.ToString();
            }
            
            if (_awaitingKey2)
            {
                _key2 = KeyInterop.KeyFromVirtualKey(keyCode);

                _awaitingKey2 = false;

                Key2_Button.Content = _key2.ToString();
            }
        }
        #endregion
    }
}
