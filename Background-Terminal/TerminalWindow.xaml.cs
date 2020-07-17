using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Background_Terminal
{
    public partial class TerminalWindow : Window
    {
        // MainWindow Delegates
        public delegate void SendCommandProc(string command);
        private SendCommandProc SendCommand;

        public delegate void KillProcessProc();
        private KillProcessProc KillProcess;

        public delegate void TerminalWindowUIUpdateProc();
        private TerminalWindowUIUpdateProc TerminalWindowUIUpdate;

        // Command History
        private List<string> _commandHistory = new List<string>();
        private int _commandHistoryIndex = -1;

        // UI
        private bool _locked;

        // Input Handling
        private bool _ctrlDown = false;

        // Password Mode
        public bool _passwordMode = false;
        public string _password = String.Empty;

        #region Constructors
        public TerminalWindow(SendCommandProc sendCommand, KillProcessProc killProcess, TerminalWindowUIUpdateProc terminalWindowUIUpdate)
        {
            InitializeComponent();

            SendCommand = sendCommand;
            KillProcess = killProcess;
            TerminalWindowUIUpdate = terminalWindowUIUpdate;
        }
        #endregion

        #region UI State Functions
        private void UpdateTerminalDataTextBoxMargin()
        {
            TerminalData_TextBox.Margin = new Thickness(0, 0, 0, Input_TextBox.ActualHeight);
        }

        public void SetWindowLocked(bool locked)
        {
            _locked = locked;

            if (locked)
            {
                this.Background = Brushes.Transparent;
                this.ResizeMode = ResizeMode.NoResize;
                TerminalData_TextBox.IsHitTestVisible = true;
            }
            else
            {
                this.Background = Brushes.Gray;
                this.ResizeMode = ResizeMode.CanResizeWithGrip;
                TerminalData_TextBox.IsHitTestVisible = false;
            }
        }
        #endregion

        #region Event Handlers
        private void TerminalWindow_MouseDown(object sender, MouseEventArgs e)
        {
            if (!_locked && e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void TerminalWindow_LocationChanged(object sender, EventArgs e)
        {
            UpdateTerminalDataTextBoxMargin();

            TerminalWindowUIUpdate();
        }

        private void TerminalWindow_SizeChanged(object sender, RoutedEventArgs e)
        {
            UpdateTerminalDataTextBoxMargin();

            TerminalWindowUIUpdate();
        }

        private void TerminalDataTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TerminalData_TextBox.ScrollToEnd();
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl + C handling
            if (e.Key.Equals(Key.C) && _ctrlDown)
            {
                _password = String.Empty;
                _passwordMode = false;
                Input_TextBox.Text = "";

                KillProcess();

                e.Handled = true;
            }
            else if (e.Key.Equals(Key.LeftCtrl))
            {
                _ctrlDown = true;
            }

            // Command cycling
            else if (e.Key.Equals(Key.Up))
            {
                if (_commandHistoryIndex + 1 < _commandHistory.Count)
                {
                    _commandHistoryIndex++;

                    Input_TextBox.Text = _commandHistory[_commandHistoryIndex];
                    Input_TextBox.CaretIndex = Input_TextBox.Text.Length;
                }
            }

            else if (e.Key.Equals(Key.Down))
            {
                if (_commandHistoryIndex - 1 >= 0)
                {
                    _commandHistoryIndex--;

                    Input_TextBox.Text = _commandHistory[_commandHistoryIndex];
                    Input_TextBox.CaretIndex = Input_TextBox.Text.Length;
                }
            }

            // Enter/Return command
            else if (e.Key.Equals(Key.Return) || e.Key.Equals(Key.Enter))
            {
                _commandHistory.Insert(0, Input_TextBox.Text);
                _commandHistoryIndex = -1;

                SendCommand(Input_TextBox.Text);

                Input_TextBox.Text = "";
            }


            // Backspace password data
            else if (e.Key.Equals(Key.Back))
            {
                if (_passwordMode)
                    if (_password.Length > 0)
                        _password = _password.Substring(0, _password.Length - 1);
            }
        }

        private void InputTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Password handling
            if (_passwordMode)
            {
                if (!e.Text.Equals(String.Empty))
                {
                    foreach (char c in e.Text)
                        if (c == '\n' || c == '\r' || !char.IsLetterOrDigit(c) && !char.IsSymbol(c) && !char.IsWhiteSpace(c) && !char.IsPunctuation(c))
                            return;

                    _password += e.Text;

                    int caretIndex = Input_TextBox.CaretIndex;
                    Input_TextBox.Text = Input_TextBox.Text + "*";
                    Input_TextBox.CaretIndex = caretIndex + 1;

                    e.Handled = true;
                }
            }
        }

        private void InputTextBox_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key.Equals(Key.LeftCtrl))
            {
                _ctrlDown = false;
            }
        }

        private void TerminalWindow_Loaded(object sender, EventArgs e)
        {
            UpdateTerminalDataTextBoxMargin();
        }
        #endregion
    }
}
