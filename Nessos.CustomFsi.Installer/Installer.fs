module internal Nessos.CustomFsi.Installer

    open System.IO
    open System.Diagnostics
    open System.Threading
    open System.Security
    open System.Security.Permissions
    open System.Security.Principal

    open Nessos.CustomFsi.Lib

    [<AutoOpen>]
    module internal Utils =

        let exitWait n =
            if isWindowedConsole then
                printf "Press any key to exit..."
                System.Console.ReadKey() |> ignore
            exit n

        // wtf??
        let checkForWritePermissions (dir : string) =
            try
                let test = Path.Combine(dir, ".test")
                Directory.CreateDirectory(test) |> ignore
                Directory.Delete(test)
            with _ -> failwith "Administrative permissions required."


        let registerExceptionHandler () =
            System.AppDomain.CurrentDomain.UnhandledException.Add(fun uea ->
                let e = uea.ExceptionObject :?> exn
                eprintfn "Error: %s" e.Message
                exitWait 10)


    [<AutoOpen>]
    module internal Installers =

        let fsi32 = "Fsi32.exe"
        let fsi64 = "Fsi64.exe"

        // recoverable file system operations
        let move (src : string, dst : string) =
            Reversible.ofPrimitive (fun () -> File.Move(src, dst))
                                    (fun () -> File.Move(dst, src)) id

        let copy (src : string, dst : string) =
            Reversible.ofPrimitive (fun () -> File.Copy(src, dst))
                                    (fun () -> File.Delete dst) id

        let delete (file : string) =
            let tmp = Path.GetTempFileName()
            Reversible.ofPrimitive (fun () ->   
                                        File.Copy(file, tmp, true); 
                                        try File.Delete file 
                                        with _ -> failwith "Cannot uninstall; fsi sessions appear to be running.")
                                    (fun () -> File.Copy(tmp, file))
                                    (fun () -> File.Delete tmp)

        let resolveCompilerPath (settings : SettingsResolver) =
            match settings.FSharpCompilerPath with
            | null -> failwith "Could not resolve F# interactive path."
            | path when not <| Directory.Exists path -> failwith "F# interactive path does not exist."
            | path -> path

        let isInstalled (fsiPath : string) =
            let inline (!) file = Path.Combine(fsiPath, file)
            let status = 
                [ ! fsi32 ; ! fsi32 + ".config" ; ! fsi64 ; ! fsi64 + ".config" ]
                |> List.map File.Exists

            if List.forall id status then true
            elif List.exists id status then failwith "F# interactive folder is corrupt; will not continue."
            else false

        let resolveVsixInstaller (settings : SettingsResolver) =
            let vsDir = settings.VisualStudioPath    
            let vsixInst = Path.Combine(vsDir, "VSIXInstaller.exe")
            if File.Exists vsixInst then vsixInst
            else
                failwith "Could not locate VSIX installer."

        let installVsPlugin vsixInstaller vsixFile =
            let proc = Process.Start(vsixInstaller, "/admin " + "\"" + vsixFile + "\"")
            while not proc.HasExited do Thread.Sleep(200)
            if proc.ExitCode <> 0 then
                failwith "Vsix installation failed."

        let unInstallVsPlugin vsixInstaller appGuid =
            let proc = Process.Start(vsixInstaller, "/admin " + "\"/u:" + appGuid + "\"")
            while not proc.HasExited do Thread.Sleep(200)
            if proc.ExitCode <> 0 then
                failwith "Vsix uninstallation failed."

        let install (sourceDir : string) (settings : SettingsResolver) =

            // preparation
            let fsiProxyPath = Path.Combine(sourceDir, "CustomFsi.Proxy.exe")
            let vsix = Path.Combine(sourceDir, settings.VsixFile)
            let vsixInstaller = resolveVsixInstaller settings

            if not <| File.Exists fsiProxyPath then
                failwithf "Missing file %s" fsiProxyPath

            if not <| File.Exists vsix then
                failwithf "Missing file %s" vsix

            let fsiPath = resolveCompilerPath settings

            if isInstalled fsiPath then failwith "F# interactive proxy appears to have already been installed."

            do checkForWritePermissions fsiPath

            let inline (!) fileName = Path.Combine(fsiPath, fileName)

            // installation workflow

            reversible {
                printfn "Installing F# interactive proxy..."

                // move Fsi.exe and FsiAnyCPU.exe to new locations  
                do! move (! "Fsi.exe", ! fsi32)
                do! move (! "Fsi.exe.config", ! fsi32 + ".config")
                do! move (! "FsiAnyCPU.exe", ! fsi64)
                do! move (! "FsiAnyCPU.exe.config", ! fsi64 + ".config")

                // install FsiProxy.exe
                do! copy (fsiProxyPath, ! "Fsi.exe")
                do! copy (fsiProxyPath, ! "FsiAnyCPU.exe")

                printfn "Installing Visual Studio Plugin..."

                // install the plugin
                do installVsPlugin vsixInstaller vsix
            } |> Reversible.run

        let uninstall (settings : SettingsResolver) =
            let fsiPath = resolveCompilerPath settings
            let vsixInstaller = resolveVsixInstaller settings
            let appGuid = settings.AppGuid

            if not <| isInstalled fsiPath then failwith "F# interactive proxy appears to have not been installed."

            do checkForWritePermissions fsiPath

            let inline (!) fileName = Path.Combine(fsiPath, fileName)

            reversible {

                printfn "Removing F# interactive proxy..."
                
                // remove proxy
                do! delete (! "Fsi.exe")
                do! delete (! "FsiAnyCPU.exe")
            
                // put back original configurations
                do! move (! fsi32, ! "Fsi.exe")
                do! move (! fsi32 + ".config", ! "Fsi.exe.config")
                do! move (! fsi64, ! "FsiAnyCPU.exe")
                do! move (! fsi64 + ".config", ! "FsiAnyCPU.exe.config")

                printfn "Uninstalling Visual Studio Plugin..."

                // uninstall plugin
                do unInstallVsPlugin vsixInstaller appGuid
            } |> Reversible.run


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


    [<EntryPoint>]
    let main args =

        let thisDirectory = System.Reflection.Assembly.GetEntryAssembly().Location |> Path.GetDirectoryName
        let settings = SettingsResolver.OfSettingsId("VS2013")

        let installOrUninstall = parseMode args

        try
            if installOrUninstall then
                printfn "This will install a stream proxy in over your installed FSI executables."
                printfn "It will also install the CustomFsi Visual Studio 2013 Plugin."
                printfn "Press any key to continue..."
                System.Console.ReadLine() |> ignore

                install thisDirectory settings

//                if not <| File.Exists fsiProxy then
//                    eprintfn "Error: FsiProxy.exe not found."
//                    exitWait 3
//
//                if not <| File.Exists vsix then
//                    eprintfn "Error: CustomFsi.vsix not found."
//                    exitWait 3
//
//                printfn "Installing F# interactive proxy..." ; install fsiProxy
//
//                match vsixInstaller.Value with
//                | None ->
//                    eprintfn "Could not locate Visual Studio 2013, will not install plugin."
//                | Some installer ->
//                    printfn "Installing Visual Studio Plugin..."
//                    installVsPlugin installer vsix
            else
                printfn "This will uninstall CustomFsi from your system."
                printfn "Press any key to continue..."
                System.Console.ReadLine() |> ignore

                uninstall settings
//                printfn "Removing F# interactive proxy..." ; uninstall ()

//                match vsixInstaller.Value with
//                | None ->
//                    eprintfn "Could not locate Visual Studio 2013, will not install plugin."
//                | Some installer ->
//                    printfn "Uninstalling Visual Studio Plugin..."
//                    unInstallVsPlugin installer resolver.AppGuid

        with e -> eprintfn "Error: %s" e.Message ; exitWait 2

        exitWait 0