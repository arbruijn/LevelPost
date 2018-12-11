using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace LevelPost
{
    /// <summary>
    /// Interaction logic for DumpWindow.xaml
    /// </summary>
    public partial class DumpWindow : Window
    {
        private string searchString = "";

        public DumpWindow()
        {
            InitializeComponent();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var w = new FindWindow();
                w.Init(searchString);
                w.ShowDialog();
                if (w.value == null)
                    return;
                searchString = w.value;
                int searchIndex = Text.Text.IndexOf(searchString, StringComparison.InvariantCultureIgnoreCase);
                if (searchIndex >= 0)
                    Text.Select(searchIndex, searchString.Length);
            }
            else if (e.Key == Key.F3 && Keyboard.Modifiers == ModifierKeys.None && searchString != "")
            {
                int searchIndex = Text.Text.IndexOf(searchString, Text.SelectionStart + 1, StringComparison.InvariantCultureIgnoreCase);
                if (searchIndex >= 0)
                    Text.Select(searchIndex, searchString.Length);
            }
        }
    }
}
