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
        public delegate void SendCommandProc(string command);
        private SendCommandProc SendCommand;

        public TerminalWindow(SendCommandProc sendCommand)
        {
            InitializeComponent();

            SendCommand = sendCommand;
        }

        public void UpdateTerminalDataTextBoxMargin() // I do what I want
        {
            TerminalData_TextBox.Margin = new Thickness(0, 0, 0, Input_TextBox.ActualHeight);
        }

        #region Event Handlers
        public void TerminalDataTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TerminalData_TextBox.ScrollToEnd();
        }

        public void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key.Equals(Key.Return) || e.Key.Equals(Key.Enter))
            {
                SendCommand(Input_TextBox.Text);

                Input_TextBox.Text = "";
            }
        }

        public void TerminalWindow_Loaded(object sender, EventArgs e)
        {
            UpdateTerminalDataTextBoxMargin();
        }
        #endregion
    }
}
