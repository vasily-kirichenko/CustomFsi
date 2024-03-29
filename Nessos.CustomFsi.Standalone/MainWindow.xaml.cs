﻿using System;
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

using Nessos.CustomFsi.Lib;
using Microsoft.Win32;

namespace StandalonePlugin
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            this.AllSettings = SettingsResolver
                                    .GetAllConfigurations()
                                    .Where(x => x.IsPluginInstalled)
                                    .ToArray();

            foreach (var config in this.AllSettings)
            {
                this.VersionSelection.Items.Add(config.Name);
            }

            if (this.AllSettings.Length > 0)
                this.VersionSelection.SelectedIndex = 0;
                this.Settings = this.AllSettings[0];
            
            this.SetRegistryValues();

            this.VersionSelection.SelectionChanged += changeSelection;
        }

        private SettingsResolver Settings;
        private SettingsResolver[] AllSettings;

        private void changeSelection(object sender, EventArgs e)
        {
            this.Settings = this.AllSettings[((ComboBox) sender).SelectedIndex];

            SetRegistryValues();
        }

        private void SetRegistryValues()
        {
            if (this.Settings != null)
            {
                this.FsiPath.Text = this.Settings.CustomFsiPath;
                this.FsiEnabled.IsChecked = this.Settings.CustomFsiEnabled;
            }
        }

        private void commit(object sender, RoutedEventArgs e)
        {
            // apply changes
            this.Settings.SetConfig((bool)this.FsiEnabled.IsChecked, this.FsiPath.Text);

            base.Close();
        }

        private void cancel(object sender, RoutedEventArgs e)
        {
            // revert changes
            this.SetRegistryValues();

            base.Close();
        }

        private void browse(object sender, RoutedEventArgs e)
        {
            // Create an instance of the open file dialog box.
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();

            // Set filter options and filter index.
            openFileDialog.Filter = "Executable Files (.exe)|*.exe|All Files (*.*)|*.*";
            openFileDialog.FilterIndex = 1;

            // set initial directory
            if (System.IO.File.Exists(this.FsiPath.Text))
            {
                string currentPath = System.IO.Path.GetDirectoryName(this.FsiPath.Text);
                openFileDialog.InitialDirectory = currentPath;
            }

            // Call the ShowDialog method to show the dialog box.
            bool? userClickedOK = openFileDialog.ShowDialog();

            // Process input if the user clicked OK.
            if (userClickedOK == true)
            {
                // Read the first line from the file and write it the textbox.
                FsiPath.Text = openFileDialog.FileName;
            }
        }
    }
}