namespace Nessos.CustomFsi.Lib

    open Microsoft.Win32
    open Nessos.CustomFsi.Lib.Registry

    [<AutoOpen>]
    module internal Resources =

        let customFsiKey = FsRegistry.DefineKey(RegistryHive.CurrentUser, RegistryView.Default, ["Software";"Nessos";"CustomFsi"])
        let customFsiEnabled = customFsiKey.GetField<bool>("Enabled")
        let customFsiPath = customFsiKey.GetField<string>("Path")        

        type VisualStudioKeys =
            {
                Id : string
                PluginGuid : string

                // Visual Studio Registry Info

                VisualStudioPath : FsRegistryField<string>
                VisualStudioFsiPrefer64 : FsRegistryField<bool>
                VisualStudioFsiParam : FsRegistryField<string>

                // F# compiler registry info

                FSharpPath : FsRegistryField<string>
            }

        // F# 3.1 / VS 2013 settings
        let vs2013 =
            let vsPath = ["Software"; "Microsoft"; "VisualStudio" ; "12.0"]
            let fsPath = ["Software" ; "Microsoft" ; "FSharp" ; "3.1"; "Runtime"; "v4.0"]
            let vsKey = FsRegistry.DefineKey(RegistryHive.CurrentUser, RegistryView.Default, vsPath)
            let vsKeyLM = FsRegistry.DefineKey(RegistryHive.LocalMachine, RegistryView.Registry32, vsPath)
            let fsKey = FsRegistry.DefineKey(RegistryHive.LocalMachine, RegistryView.Registry32, fsPath)
            let fsiPrefsKey = vsKey.GetSubKey ["DialogPage";"Microsoft.VisualStudio.FSharp.Interactive.FsiPropertyPage"]
            {
                Id = "VS2013"
                PluginGuid = "adff2b7c-9847-421c-9598-b378536cc3c4"

                VisualStudioPath = vsKeyLM.GetField("InstallDir")
                VisualStudioFsiPrefer64 = fsiPrefsKey.GetField("FsiPreferAnyCPUVersion")
                VisualStudioFsiParam = fsiPrefsKey.GetField("FsiCommandLineArgs")

                FSharpPath = fsKey.GetField()
            }

        // F# 3.0 / VS 2012 settings
        let vs2012 =
            let vsPath = ["Software"; "Microsoft"; "VisualStudio" ; "11.0"]
            let fsPath = ["Software" ; "Microsoft" ; "FSharp" ; "3.0"; "Runtime"; "v4.0"]
            let vsKey = FsRegistry.DefineKey(RegistryHive.CurrentUser, RegistryView.Default, vsPath)
            let vsKeyLM = FsRegistry.DefineKey(RegistryHive.LocalMachine, RegistryView.Registry32, vsPath)
            let fsKey = FsRegistry.DefineKey(RegistryHive.LocalMachine, RegistryView.Registry32, fsPath)
            let fsiPrefsKey = vsKey.GetSubKey ["DialogPage";"Microsoft.VisualStudio.FSharp.Interactive.FsiPropertyPage"]
            {
                Id = "VS2012"
                PluginGuid = "9cf2e4d2-fa2e-4e55-9af0-185783ea2dc7"

                VisualStudioPath = vsKeyLM.GetField("InstallDir")
                VisualStudioFsiPrefer64 = fsiPrefsKey.GetField("FsiPreferAnyCPUVersion")
                VisualStudioFsiParam = fsiPrefsKey.GetField("FsiCommandLineArgs")

                FSharpPath = fsKey.GetField()
            }

        let settings = [vs2012 ; vs2013]

        let tryGetSettingsById (id : string) =
            settings |> List.tryFind (fun s -> s.Id = id)

        let tryGetSettingsByCompilerPath (path : string) =
            settings 
            |> List.tryFind(fun s -> FsRegistry.TryGetValue s.FSharpPath |> Option.exists(fun p -> p = path))

        let pickMostSuitableConfiguration () =
            settings
            |> List.tryFind(fun s -> FsRegistry.Exists(s.VisualStudioPath))
            |> fun x -> defaultArg x vs2013


    [<Sealed>]
    type SettingsResolver internal (settings : VisualStudioKeys) =

        let evaluate (fld : FsRegistryField<'T>) = FsRegistry.TryGetValue fld
        let defaultArg def (v : 'T option) = match v with None -> def | Some v -> v
        let valueOrFail msg (v : 'T option) = match v with None -> invalidOp msg | Some v -> v

        static member OfSettingsId(id : string) =
            match tryGetSettingsById id with
            | None -> invalidArg "id" <| sprintf "Could not locate settings with id '%s'" id
            | Some s -> new SettingsResolver(s)

        static member OfFSharpCompilerPath(path : string) =
            match tryGetSettingsByCompilerPath path with
            | Some s -> new SettingsResolver(s)
            | None ->
                let s = pickMostSuitableConfiguration()
                new SettingsResolver(s)

        member __.SetConfig(enabled : bool, path : string) =
            FsRegistry.SetValue(customFsiEnabled, enabled, overwrite = true)
            FsRegistry.SetValue(customFsiPath, path, overwrite = true)

        member __.CustomFsiPath = customFsiPath |> evaluate |> defaultArg ""
        member __.CustomFsiEnabled = customFsiEnabled |> evaluate |> defaultArg false
        member __.FSharpCompilerPath = settings.FSharpPath |> evaluate |> valueOrFail "Could not resolve path for F# compiler."
        member __.IsFsiAnyCpu = settings.VisualStudioFsiPrefer64 |> evaluate |> defaultArg false
        member __.FsiParams = settings.VisualStudioFsiParam |> evaluate |> defaultArg ""
        member __.VisualStudioPath = settings.VisualStudioPath |> evaluate |> valueOrFail "Could not resolve path for Visual Studio."
        member __.AppGuid = settings.PluginGuid