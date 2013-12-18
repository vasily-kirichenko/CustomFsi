

open Microsoft.Win32

let bkey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.)
let key = bkey.OpenSubKey(@"Software\Microsoft\VisualStudio\11.0\")
key.GetValue(@"InstallDir")