module internal Nessos.CustomFsi.Installer

    open System.IO
    open System.Diagnostics
    open System.Security
    open System.Security.Permissions
    open System.Security.Principal

    open Nessos.CustomFsi.Lib

    [<AutoOpen>]
    module internal Common =

        let fsi32 = "Fsi32.exe"
        let fsi64 = "Fsi64.exe"

        let fsiPath = lazy(
            match RegistryResolver.FsCompilerPath with
            | null -> failwith "Could not resolve F# interactive path."
            | path when not <| Directory.Exists path -> failwith "F# interactive path does not exist."
            | path -> path)

        let vsixInstaller = lazy(
            match RegistryResolver.VsDir with
            | null -> None
            | vsDir ->
                let vsixInst = Path.Combine(vsDir, "VSIXInstaller.exe")
                if File.Exists vsixInst then Some vsixInst
                else None)

        let (!) fileName = Path.Combine(fsiPath.Value, fileName)

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
            

        // checks if the proxy is installed
        let isInstalled () =
            let status = 
                [ ! fsi32 ; ! fsi32 + ".config" ; ! fsi64 ; !fsi64 + ".config" ]
                |> List.map File.Exists

            if List.forall id status then true
            elif List.exists id status then failwith "F# interactive folder is corrupt; will not continue."
            else false

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

    let install (fsiProxyPath : string) =
        if not <| File.Exists fsiProxyPath then
            failwithf "Missing file %s" fsiProxyPath

        let _ = fsiPath.Force()

        do checkForWritePermissions fsiPath.Value

        if isInstalled () then failwith "F# interactive proxy appears to have already been installed."

        reversible {
            // move Fsi.exe and FsiAnyCPU.exe to new locations  
            do! move (! "Fsi.exe", ! fsi32)
            do! move (! "Fsi.exe.config", ! fsi32 + ".config")
            do! move (! "FsiAnyCPU.exe", ! fsi64)
            do! move (! "FsiAnyCPU.exe.config", ! fsi64 + ".config")

            // install FsiProxy.exe
            do! copy (fsiProxyPath, ! "Fsi.exe")
            do! copy (fsiProxyPath, ! "FsiAnyCPU.exe")
        } |> Reversible.run

    let uninstall () =
        let _ = fsiPath.Force()

        do checkForWritePermissions fsiPath.Value

        if not <| isInstalled () then failwith "F# interactive proxy appears to have not been installed."

        reversible {
            do! delete (! "Fsi.exe")
            do! delete (! "FsiAnyCPU.exe")
            
            do! move (! fsi32, ! "Fsi.exe")
            do! move (! fsi32 + ".config", ! "Fsi.exe.config")
            do! move (! fsi64, ! "FsiAnyCPU.exe")
            do! move (! fsi64 + ".config", ! "FsiAnyCPU.exe.config")
        } |> Reversible.run

    let installVsPlugin installer vsix =
        let proc = Process.Start(installer, "/admin " + "\"" + vsix + "\"")

        async { while not proc.HasExited do do! Async.Sleep 200 } |> Async.RunSynchronously

    let unInstallVsPlugin installer (guid : string) =
        let proc = Process.Start(installer, "/admin " + "\"/u:" + guid + "\"")

        async { while not proc.HasExited do do! Async.Sleep 200 } |> Async.RunSynchronously

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
        let fsiProxy = Path.Combine(thisDirectory, "CustomFsi.Proxy.exe")
        let vsix = Path.Combine(thisDirectory, "CustomFsi.vsix")

        let installOrUninstall = parseMode args

        try
            if installOrUninstall then
                printfn "This will install a stream proxy in over your installed FSI executables."
                printfn "It will also install the CustomFsi Visual Studio 2013 Plugin."
                printfn "Press any key to continue..."
                System.Console.ReadLine() |> ignore

                if not <| File.Exists fsiProxy then
                    eprintfn "Error: FsiProxy.exe not found."
                    exitWait 3

                if not <| File.Exists vsix then
                    eprintfn "Error: CustomFsi.vsix not found."
                    exitWait 3

                printfn "Installing F# interactive proxy..." ; install fsiProxy

                match vsixInstaller.Value with
                | None ->
                    eprintfn "Could not locate Visual Studio 2013, will not install plugin."
                | Some installer ->
                    printfn "Installing Visual Studio Plugin..."
                    installVsPlugin installer vsix
            else
                printfn "Removing F# interactive proxy..." ; uninstall ()

                match vsixInstaller.Value with
                | None ->
                    eprintfn "Could not locate Visual Studio 2013, will not install plugin."
                | Some installer ->
                    printfn "Uninstalling Visual Studio Plugin..."
                    unInstallVsPlugin installer RegistryResolver.AppGuid

        with e -> eprintfn "Error: %s" e.Message ; exitWait 2

        exitWait 0