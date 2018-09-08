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
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using Microsoft.Win32;
//using Microsoft.WindowsAPICodePack.Dialogs;
using System.Timers;

namespace LevelPost
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private FileSystemWatcher watcher;
        private Timer timer;
        private bool converting;
        private bool updating;
        private const int texDirCount = 1;

        public void UpdateWatcher()
        {
            if (watcher == null)
            {
                watcher = new FileSystemWatcher();
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
                watcher.Changed += new FileSystemEventHandler(OnChanged);

                timer = new Timer(100);
                timer.AutoReset = false;
                timer.Elapsed += OnChangedTimer;
            }

            try
            {
                watcher.Path = new DirectoryInfo(LvlFile.Text).Parent.FullName;
                watcher.EnableRaisingEvents = AutoConvert.IsChecked == true;
            }
            catch
            {
                watcher.EnableRaisingEvents = false;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            updating = true;
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ArneDeBruijn\LevelPost"))
            {
                if (key != null)
                {
                    LvlFile.Text = (string)key.GetValue("LvlFile");
                    EditorDir.Text = (string)key.GetValue("EditorDir");
                    AutoConvert.IsChecked = (int)key.GetValue("AutoConvert", 1) != 0;
                    DebugOptions.IsChecked = (int)key.GetValue("DebugOptions", 0) != 0;
                    for (int i = 1; i <= texDirCount; i++)
                        ((TextBox)this.FindName("TexDir" + i.ToString())).Text = (string)key.GetValue("TexDir" + i.ToString());
                }
            }

            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 448850"))
            {
                if (key != null)
                {
                    string editorDir = (string)key.GetValue("InstallLocation") + @"\OverloadLevelEditor";
                    if (EditorDir.Text.Equals("") && Directory.Exists(editorDir))
                        EditorDir.Text = editorDir;
                }
            }

            updating = false;
            UpdateAll();

            AddMessage("Ready.");
        }

        private void UpdateAll()
        {
            if (updating)
                return;
            updating = true;
            UpdateWatcher();
            UpdateSettings();
            DumpBtn.Visibility = DebugOptions.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
            updating = false;
        }

        private void AddMessage(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                if (msg == null)
                    Messages.AppendText("\n");
                else
                    Messages.AppendText(DateTime.Now.ToLongTimeString() + " " + msg + "\n");
                Messages.SelectAll();
                Messages.ScrollToEnd();
            });
        }

        private void FileButton_Click(object sender, RoutedEventArgs e)
        {
			OpenFileDialog openFileDialog = new OpenFileDialog();
            if (LvlFile.Text.Length != 0) {
                var info = new DirectoryInfo(LvlFile.Text);
                if (info.Attributes.HasFlag(FileAttributes.Directory))
                {
                    openFileDialog.InitialDirectory = info.FullName;
                }
                else
                {
                    openFileDialog.InitialDirectory = info.Parent.FullName;
                    openFileDialog.FileName = info.Name;
                }
            }
            else
            {
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\Revival\Overload";
            }

            if (openFileDialog.ShowDialog() == true) {
				LvlFile.Text = openFileDialog.FileName;
                UpdateAll();
            }
        }

        private void DirButton_Click(object sender, RoutedEventArgs e)
        {
            string name = ((Button)sender).Name;
            TextBox textBox;
            if (name.Equals("EditorDirBtn"))
                textBox = EditorDir;
            else
                textBox = (TextBox)this.FindName("TexDir" + name.Substring(name.Length - 1));

            /*
            CommonOpenFileDialog dialog = new CommonOpenFileDialog();
            dialog.InitialDirectory = textBox.Text;
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                textBox.Text = dialog.FileName;
                UpdateAll();
            }
            */
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                fbd.SelectedPath = textBox.Text;
                System.Windows.Forms.DialogResult result = fbd.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    textBox.Text = fbd.SelectedPath;
                    UpdateAll();
                }
            }
        }

        private void Convert(string filename, ConvertSettings settings)
        {
            converting = true;
            this.Dispatcher.Invoke(() => ConvertBtn.IsEnabled = false);
            try
            {
                var stats = LevelConvert.Convert(filename, settings, (cmsg) => AddMessage(cmsg));
                var msg = "Converted " + filename + " changed " + stats.convertedTextures + " of " + stats.totalTextures + " textures";
                var emsg = new List<string>();
                if (stats.builtInTextures != 0)
                    emsg.Add(stats.builtInTextures + " built-in");
                if (stats.missingTextures != 0)
                    emsg.Add(stats.missingTextures + " missing");
                if (stats.alreadyTextures != 0)
                    emsg.Add(stats.alreadyTextures + " already converted");
                if (emsg.Count != 0)
                    msg += " (" + String.Join(", ", emsg.ToArray()) + ")";
                AddMessage(msg);
            }
            catch (Exception ex)
            {
                AddMessage("Convert failed: " + ex.Message);
            }
            finally
            {
                converting = false;
                this.Dispatcher.Invoke(() => ConvertBtn.IsEnabled = true);
            }
        }

        private void ConvertCurrent(bool isAuto)
        {
            AddMessage(null);
            string filename = LvlFile.Text;
            if (Directory.Exists(filename))
            {
                AddMessage("Error: " + filename + " is a directory. Specify a single level file");
                return;
            }
            AddMessage((isAuto ? "Auto converting " : "Converting ") + filename);

            var dirs = new List<string>();
            var ignoreDirs = new List<string>();

            string editorDir = EditorDir.Text;
            if (!editorDir.Equals("")) {
                if (!Directory.Exists(editorDir))
                {
                    AddMessage("Warning: ignoring non-existing level editor directory " + editorDir);
                }
                else
                {
                    foreach (var name in new string[]{"LevelTextures", "DecalTextures"})
                    {
                        string subdir = editorDir + @"\" + name;
                        if (Directory.Exists(subdir))
                            ignoreDirs.Add(subdir);
                    }
                }
            }
            
            for (int i = 1; i <= texDirCount; i++)
            {
                TextBox dir = (TextBox)this.FindName(String.Format("TexDir{0}", i));
                string val = dir.Text;
                if (!val.Equals(""))
                {
                    if (!Directory.Exists(val))
                    {
                        AddMessage("Warning: ignoring non-existing directory " + val);
                        continue;
                    }
                    dirs.Add(val);
                }
            }

            ConvertSettings settings = new ConvertSettings() { texDirs = dirs, ignoreTexDirs = ignoreDirs };
            new Task(() => Convert(filename, settings)).Start();
        }

        private void ConvertBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateAll();
                ConvertCurrent(false);
            }
            catch (Exception ex)
            {
                AddMessage("Convert failed: " + ex.Message);
            }
        }

        private void UpdateSettings()
        {
            RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\ArneDeBruijn\LevelPost");
            key.SetValue("LvlFile", LvlFile.Text);
            key.SetValue("AutoConvert", AutoConvert.IsChecked == true ? 1 : 0);
            key.SetValue("EditorDir", EditorDir.Text);
            key.SetValue("DebugOptions", DebugOptions.IsChecked == true ? 1 : 0);

            for (int i = 1; i <= texDirCount; i++)
                key.SetValue("TexDir" + i.ToString(), ((TextBox)this.FindName("TexDir" + i.ToString())).Text);

            key.Close(); 
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            if (converting)
                return;
            Dispatcher.Invoke(() =>
            {
                if (!e.FullPath.Equals(LvlFile.Text, StringComparison.InvariantCultureIgnoreCase))
                    return;
                long len = new FileInfo(e.FullPath).Length;
                //AddMessage("Changed File: " + e.FullPath + " " + e.ChangeType + " " + len);
                if (len > 0)
                {
                    timer.Stop();
                    timer.Start();
                }
            });
        }

        private void OnChangedTimer(Object source, ElapsedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                //AddMessage("timer");
                ConvertCurrent(true);
            });
        }

        private void AboutLinkClick(object sender, MouseButtonEventArgs e)
        {
            Label lbl = (Label)sender;
            string val = (string)lbl.Content;
            System.Diagnostics.Process.Start(val);
        }

        private void AutoConvert_Click(object sender, RoutedEventArgs e)
        {
            UpdateAll();
        }

        private void TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAll();
        }

        private void AboutLink_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key.Equals(Key.Space) || e.Key.Equals(Key.Return))
                AboutLinkClick(sender, null);
        }

        private void DumpBtn_Click(object sender, RoutedEventArgs e)
        {
            string filename = LvlFile.Text;
            DumpBtn.IsEnabled = false;
            new Task(() => {
                var lines = new List<string>();
                AddMessage(null);
                AddMessage("Dumping " + filename);
                try
                {
                    foreach (var cmd in LevelFile.ReadLevel(filename).cmds)
                        lines.Add(LevelFile.FmtCmd(cmd));
                }
                catch (Exception ex)
                {
                    AddMessage("Dump failed: " + ex.Message);
                    Dispatcher.Invoke(() => { DumpBtn.IsEnabled = true; });
                    return;
                }
                string text = String.Join("\n", lines);
                lines = null;
                Dispatcher.Invoke(() =>
                {
                    var win = new DumpWindow();
                    win.Text.Text = text;
                    win.Show();
                    win.Text.Focus();
                    DumpBtn.IsEnabled = true;
                    AddMessage("Dump done");
                });
            }).Start();
        }

        private void DebugOptions_Click(object sender, RoutedEventArgs e)
        {
            UpdateAll();
        }
    }
}
