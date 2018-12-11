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
    /// Interaction logic for FindWindow.xaml
    /// </summary>
    public partial class FindWindow : Window
    {
        public string value;
        public FindWindow()
        {
            InitializeComponent();
        }

        public void Init(string value)
        {
            TxtFind.Text = value;
            TxtFind.SelectAll();
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            value = TxtFind.Text;
            Close();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            value = null;
            Close();
        }
    }
}
