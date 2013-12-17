namespace Nessos.CustomFsi.Lib

    open Microsoft.Win32

    [<AutoOpen>]
    module internal Resources =
        let vsHive = RegistryHive.CurrentUser
        let vsView = RegistryView.Default

        let vsKey = @"Software\Microsoft\VisualStudio\12.0\"
        let vsDir = @"InstallDir"

        let fsiKey = @"Software\Microsoft\VisualStudio\12.0\DialogPage\Microsoft.VisualStudio.FSharp.Interactive.FsiPropertyPage\"
        let fsi64 = "FsiPreferAnyCPUVersion"
        
        let pluginKey = @"Software\Nessos\CustomFsi\VS2013\"
        let pluginEnabled = "Enabled"
        let pluginPath = "Path"

        let fsiCompilerHive = RegistryHive.LocalMachine
        let fsiCompilerView = RegistryView.Registry32
        let fsiCompilerKey = @"Software\Microsoft\FSharp\3.1\Runtime\v4.0"
        let fsiCompilerPath = null : string

        let vsixGuid = "adff2b7c-9847-421c-9598-b378536cc3c4"

    [<Sealed>]
    type RegistryResolver private () =
        static let pluginSettings = lazy(
            try
                option {
                    let! bkey = denull <| RegistryKey.OpenBaseKey(vsHive, vsView)

                    // create plugin subkey if it does not exist
                    match bkey.OpenSubKey(pluginKey, true) with
                    | null -> return! denull <| bkey.CreateSubKey pluginKey
                    | settings -> return settings
                }
            with _ -> None)

        // returns None iff settings is undefined
        static let tryGetPath () =
            option {
                let! settings = pluginSettings.Value

                match settings.GetValue pluginPath with
                | null -> return ""
                | path -> return path :?> string
            }

        // returns None iff settings is undefined
        static let tryGetState () =
            option {
                let! settings = pluginSettings.Value

                match settings.GetValue(pluginEnabled) with
                | :? string as value when value = "True" -> return true
                | _ -> return false
            }

        // obtains the 64bit setting of Visual Studio
        static let tryGetArch () =
            option {
                let! bkey = denull <| RegistryKey.OpenBaseKey(vsHive, vsView)
                let! fsiKey = denull <| bkey.OpenSubKey fsiKey

                match fsiKey.GetValue fsi64 with
                | :? string as value when value = "True" -> return true
                | _ -> return false
            }

        // gets the location of the F# compiler
        static let tryGetFsCompilerPath () =
            option {
                let! bkey = denull <| RegistryKey.OpenBaseKey(fsiCompilerHive, fsiCompilerView)
                let! setting = denull <| bkey.OpenSubKey fsiCompilerKey

                match setting.GetValue fsiCompilerPath with
                | :? string as path -> return! denull path
                | _ -> return! None
            }

        static let tryGetVsDir () =
            option {
                let! bkey = denull <| RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default)
                let! vsKey = denull <| bkey.OpenSubKey vsKey

                match vsKey.GetValue vsDir with
                | :? string as path -> return! denull path
                | _ -> return! None
            }

        static let setConfig (state : bool, path : string) =
            try
                let settings = pluginSettings.Value.Value

                settings.SetValue(pluginEnabled, if state then "True" else "False")
                settings.SetValue(pluginPath, path)
            with e ->
                failwith "CustomFsi: failed to update configuration."

        // C# friendly method wrappers
        static member AppGuid = Resources.vsixGuid
        static member SetConfig(state, path) = setConfig(state, path)
        static member CustomFsiPath = match tryGetPath () with Some p -> p | None -> null
        static member PluginEnabled = match tryGetState () with Some s -> s | None -> false
        static member FsCompilerPath = match tryGetFsCompilerPath () with Some p -> p | None -> null
        static member FsiAnyCpu = match tryGetArch () with Some a -> a | None -> false
        static member VsDir = match tryGetVsDir () with Some d -> d | None -> null