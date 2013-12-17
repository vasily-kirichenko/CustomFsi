﻿namespace Nessos.CustomFsi.Lib

    [<AutoOpen>]
    module Reversible =

        open System.IO
        open System.Runtime.Serialization.Formatters.Binary

        type Reversible<'T> = private { Untyped : ReversibleExpr }

        // defines the syntactic monad over reversible primitives
        and private ReversibleExpr =
            | Primitive of ReversiblePrimitive
            | Bind of ReversibleExpr * (obj -> ReversibleExpr)
            | Sequential of ReversibleExpr seq
            // execution-specific branches
            | Continuation of (obj -> ReversibleExpr)
            | Value of obj * System.Type

        // untyped reversible computation primitive
        and private ReversiblePrimitive =
            {
                Execute : unit -> obj // untyped unit -> 'T
                Recover : obj -> unit // untyped 'T -> unit
                Finally : unit -> unit
                Type : System.Type
            }

        and ReversibleBuilder() =
            member r.Return (x : 'T) : Reversible<'T> =
                let primitive = { Execute = (fun () -> x :> obj) ; Recover = ignore ; Finally = id ; Type = typeof<'T> }
                { Untyped = Primitive primitive }
            member r.Bind(f : Reversible<'T>, g : 'T -> Reversible<'S>) : Reversible<'S> =
                let g0 (o : obj) = (g (o :?> 'T)).Untyped
                { Untyped = Bind(f.Untyped, g0) }

            member r.Zero() = r.Return ()
            member r.Delay(f : unit -> Reversible<'T>) = r.Bind(r.Zero(), f)
            member r.Combine(f : Reversible<unit>, g : Reversible<'T>) = r.Bind(f, fun () -> g)
            member r.For(xs : 'T seq, f : 'T -> Reversible<unit>) : Reversible<unit> = 
                { Untyped = Sequential(seq { for t in xs -> (f t).Untyped } |> Seq.cache) }
            member r.While(b : unit -> bool, f : Reversible<unit>) : Reversible<unit> =
                { Untyped = Sequential(seq { while b () do yield f.Untyped } |> Seq.cache) }

        let reversible = new ReversibleBuilder()


        [<RequireQualifiedAccess>]
        module Reversible =

            let run (f : Reversible<'T>) =
                let iter fs = List.iter (fun f -> f ()) fs

                // deep clone an object
                let clone (o : obj) =
                    try
                        let bfs = new BinaryFormatter()
                        use stream = new MemoryStream()
                        do bfs.Serialize(stream, o); stream.Position <- 0L
                        bfs.Deserialize(stream)
                    with _ -> o

                // lazy seq deconstructor AP
                let (|Cons|Nil|) (t : _ seq) =
                    if Seq.isEmpty t then Nil
                    else Cons(Seq.head t, Seq.skip 1 t)

                let rec eval ((recovs, finals) as state) =
                    function
                    | (Bind (f, g)) :: rest -> eval state (f :: Continuation g :: rest)
                    | Sequential(Cons(f, fs)) :: rest -> eval state (f :: Sequential fs :: rest)
                    | Sequential Nil :: rest -> eval state (Value (null,typeof<unit>) :: rest)
                    | Primitive { Execute = exec ; Recover = recov ; Finally = final ; Type = t } :: rest ->
                        let result, state' = 
                            try 
                                let result = exec ()
                                let closure = let r = clone result in fun () -> recov r
                                result, (closure :: recovs, final :: finals)
                            with e ->
                                iter recovs;
                                iter (List.rev finals);
                                raise e
                        eval state' (Value (result, t) :: rest)
                    | Value (o,_) :: Continuation f :: rest -> eval state (f o :: rest)
                    | Value (o,_) :: [] -> iter (List.rev finals) ; o
                    | Value (_,t) :: rest when t = typeof<unit> -> eval state rest
                    | stack -> failwithf "stack error: %A" stack

                eval ([],[]) [f.Untyped] :?> 'T

            let ofPrimitive (execute : unit -> 'T) (recover : 'T -> unit) (final : unit -> unit) =
                let primitive = 
                    { 
                        Execute = fun () -> execute () :> obj
                        Recover = fun o -> recover (o :?> 'T)
                        Finally = final
                        Type = typeof<'T>
                    }
                { Untyped = Primitive primitive } : Reversible<'T>

            let failwith msg = ofPrimitive (fun () -> failwith msg) ignore id