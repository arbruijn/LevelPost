using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using Microsoft.Win32;
//using Microsoft.WindowsAPICodePack.Dialogs;
using System.Timers;
using System.Media;
using System.Linq;

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
        private BundleFiles bundleFiles;
        private readonly int[] resArray = new [] { 128, 256, 512, 1024, 2048 };

        private List<Tuple<string, string>> matTexs = new List<Tuple<string, string>> { 
            new Tuple<string, string>("_MainTex", "Diff"),
            new Tuple<string, string>("_BumpMap", "Norm"),
            new Tuple<string, string>("_MetallicGlossMap", "Met"),
            new Tuple<string, string>("_SpecGlossMap", "Rough"),
            new Tuple<string, string>("_ParallaxMap", "Height"),
            new Tuple<string, string>("_EmissionMap", "Emission"),
        };

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

            bundleFiles = new BundleFiles();
            bundleFiles.Logger = AddMessage;

            updating = true;
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ArneDeBruijn\LevelPost"))
            {
                if (key != null)
                {
                    LvlFile.Text = (string)key.GetValue("LvlFile");

                    BunFile.Text = (string)key.GetValue("BunFile");
                    BunPrefix.Text = (string)key.GetValue("BunPrefix");

                    EditorDir.Text = (string)key.GetValue("EditorDir");
                    AutoConvert.IsChecked = (int)key.GetValue("AutoConvert", 0) != 0;
                    DebugOptions.IsChecked = (int)key.GetValue("DebugOptions", 0) != 0;
                    DoneBeep.IsChecked = (int)key.GetValue("DoneBeep", 0) != 0;
                    for (int i = 1; i <= texDirCount; i++)
                        ((TextBox)this.FindName("TexDir" + i.ToString())).Text = (string)key.GetValue("TexDir" + i.ToString());

                    TexPointPx.Text = (string)key.GetValue("TexPointPx", "64");

                    MatDir.Text = (string)key.GetValue("MatDir");

                    foreach (var x in matTexs)
                    {
                        string prop = "Mat" + x.Item2;
                        ((TextBox)FindName(prop)).Text = (string)key.GetValue(prop);
                    }

                    DefaultProbes_ForceOn.IsChecked = (int)key.GetValue("DefaultProbesForceOn", 0) != 0;
                    DefaultProbes_Remove.IsChecked = (int)key.GetValue("DefaultProbesRemove", 0) != 0;
                    int res = (int)key.GetValue("CustomProbeRes", 256);
                    if (Array.IndexOf(resArray, res) >= 0)
                        ((RadioButton)FindName("ProbeRes_" + res)).IsChecked = true;
                    BoxLavaNormalProbe.IsChecked = (int)key.GetValue("BoxLavaNormalProbe", 0) != 0;
                }
                else
                {
                    AutoConvert.IsChecked = true;
                    TexPointPx.Text = "64";
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

            /*
            bf.ScanBundles(GetCustomLevelDir());
            var bs = bf.Bundles.Keys.ToArray();
            int nmat = 0, ngo = 0;
            foreach (var x in bf.Bundles.Values)
            {
                nmat += x.materials.Count();
                ngo += x.gameObjects.Count();
            }
            AddMessage("Found " + bs.Length + " bundles with " + nmat + " materials and " + ngo + " game objects.");
            AddMessage(String.Join(", ", bs));
            AddMessage(String.Join(", ", bf.Bundles.Values.Select(x => String.Join(", ", x.materials))));
            AddMessage(String.Join(", ", bf.Bundles.Values.Select(x => String.Join(", ", x.gameObjects))));
            */
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
            SaveObjBtn.Visibility = DebugOptions.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
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

        private string GetCustomLevelDir()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\Revival\Overload";
        }

        private string BunLastDir;

        private List<string> ListCleanup(List<string> items)
        {
            var ret = new List<string>();
            foreach (var item in items.Select(s => s.Trim()).Where(s => s != ""))
                if (!ret.Contains(item, StringComparer.OrdinalIgnoreCase))
                    ret.Add(item);
            return ret;
        }

        private List<string> BundlesGet()
        {
            return ListCleanup(BunFile.Text.Split(new char[] { '\n', '\r', '\t' }).ToList());
        }

        private static string MsgAddFilename(string msg, string filename)
        {
            if (!string.IsNullOrEmpty(filename) && !msg.StartsWith(filename + ": "))
                msg = filename + ": " + msg;
            return msg;
        }

        private bool scanAgain, scanActive, scanAgainShowErrors, scanAgainUpdateList;
        private object scanLock = new Object();

        private void BundlesScan(List<string> lines, bool showErrors, bool updateList)
        {
            lock (scanLock)
            {
                if (scanActive)
                {
                    scanAgain = true;
                    scanAgainShowErrors = scanAgainShowErrors | showErrors;
                    scanAgainUpdateList = scanAgainUpdateList | updateList;
                    return;
                }
                scanAgain = false;
                scanActive = true;
                scanAgainShowErrors = scanAgainUpdateList = false;
            }
            new Task(() => {
                for (;;)
                {
                    var err = new List<string>();
                    var ok = new List<string>();
                    int totalMat = 0, totalEnt = 0;
                    foreach (var filename in lines)
                    {
                        try
                        {
                            var info = bundleFiles.CachedBundleInfo(filename);
                            if (!info.materials.Any() && !info.gameObjects.Any())
                                err.Add(filename + ": No materials or entities found.");
                            else
                            {
                                ok.Add(filename);
                                totalMat += info.materials.Count;
                                totalEnt += info.gameObjects.Count;
                            }
                        }
                        catch (Exception ex)
                        {
                            err.Add(MsgAddFilename(ex.Message, filename));
                        }
                    }
                    if (err.Any() && showErrors)
                        if (err.Count == 1)
                            MessageBox.Show("Error adding bundle file:\n\n" + err[0].Substring(0, err[0].IndexOf(": ")) + "\n\n" + err[0].Substring(err[0].IndexOf(": ") + 2), "Bundle Files", MessageBoxButton.OK, MessageBoxImage.Error);
                        else
                            MessageBox.Show("Not all files could be added:\n\n" + string.Join("\n\n", err), "Bundle Files", MessageBoxButton.OK, MessageBoxImage.Error);
                    Dispatcher.Invoke(() => {
                        BunStatus.Content =
                            err.Any() && !showErrors ?
                                err.Count == 1 ? err[0] : "Cannot load " + err.Count + " bundle files" :
                            ok.Any() ? "Loaded " + FmtCount(ok.Count, "bundle") + " with " + FmtCount(totalMat, "material") + " and " + FmtCount(totalEnt, "entity", "entities") + "." : "";
                    });
                    if (updateList)
                    {
                        Dispatcher.Invoke(() => {
                            BunFile.Text = string.Join("\n", ok);
                            UpdateAll();
                        });
                    }
                    lock (scanLock)
                    {
                        if (!scanAgain)
                        {
                            scanActive = false;
                            return;
                        }
                        showErrors = scanAgainShowErrors;
                        updateList = scanAgainUpdateList;
                        scanAgain = false;
                        scanAgainShowErrors = scanAgainUpdateList = false;
                    }
                 }
            }).Start();
        }

        private void BundlesSet(List<string> lines)
        {
            lines = ListCleanup(lines);
            if (lines.SequenceEqual(BundlesGet()))
                return;
            BunStatus.Content = "Loading...";
            BundlesScan(lines, true, true);
        }

        private void OFDSetInitDir(OpenFileDialog openFileDialog, string filename, string lastDir, string defaultDir)
        {
            if (!string.IsNullOrEmpty(filename))
            {
                var info = new DirectoryInfo(filename);
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
            else if (!string.IsNullOrEmpty(lastDir))
            {
                openFileDialog.InitialDirectory = lastDir;
            }
            else
            {
                openFileDialog.InitialDirectory = GetCustomLevelDir();
            }
        }

        private void BundlesAdd() {
            var lines = BundlesGet();
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true;
            OFDSetInitDir(openFileDialog, lines.LastOrDefault(), BunLastDir, GetCustomLevelDir());

            if (openFileDialog.ShowDialog() == true) {
                BunLastDir = new DirectoryInfo(openFileDialog.FileName).Parent.FullName;
                lines = BundlesGet();
                foreach (var filename in openFileDialog.FileNames)
                    if (!lines.Contains(filename, StringComparer.OrdinalIgnoreCase))
                        lines.Add(filename);
                BundlesSet(lines);
            }
        }

        private void FileButton_Click(object sender, RoutedEventArgs e)
        {
            string btnName = ((Button)sender).Name;
            TextBox textBox = (TextBox)this.FindName(btnName.Substring(0, btnName.Length - 3)); // strip Btn

            OpenFileDialog openFileDialog = new OpenFileDialog();
            OFDSetInitDir(openFileDialog, textBox.Text, null, GetCustomLevelDir());

            if (openFileDialog.ShowDialog() == true) {
				textBox.Text = openFileDialog.FileName;
                UpdateAll();
            }
        }

        private void DirButton_Click(object sender, RoutedEventArgs e)
        {
            string name = ((Button)sender).Name;
            TextBox textBox;
            if (name.Equals("EditorDirBtn"))
                textBox = EditorDir;
            else if (name.Equals("MatDirBtn"))
                textBox = MatDir;
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
                if (stats.convertedEntities != 0)
                    msg += ", changed " + stats.convertedEntities + " entities";
                AddMessage(msg);
                this.Dispatcher.Invoke(() => { if (DoneBeep.IsChecked == true) SystemSounds.Beep.Play(); });
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

        private static string FmtCount(int n, string singular, string plural = null)
        {
            return n + " " + (n == 1 ? singular : plural == null ? singular + "s" : plural);
        }

        private static void DictListAdd<TKey, TValue>(Dictionary<TKey, List<TValue>> d, TKey key, TValue value)
        {
            if (!d.TryGetValue(key, out List<TValue> l))
                d.Add(key, l = new List<TValue>());
            l.Add(value);
        }

        private static string ToTitleCase(string s)
        {
            return s.Substring(0, 1).ToUpper() + s.Substring(1);
        }

        private static string FmtList(IEnumerable<string> items, int max)
        {
            var list = items.Take(max + 1).ToList();
            int count = list.Count;
            if (count == 0)
                return "";
            if (count == 1)
                return list[0];
            if (count > max)
                return string.Join(", ", list.GetRange(0, max)) + ", ...";
            return string.Join(", ", list.GetRange(0, count - 1)) + " and " + list[count - 1];
        }

        private void ShowDups(Dictionary<string, List<ConvertBundle>> d, string singular, string plural)
        {
            var dups = d.Where(x => x.Value.Count > 1).ToList();
            if (!dups.Any())
                return;
            if (dups.Count == 1)
                AddMessage("Warning: " + singular + " " + dups[0].Key + " occurs in " + dups[1].Value.Count + " bundles: " + 
                    FmtList(dups[1].Value.Select(x => x.BundleName), 5));
            else
                AddMessage("Warning: " + dups.Count + " " + plural + " occur in multiple bundles: " +
                    FmtList(dups.Select(x => x.Key), 10));
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

            int texPointPx = 0;
            Int32.TryParse(TexPointPx.Text, out texPointPx);

            ConvertSettings settings = new ConvertSettings() {
                texDirs = dirs,
                ignoreTexDirs = ignoreDirs,
                texPointPx = texPointPx
            };

            settings.defaultProbeHide = DefaultProbes_ForceOn.IsChecked.Value;
            settings.defaultProbeRemove = DefaultProbes_Remove.IsChecked.Value;
            settings.boxLavaNormalProbe = BoxLavaNormalProbe.IsChecked.Value;
            settings.probeRes = 256;
            foreach (var res in resArray)
              if (((RadioButton)FindName("ProbeRes_" + res)).IsChecked.Value)
                settings.probeRes = res;

            var paths = BundlesGet();
            var dupMats = new Dictionary<string, List<ConvertBundle>>(StringComparer.OrdinalIgnoreCase);
            var dupGOs = new Dictionary<string, List<ConvertBundle>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                BundleInfo info;
                try
                {
                    info = bundleFiles.CachedBundleInfo(path);
                }
                catch (Exception ex)
                {
                    AddMessage("Error: cannot read bundle file: " + MsgAddFilename(ex.Message, paths.Count == 1 ? null : path));
                    return;
                }

                var f = new DirectoryInfo(path);
                var convBun = new ConvertBundle() {
                    Name = f.Name,
                    OS = f.Parent.Name,
                    Dir = f.Parent.Parent.Name, 
                    Materials = info.materials,
                    GameObjects = info.gameObjects
                    };
                settings.bundles.Add(convBun);
                foreach (var mat in convBun.Materials)
                    DictListAdd(dupMats, mat.Key, convBun);
                foreach (var go in convBun.GameObjects)
                    DictListAdd(dupGOs, go, convBun);

                var parts = new List<string>();
                int n;
                if (convBun.Materials != null && (n = convBun.Materials.Count) != 0)
                    parts.Add(FmtCount(n, "material", "materials") +
                        " (" + string.Join(", ", convBun.Materials.Keys.Take(5)) + (n > 5 ? ", ..." : "") + ")");
                if (convBun.GameObjects != null && (n = convBun.GameObjects.Count) != 0)
                    parts.Add(FmtCount(n, "entity", "entities"));
                AddMessage("Using bundle " + convBun.BundleName +
                    (parts.Count != 0 ? ": " + String.Join(", ", parts) : ", but no materials or entities found!"));

                string levelDir = new DirectoryInfo(filename).Parent.FullName;
				var bundleLevelDir = f.Parent?.Parent?.Parent;
                if (bundleLevelDir == null || !bundleLevelDir.FullName.Equals(levelDir, StringComparison.OrdinalIgnoreCase))
                {
                    AddMessage("Warning: the bundle file is not in correct subdirectory of the level file!");
                    AddMessage("The bundle file must be at " + Path.Combine(levelDir, convBun.BundleName));
                }
            }
            ShowDups(dupMats, "material", "materials");
            ShowDups(dupGOs, "entity", "entities");

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

            key.SetValue("BunFile", BunFile.Text);
            key.SetValue("BunPrefix", BunPrefix.Text);

            key.SetValue("AutoConvert", AutoConvert.IsChecked == true ? 1 : 0);
            key.SetValue("EditorDir", EditorDir.Text);
            key.SetValue("DebugOptions", DebugOptions.IsChecked == true ? 1 : 0);
            key.SetValue("DoneBeep", DoneBeep.IsChecked == true ? 1 : 0);

            key.SetValue("TexPointPx", TexPointPx.Text);

            key.SetValue("MatDir", MatDir.Text);
            foreach (var x in matTexs)
            {
                string prop = "Mat" + x.Item2;
                key.SetValue(prop, ((TextBox)FindName(prop)).Text);
            }

            for (int i = 1; i <= texDirCount; i++)
                key.SetValue("TexDir" + i.ToString(), ((TextBox)this.FindName("TexDir" + i.ToString())).Text);

            key.SetValue("DefaultProbesForceOn", DefaultProbes_ForceOn.IsChecked == true ? 1 : 0);
            key.SetValue("DefaultProbesRemove", DefaultProbes_Remove.IsChecked == true ? 1 : 0);
            key.SetValue("BoxLavaNormalProbe", BoxLavaNormalProbe.IsChecked == true ? 1 : 0);
            foreach (var res in resArray)
                if (((RadioButton)FindName("ProbeRes_" + res)).IsChecked.Value)
                    key.SetValue("CustomProbeRes", res);

            key.Close();
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            if (converting)
                return;
            Dispatcher.Invoke(() =>
            {
                if (!e.FullPath.Equals(LvlFile.Text, StringComparison.OrdinalIgnoreCase))
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
                List<string> lines;
                AddMessage(null);
                AddMessage("Dumping " + filename);
                try
                {
                    lines = LevelDump.DumpLines(filename);
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

        private void Field_Click(object sender, RoutedEventArgs e)
        {
            UpdateAll();
        }

        private void BunFile_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextChanged(sender, e);
            BundlesScan(BundlesGet(), false, false);
        }

        private void SaveObjBtn_Click(object sender, RoutedEventArgs e)
        {
            string filename = LvlFile.Text;
            SaveObjBtn.IsEnabled = false;
            new Task(() => {
                var outName = Path.ChangeExtension(filename, "obj");
                AddMessage(null);
                AddMessage("Exporting level mesh from " + filename + " to " + outName);
                try
                {
                    LevelSaveObj.SaveObj(filename, outName, AddMessage);
                }
                catch (Exception ex)
                {
                    AddMessage("Exporting failed: " + ex.Message);
                    Dispatcher.Invoke(() => { SaveObjBtn.IsEnabled = true; });
                    return;
                }
                Dispatcher.Invoke(() =>
                {
                    SaveObjBtn.IsEnabled = true;
                    AddMessage("Exporting level mesh done");
                });
            }).Start();
        }

        private void TexPointPx_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.CompareTo("0") >= 0 && e.Text.CompareTo("9") <= 0;
        }

        private void BunFileAddBtn_Click(object sender, RoutedEventArgs e)
        {
            BundlesAdd();
        }

        private void BunFileClearBtn_Click(object sender, RoutedEventArgs e)
        {
            BundlesSet(new List<string>());
        }
    }
}
