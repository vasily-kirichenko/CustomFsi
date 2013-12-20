#r "bin/Release/Nessos.CustomFsi.Lib.dll"

open Microsoft.Win32

let bkey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
let key = bkey.OpenSubKey(@"Software\Microsoft\FSharp\3.0\Runtime\v4.0")
key.GetValue(null)


open Nessos.CustomFsi.Lib
open Nessos.CustomFsi.Lib.Registry

let fsPath = ["Software" ; "Microsoft" ; "FSharp" ; "3.0"; "Runtime"; "v4.0"]
let fsKey = FsRegistry.DefineKey(RegistryHive.LocalMachine, RegistryView.Registry32, fsPath)
let ff = fsKey.GetField<string>()
FsRegistry.GetValue(ff)