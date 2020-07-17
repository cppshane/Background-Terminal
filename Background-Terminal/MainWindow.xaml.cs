using CoreMeter;
using Newtonsoft.Json;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Background_Terminal
{
    public partial class MainWindow : Window
    {
        // Static Fields
        private static BrushConverter _brushConverter = new BrushConverter();

        private static DirectoryInfo _appDataDirectory = new DirectoryInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BackgroundTerminal"));
        private static string _configFile = "config.json";
        private static string _configPath = System.IO.Path.Combine(_appDataDirectory.FullName, _configFile);

        // TerminalWindow
        private TerminalWindow _terminalWindow;

        // Main Process
        private Process _process;

        // CoreMeter
        private CoreMeterUtility _coreMeterUtility;

        // Settings Container
        private BackgroundTerminalSettings _settings;

        // SSH Handling
        private SshClient _sshClient;
        private bool _sshMode = false;
        private string _sshServer = String.Empty;
        private string _sshUsername = String.Empty;
        private string _sshCurrentDirectory = String.Empty;

        // UI List Bindings
        private ObservableCollection<string> _terminalData = new ObservableCollection<string>();
        public ObservableCollection<NewlineTrigger> NewlineTriggers { get; set; }

        // Newline State Handling
        private string _currentTrigger = null;
        private string _newlineString = Environment.NewLine;

        // CMD Process ID
        private int _cmdProcessId;

        // TerminalWindow UI State Handling
        private bool _terminalWindowActive = false;
        private bool _terminalWindowLocked = true;

        private bool _awaitingKey1 = false;
        private bool _awaitingKey2 = false;

        private Key? _key1 = null;
        private Key? _key2 = null;

        #region Constructor
        public MainWindow()
        {
            InitializeComponent();

            // Create TerminalWindow
            _terminalWindow = new TerminalWindow(SendCommand, KillProcess, TerminalWindowUIUpdate);
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

            Process_TextBox.Text = _settings.ProcessPath;
            Key1_Button.Content = _key1.ToString();
            Key2_Button.Content = _key2.ToString();
            FontSize_TextBox.Text = _settings.FontSize.ToString();
            FontColor_TextBox.Text = _settings.FontColor.ToString();
            PosX_TextBox.Text = _settings.PosX.ToString();
            PosY_TextBox.Text = _settings.PosY.ToString();
            Width_TextBox.Text = _settings.Width.ToString();
            Height_TextBox.Text = _settings.Height.ToString();

            if (_settings.NewlineTriggers == null)
                _settings.NewlineTriggers = new List<NewlineTrigger>();

            NewlineTriggers = new ObservableCollection<NewlineTrigger>(_settings.NewlineTriggers);

            // Set KeyTriggered callback delegate
            Win32Interop.KeyTriggered = KeyTriggered;

            // Initialize Global Keyhook
            Win32Interop.SetKeyhook();

            // Begin terminal process
            RunTerminalProcessAsync();

            DataContext = this;
        }
        #endregion

        #region General Functions
        private void KillProcess()
        {
            if (_sshMode)
            {
                _sshClient.Disconnect();
                _sshClient.Dispose();

                _sshMode = false;

                _terminalData.Add("SSH Session Disconnected");
            }
            else
            {
                KillChildren();
            }
        }

        private void OutputSSHUsage()
        {
            _terminalData.Add("Background Terminal manually handles SSH connection. (Ctrl + C to quit)");
            _terminalData.Add("Usage: ssh <server>");
            _terminalData.Add("Note that SSH.net does not support change directory (cd), so you are required to prefix the " +
                "command with a (cd) call to the directory you want to be in. (cd /my/directory && mycommand)");
            _terminalData.Add("To get around this, I have implemented automated directory prefixing. If you call (cd) while in SSH mode, it will automatically prefix any " +
                "further commands with the directory you previously specified.");
        }

        private string DirectoryPrefixCommand(string command)
        {
            if (!_sshCurrentDirectory.Equals(String.Empty))
                return "cd " + _sshCurrentDirectory + " && " + command;

            return command;
        }
        #endregion

        #region UI State Functions
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
        }

        private void TerminalWindowUIUpdate()
        {
            PosX_TextBox.Text = _terminalWindow.Left.ToString();
            PosY_TextBox.Text = _terminalWindow.Top.ToString();

            Width_TextBox.Text = _terminalWindow.Width.ToString();
            Height_TextBox.Text = _terminalWindow.Height.ToString();
        }
        #endregion

        #region Process Helper Functions
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

        #region Terminal Data Handlers
        private async Task<int> RunTerminalProcessAsync()
        {
            TaskCompletionSource<int> taskCompletionSource = new TaskCompletionSource<int>();

            try
            {
                _process?.Kill();
            }
            catch (Exception e) { }

            _process = new Process();

            _process.StartInfo.FileName = _settings.ProcessPath;
            _process.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.CreateNoWindow = true;
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardError = true;

            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += OutputDataReceived;
            _process.ErrorDataReceived += ErrorDataReceived;

            _process.Exited += new EventHandler((sender, args) =>
            {
                Process process = (Process)sender;
                taskCompletionSource.SetResult(process.ExitCode);
                process.Dispose();
            });

            try
            {
                _process.Start();

                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                List<Process> children = GetProcessChildren();
                if (children.Count > 0)
                    _cmdProcessId = children[0].Id;
            } 
            catch (Exception e)
            {
                Show();
                WindowState = WindowState.Normal;
                Topmost = true;

                System.Windows.MessageBox.Show("There was an error starting the process. Check your Process input and Apply Changes to retry. Details: " + Environment.NewLine + Environment.NewLine + e.Message);
                taskCompletionSource.SetException(e);
            }

            return await taskCompletionSource.Task;
        }

        private void OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            _terminalData.Add(e.Data);
        }

        private void ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            _terminalData.Add(e.Data);
        }

        private string SendCommandSSH(string command, bool silent = false)
        {
            // Handle SSH login connection
            if (_sshUsername.Equals(String.Empty))
            {
                _sshUsername = command;
                _terminalData.Add("Enter password:");

                _terminalWindow._passwordMode = true;
            }
            else if (_terminalWindow._passwordMode)
            {
                _terminalData.Add("Connecting...");

                // Attempt connection
                _sshClient = new SshClient(_sshServer, _sshUsername, _terminalWindow._password);
                try
                {
                    _sshClient.Connect();

                    _terminalWindow._passwordMode = false;
                    _terminalWindow._password = String.Empty;

                    if (_sshClient.IsConnected)
                        _terminalData.Add("Connected to " + _sshServer);
                    else
                    {
                        _terminalData.Add("There was a problem connecting.");

                        _sshMode = false;
                        _sshUsername = String.Empty;
                    }
                }
                catch (Exception e)
                {
                    _terminalData.Add(e.Message);
                }
            }

            // Handle SSH commands
            else
            {
                if (_sshClient.IsConnected)
                {
                    try
                    {
                        SshCommand sshCommand = _sshClient.CreateCommand(command);
                        string result = sshCommand.Execute();

                        StreamReader reader = new StreamReader(sshCommand.ExtendedOutputStream);
                        string extendedResult = reader.ReadToEnd();

                        if (result.Length > 0 && (result[result.Length - 1] == '\n' || result[result.Length - 1] == '\r'))
                            result = result.Substring(0, result.Length - 1);

                        // Handle silent calls to pwd maintain SSH current directory
                        if (silent)
                            return result;

                        if (extendedResult.Length > 0 && (extendedResult[extendedResult.Length - 1] == '\n' || extendedResult[extendedResult.Length - 1] == '\r'))
                            extendedResult = extendedResult.Substring(0, extendedResult.Length - 1);

                        if (!result.Equals(String.Empty))
                            _terminalData.Add(result);

                        if (!extendedResult.Equals(String.Empty))
                            _terminalData.Add(extendedResult);

                    }
                    catch (Exception e)
                    {
                        _terminalData.Add(e.Message);
                    }
                }
                else
                {
                    _terminalData.Add("You are no longer connected to SSH. Exiting.");

                    _sshMode = false;
                    _sshUsername = String.Empty;
                }
            }

            return null;
        }

        private void SendCommandBGT(string command)
        {
            string bgtCommand = command.Split(' ')[1];
            string[] parameters = command.Substring(command.IndexOf(bgtCommand) + bgtCommand.Length + 1).Split(' ');

            if (bgtCommand.Equals("newline"))
            {
                _newlineString = Regex.Unescape(parameters[0]);
            }
        }

        private void SendCommand(string command)
        {


            // Handle SSH mode
            if (_sshMode)
            {
                _terminalData.Add(DirectoryPrefixCommand(command));
                SendCommandSSH(DirectoryPrefixCommand(command));

                if (command.ToLower().StartsWith("cd"))
                    _sshCurrentDirectory = SendCommandSSH(command + " && pwd", true);
            }

            // Background-Terminal application commands
            else if (command.ToLower().StartsWith("bgt"))
            {
                _terminalData.Add(command);
                SendCommandBGT(command);
            }

            // Initialize SSH mode
            else if (command.ToLower().StartsWith("ssh"))
            {
                _terminalData.Add(command);

                List<string> commandParams = command.Split(' ').ToList();

                if (commandParams.Count != 2)
                {
                    OutputSSHUsage();
                }
                else
                {
                    _sshServer = commandParams[1];

                    OutputSSHUsage();

                    _terminalData.Add("");
                    _terminalData.Add("Enter username:");

                    _sshMode = true;
                }
            }

            // Standard command handling
            else
            {
                _terminalData.Add(command);

                _process.StandardInput.NewLine = _newlineString;
                _process.StandardInput.WriteLine(command);

                // Check for newline trigger activations
                foreach (NewlineTrigger trigger in _settings.NewlineTriggers)
                {
                    if (command.StartsWith(trigger.TriggerCommand))
                    {
                        _currentTrigger = trigger.TriggerCommand;

                        _newlineString = Regex.Unescape(trigger.NewlineString);
                    }
                    else if (command.StartsWith(trigger.ExitCommand) && _currentTrigger != null && _currentTrigger.Equals(trigger.TriggerCommand))
                    {
                        _currentTrigger = null;

                        _newlineString = Environment.NewLine;
                    }
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

        private void TerminalWindowLockedButton_Click(object sender, RoutedEventArgs e)
        {
            if (_terminalWindowLocked)
            {
                _terminalWindowLocked = false;
                TerminalWindowLocked_Button.Content = "Unlocked";
            }
            else
            {
                _terminalWindowLocked = true;
                TerminalWindowLocked_Button.Content = "Locked";
            }

            _terminalWindow.SetWindowLocked(_terminalWindowLocked);
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

        private void AddNewlineTriggerButton_Click(object sender, RoutedEventArgs e)
        {
            NewlineTriggers.Add(new NewlineTrigger("Trigger Command", "Exit Command", "Newline Character"));
        }

        private void DeleteNewlineTriggerButton_Click(object sender, RoutedEventArgs e)
        {
            if (NewlineTrigger_ListBox.SelectedItem != null)
                NewlineTriggers.Remove((NewlineTrigger)NewlineTrigger_ListBox.SelectedItem);
        }

        private void NewlineTriggerTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            NewlineTrigger_ListBox.SelectedItem = ((TextBox)sender).DataContext;
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

            _settings.NewlineTriggers = new List<NewlineTrigger>(NewlineTriggers);

            if (!_settings.ProcessPath.Equals(Process_TextBox.Text))
            {
                _settings.ProcessPath = Process_TextBox.Text;

                // Begin terminal process
                RunTerminalProcessAsync();
            }

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
            if (_sshClient != null)
            {
                if (_sshClient.IsConnected)
                    _sshClient.Disconnect();

                _sshClient.Dispose();
            }

            _process.Kill();

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
