module internal Nessos.CustomFsi.Installer

    open System.IO
    open System
    open System.Diagnostics
    open System.Threading
    open System.Security
    open System.Security.Permissions
    open System.Security.Principal

    open Nessos.Reversible
    open Nessos.InstallUtils

    open Nessos.CustomFsi.Lib

    [<AutoOpen>]
    module internal Utils =

        let exitWait n =
            if isWindowedConsole then
                printf "Press any key to exit..."
                System.Console.ReadKey() |> ignore
            exit n

        let checkForWritePermissions (dir : string) =
            try
                let test = Path.Combine(dir, ".test")
                Directory.CreateDirectory(test) |> ignore
                Directory.Delete(test)
            with _ -> failwith "Administrative permissions required."


    [<AutoOpen>]
    module internal Installers =

        let fsi32 = "Fsi32.exe"
        let fsi64 = "Fsi64.exe"

        let resolveCompilerPath (settings : SettingsResolver) =
            match settings.FSharpCompilerPath with
            | null -> failwith "Could not resolve F# interactive path."
            | path when not <| Directory.Exists path -> failwith "F# interactive path does not exist."
            | path -> path

        let isInstalled (settings : SettingsResolver) =
            let fsiPath = settings.FSharpCompilerPath
            let inline (!) file = Path.Combine(fsiPath, file)
            [ ! fsi32 ; ! fsi64 ] |> List.exists File.Exists

        /// check state of F# compiler directory; fail if inconsistent.
        /// return true if installed, false otherwise
        let checkInstallationState (fsiPath : string) =
            let inline (!) file = Path.Combine(fsiPath, file)
            let status = 
                [ ! fsi32 ; ! fsi32 + ".config" ; ! fsi64 ; ! fsi64 + ".config" ]
                |> List.map File.Exists

            if List.forall id status then true
            elif List.exists id status then failwith "F# interactive folder is corrupt; will not continue."
            else false

        let tryResolveVsixInstaller (settings : SettingsResolver) =
            let vsDir = settings.VisualStudioPath    
            let vsixInst = Path.Combine(vsDir, "VSIXInstaller.exe")
            if File.Exists vsixInst then Some vsixInst
            else
                None

        let installVsPlugin vsixInstaller vsixFile =
            let proc = Process.Start(vsixInstaller, "/admin " + "\"" + vsixFile + "\"")
            while not proc.HasExited do Thread.Sleep(200)
//            if proc.ExitCode <> 0 then
//                failwith "Vsix installation failed."

        let unInstallVsPlugin vsixInstaller appGuid =
            let proc = Process.Start(vsixInstaller, "/admin " + "\"/u:" + appGuid + "\"")
            while not proc.HasExited do Thread.Sleep(200)
