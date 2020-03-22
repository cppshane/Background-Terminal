using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Background_Terminal
{
    public partial class TerminalWindow : Window
    {
        public delegate void SendCommandProc(string command, bool output = false);
        private SendCommandProc SendCommand;

        public delegate void KillChildrenProc();
        private KillChildrenProc KillChildren;

        private List<string> _commandHistory = new List<string>();
        private int _commandHistoryIndex = -1;

        bool _ctrlDown = false;

        public TerminalWindow(SendCommandProc sendCommand, KillChildrenProc killChildren)
        {
            InitializeComponent();

            SendCommand = sendCommand;
            KillChildren = killChildren;
        }

        public void UpdateTerminalDataTextBoxMargin() // I do what I want
        {
            TerminalData_TextBox.Margin = new Thickness(0, 0, 0, Input_TextBox.ActualHeight);
        }

        #region Event Handlers
        private void TerminalDataTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TerminalData_TextBox.ScrollToEnd();
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Cancel current command
            if (e.Key.Equals(Key.C) && _ctrlDown)
            {
                KillChildren();

                e.Handled = true;
            }

            if (e.Key.Equals(Key.LeftCtrl))
            {
                _ctrlDown = true;
            }

            if (e.Key.Equals(Key.Up))
            {
                if (_commandHistoryIndex + 1 < _commandHistory.Count)
                {
                    _commandHistoryIndex++;

                    Input_TextBox.Text = _commandHistory[_commandHistoryIndex];
                    Input_TextBox.CaretIndex = Input_TextBox.Text.Length;
                }
            }

            if (e.Key.Equals(Key.Down))
            {
                if (_commandHistoryIndex - 1 >= 0)
                {
                    _commandHistoryIndex--;

                    Input_TextBox.Text = _commandHistory[_commandHistoryIndex];
                    Input_TextBox.CaretIndex = Input_TextBox.Text.Length;
                }
            }

            if (e.Key.Equals(Key.Return) || e.Key.Equals(Key.Enter))
            {
                // Add to command history
                _commandHistory.Insert(0, Input_TextBox.Text);
                _commandHistoryIndex = -1;

                // Send command
                SendCommand(Input_TextBox.Text);

                // Reset textbox content
                Input_TextBox.Text = "";
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
