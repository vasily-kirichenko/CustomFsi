namespace Nessos.CustomFsi.Lib

[<AutoOpen>]
module Utils =

    open System
    open System.Threading
    open System.IO

    open Microsoft.Win32

    /// strips values of reference types that are null
    let denull<'T when 'T : null> (x : 'T) =
        match x with null -> None | x -> Some x


    let selfProc = System.Diagnostics.Process.GetCurrentProcess()
    let isWindowedConsole = System.Environment.UserInteractive && selfProc.MainWindowHandle <> 0n

    type OptionBuilder() =
        member __.Zero () = None
        member __.Return x = Some x
        member __.Bind(x,f) = Option.bind f x
        member __.Combine(x : unit option, y : 'T option) = y
        member __.ReturnFrom (x : 'T option) = x

    let option = new OptionBuilder()

    // pipe two streams together
    type Pipe(input : TextReader, output : TextWriter, ?readLine, ?filter : string -> string, ?debug) =
        let debug = defaultArg debug false
        let readLine = defaultArg readLine true
        let filter = defaultArg filter id

        let eos =
            match input with
            | :? StreamReader as r -> fun () -> r.EndOfStream
            | _ -> fun () -> false

        let rec loop () =
            async {
                if eos () then return ()
                elif readLine then
                    let! line = input.ReadLineAsync () |> Async.AwaitTask
                    if debug then Console.WriteLine line
                    output.WriteLine (filter line)

                    return! loop ()
                else
                    let buf = [| '0' |]
                    let _ = input.Read(buf,0,1)

                    output.Write(buf.[0])

                    return! loop ()
            }

        let cts = new CancellationTokenSource()

        do Async.Start(loop (), cts.Token)

        interface IDisposable with member __.Dispose() = cts.Cancel()


    // primitive parser

    let private parsers =
        let inline dc (f : string -> 'T) = (typeof<'T>, f :> obj)
        [
            dc System.Boolean.Parse
            dc int32
            dc float
            dc float
            dc int64
            dc int16
            dc uint16
            dc uint32
            dc uint64
            dc sbyte
            dc decimal
            dc System.DateTime.Parse
            dc System.Guid.Parse
            dc id
        ] 
        |> Seq.map(fun (t,p) -> t.MetadataToken, p)
        |> Map.ofSeq

    let tryGetParser<'T> = 
        let inline uc (x : obj) = x :?> 'T
        parsers.TryFind typeof<'T>.MetadataToken
        |> Option.map (fun p -> p :?> string -> 'T)