//            if proc.ExitCode <> 0 then
//                failwith "Vsix uninstallation failed."
        
        let defineStandaloneIcon installPath = 
            let target = Path.Combine(installPath, "CustomFsi.Standalone.exe")
            ShortcutDescriptor.Create("CustomFsi", Environment.SpecialFolder.Desktop, target)

        let defineUninstallerIcon installPath =
            let target = Path.Combine(installPath, "CustomFsi.Installer.exe")
            let icon = Path.Combine(installPath, "mbrace.ico")
            ShortcutDescriptor.Create("Uninstall CustomFsi", Environment.SpecialFolder.Desktop, target, arguments = "--uninstall", iconLocation = icon)

        let installLocation = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles, "Nessos", "CustomFsi")
        
        let installFiles (source : string) (location : string) =
            reversible {
                do! Directory.RevCopyRecursive(source, location, overwrite = true)

                do! ShortcutDescriptor.RevCreate (defineStandaloneIcon location)
                do! ShortcutDescriptor.RevCreate (defineUninstallerIcon location)
            } |> Reversible.RunWithRecovery

        let uninstallFiles (location : string) =
            reversible {
                do! ShortcutDescriptor.RevDelete (defineStandaloneIcon location)
                do! ShortcutDescriptor.RevDelete (defineUninstallerIcon location)
            } |> Reversible.RunWithRecovery

        let install (sourceDir : string) (settings : SettingsResolver) =

            // preparation
            
            let fsiProxyPath = Path.Combine(sourceDir, "CustomFsi.Proxy.exe")
            let vsix = Path.Combine(sourceDir, settings.VsixFile)
            let vsixInstaller = tryResolveVsixInstaller settings

            if not <| File.Exists fsiProxyPath then
                failwithf "Missing file %s" fsiProxyPath

            if not <| File.Exists vsix then
                failwithf "Missing file %s" vsix

            let fsiPath = resolveCompilerPath settings

            if checkInstallationState fsiPath then failwith "F# interactive proxy appears to have already been installed."

            do checkForWritePermissions fsiPath

            let inline (!) fileName = Path.Combine(fsiPath, fileName)

            // installation workflow

            reversible {

                printfn "Installing F# interactive proxy..."

                // move Fsi.exe and FsiAnyCPU.exe to new locations  
                do! File.RevMove(! "Fsi.exe", ! fsi32)
                do! File.RevMove(! "Fsi.exe.config", ! fsi32 + ".config")
                do! File.RevMove(! "FsiAnyCPU.exe", ! fsi64)
                do! File.RevMove(! "FsiAnyCPU.exe.config", ! fsi64 + ".config")

                // install FsiProxy.exe
                do! File.RevCopy(fsiProxyPath, ! "Fsi.exe")
                do! File.RevCopy(fsiProxyPath, ! "FsiAnyCPU.exe")

                match vsixInstaller with
                | None -> ()
                | Some installer ->
                    // install the plugin
                    printfn "Installing Visual Studio Plugin..."

                    do installVsPlugin installer vsix

                do settings.SetPluginInstallationStatus(true)

            } |> Reversible.RunWithRecovery



        let uninstall (settings : SettingsResolver) =
            let fsiPath = resolveCompilerPath settings
            let vsixInstaller = tryResolveVsixInstaller settings
            let appGuid = settings.AppGuid

            if not <| checkInstallationState fsiPath then failwith "F# interactive proxy appears to have not been installed."

            do checkForWritePermissions fsiPath

            let inline (!) fileName = Path.Combine(fsiPath, fileName)

            reversible {

                printfn "Removing F# interactive proxy..."
            
                // put back original files
                do! File.RevMove(! fsi32, ! "Fsi.exe", overwrite = true)
                do! File.RevMove(! fsi32 + ".config", ! "Fsi.exe.config", overwrite = true)
                do! File.RevMove(! fsi64, ! "FsiAnyCPU.exe", overwrite = true)
                do! File.RevMove(! fsi64 + ".config", ! "FsiAnyCPU.exe.config", overwrite = true)

                // uninstall plugin
                match vsixInstaller with
                | None -> ()
                | Some installer ->
                    printfn "Uninstalling Visual Studio Plugin..."
                    do unInstallVsPlugin installer appGuid

                do settings.SetPluginInstallationStatus(false)

            } |> Reversible.RunWithRecovery


    let parseMode (args : string []) =
        let usageAndExit () =
            let exe = System.Reflection.Assembly.GetExecutingAssembly().Location |> Path.GetFileName
            eprintfn "USAGE: %s [ --install| --uninstall]" exe
            exitWait 1

        if args.Length = 0 then true
        else
            match args.[0] with
            | "--install" -> true
            | "--uninstall" ->  false
            | _ -> usageAndExit ()


    let promptSelection (candidates : SettingsResolver list) =
        let options = candidates |> List.map (fun c -> { Value = c; Description = c.Name ; InitiallyEnabled = true})
        Console.PromptComponents options

    [<EntryPoint>]
    let main args =

        let thisDirectory = System.Reflection.Assembly.GetEntryAssembly().Location |> Path.GetDirectoryName

        let installOrUninstall = parseMode args

        try
            if installOrUninstall then
                printfn "This will install a stream proxy in over your installed FSI executables."
                printfn "It will also install the CustomFsi Visual Studio 2012/2013 Plugin."
                printfn "Press any key to continue..."
                System.Console.ReadLine() |> ignore

                match SettingsResolver.GetAllConfigurations() |> List.filter(fun s -> s.IsVisualStudioInstalled && not <| isInstalled s) with
                | [] -> eprintfn "No Visual Studio installations found or plugin already installed."
                | targets ->
                    let chosen = promptSelection targets

                    for setting in chosen do
                        install thisDirectory setting

                    printfn "Copying files to %s..." installLocation
                    installFiles thisDirectory installLocation

            else
                printfn "This will uninstall CustomFsi from your system."
                printfn "Press any key to continue..."
                System.Console.ReadLine() |> ignore

                match SettingsResolver.GetAllConfigurations() |> List.filter (fun s -> s.IsVisualStudioInstalled &&  isInstalled s) with
                | [] -> printfn "Nothing to uninstall."
                | targets ->
                    let chosen = promptSelection targets

                    for setting in chosen do
                        uninstall setting

                    if chosen.Length = targets.Length then
                        uninstallFiles installLocation

        with e -> eprintfn "Error: %A" e ; exitWait 2

        exitWait 0