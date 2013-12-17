module internal Nessos.CustomFsi.Proxy

    open System
    open System.IO
    open System.Diagnostics
    open System.Text
    open System.Text.RegularExpressions
    open System.Reflection

    open Nessos.CustomFsi.Lib

    [<AutoOpen>]
    module internal Utils =

        let thisExecutable = lazy(System.Reflection.Assembly.GetExecutingAssembly().Location)
        let defaultExe = lazy(if RegistryResolver.FsiAnyCpu then "Fsi64.exe" else "Fsi32.exe")

        let assertCustomExecutable (path : string) =
            let errorPrint reason code =
                eprintfn "The F# Interactive path has been set in \"Tools > CustomFsi\"."
                eprintfn "%s" reason
                eprintfn "The given file path is:"
                eprintfn ""
                eprintfn "  %s" path
                eprintfn ""
                eprintfn "You can either:"
                eprintfn "a) correct that path to point to the %s executable, or" defaultExe.Value
                eprintfn "b) clear it and then the default %s will be used." defaultExe.Value
                exit code

            let getPublicKey(file : string) = 
                try (AssemblyName.GetAssemblyName file).GetPublicKey() |> Some with _ -> None
            
            if not <| File.Exists path then
                errorPrint "However, the given file path does not exist (it is not a file)." 3
            else
                match getPublicKey path with
                | None -> errorPrint "However, the given file path is not a valid Fsi executable." 3
                | key when getPublicKey thisExecutable.Value = key ->
                    // preempt cases of FsiProxy calling itself...
                    errorPrint "However, the given file path is not a valid Fsi executable." 3
                | _ -> ()

        let resolveFsiPath () =
            let registryPath () =
                option {
                    if RegistryResolver.PluginEnabled then
                        match RegistryResolver.CustomFsiPath with
                        | null | "" -> return! None
                        | path ->
                            do assertCustomExecutable path
                            return path
                }

            let defaultPath () =
                let defaultExe =
                    option {
                        let! compilerDir = denull RegistryResolver.FsCompilerPath
                        let file = Path.Combine(compilerDir, defaultExe.Value)

                        if File.Exists file then return file
                        else return! None
                    }

                match defaultExe with 
                | Some e -> e 
                | None -> eprintfn "CustomFsi: could not locate default Fsi executable." ; exit 4

            match registryPath () with
            | None -> defaultPath ()
            | Some path -> path

        let isServerMode(args : string []) =
            args |> Array.exists (fun arg -> arg.Contains("--fsi-server"))

        // fixes curious bug in server mode where stdin ident is printed with a path prefix, ie.
        // "C:\Development\VS 2012\Microsoft.FSharp\src\mbi\bin\Debug\stdin(3,1): error FS0039:"
        //
        //  instead of "stdin(3,1): error FS0039:"
        let filterStdErr fsiPath =
            let prefix = Path.GetDirectoryName(fsiPath) |> Regex.Escape
            let regex = new Regex(sprintf "^%s\\\\(stdin\([0-9]*,[0-9]*\): error)" prefix)
            fun (inp : string) -> regex.Replace(inp, fun (m: Match) -> m.Groups.[1].Value)

        let filterTabs (x : string) = x.Replace("\t", "    ")

        let printNonServerModeWarning fsiExe =
            let actualFsi = 
                match (Path.GetFileName thisExecutable.Value).ToLower() with
                | "fsianycpu.exe" -> "Fsi64.exe"
                | _ -> "Fsi32.exe"

            eprintfn "CustomFSI: relaying to '%s'." fsiExe

//            Console.ForegroundColor <- ConsoleColor.Red
//            eprintfn "WARNING: you are using Fsi through a stream proxy."
//            eprintfn "Some features of the command line might become unavailable."
//            eprintfn "It is recommended that you run %s directly instead." actualFsi
//            Console.ResetColor()

        let spawnShell encoding (fsiExe : string) (args : string []) =
            try
                let foldedArgs = args |> Seq.map (fun a -> "\"" + a + "\"") |> String.concat " "
                let pInfo = new ProcessStartInfo(fsiExe, foldedArgs)

                pInfo.WorkingDirectory <- Directory.GetCurrentDirectory()
                pInfo.UseShellExecute <- false
                pInfo.StandardErrorEncoding <- encoding
                pInfo.StandardOutputEncoding <- encoding
                pInfo.RedirectStandardError <- true
                pInfo.RedirectStandardInput <- true
                pInfo.RedirectStandardOutput <- true
                pInfo.CreateNoWindow <- true

                Process.Start pInfo
            with e -> 
                eprintfn "Failed to spawn fsi in path %s" fsiExe
                exit 2

        let rec mainloop (proc : Process) =
            async {
                try
                    if proc.HasExited then return proc.ExitCode
                    else
                        do! Async.Sleep 500
                        return! mainloop proc
                with _ -> return 1
            }


    [<EntryPoint>]
    let main args =

        let serverMode = isServerMode args
        let fsiExe = resolveFsiPath ()

        if args.Length = 0 then printNonServerModeWarning fsiExe

        let proc = spawnShell System.Console.OutputEncoding fsiExe args

        let _ = new Pipe(proc.StandardOutput, Console.Out, readLine = serverMode)
        let _ = new Pipe(proc.StandardError, Console.Error, readLine = true, filter = filterStdErr fsiExe)
        let _ = new Pipe(Console.In, proc.StandardInput, readLine = true, filter = if serverMode then id else filterTabs)

        mainloop proc |> Async.RunSynchronously