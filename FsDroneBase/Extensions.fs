module Extensions
open System
open System.IO
open System.Text
open System.Net
open System.Net.Sockets
open System.Collections.Generic

type Agent<'a> = MailboxProcessor<'a>
type RC<'a>    = AsyncReplyChannel<'a>

let log (s:string)  = System.Console.WriteLine s
let logEx (ex:Exception) = log (sprintf "%s\r%s" ex.Message ex.StackTrace)

type Socket with
    member x.AsyncReceiveFrom (buffer:byte array, endpoint:EndPoint ref) =
        let fBegin = fun (ba,ep,cb,st) -> x.BeginReceiveFrom(ba,0,ba.Length,SocketFlags.None,ep,cb,st)
        let fEnd   = fun ar -> x.EndReceiveFrom(ar,endpoint)
        Async.FromBeginEnd(buffer, endpoint, fBegin, fEnd)

    member x.AsycReceive(buffer:byte array)  = 
        let fBegin = fun (ba,cb,st) -> x.BeginReceive(ba,0,ba.Length,SocketFlags.None,cb,st)
        let fEnd   = fun ar -> x.EndReceive(ar)
        Async.FromBeginEnd(buffer, fBegin, fEnd)

type System.IO.Stream with
    member x.Skip n = x.Position <- x.Position + n

module Array =
    /// scans the array backwards from startAt, 2 elements at a time
    /// and evaluates the predicate function f
    /// returns the index when f evalutes to true else -1
    let reverseFind startAt f xs =
        let mutable found = -1
        let mutable idx = startAt
        while idx >= 0 && found = -1 do
            if f (xs,idx) then found <- idx
            idx <- idx - 1
        found
            
module Observable =
    let createObservableAgent<'T> (token:System.Threading.CancellationToken) =
        let finished = ref false
        let subscribers = ref (Map.empty : Map<int, IObserver<'T>>)

        let inline publish msg = 
            !subscribers 
            |> Seq.iter (fun (KeyValue(_, sub)) ->
                try
                     sub.OnNext(msg)
                with ex -> 
                    System.Diagnostics.Debug.Write(ex))

        let completed() = 
            lock subscribers (fun () ->
            finished := true
            !subscribers |> Seq.iter (fun (KeyValue(_, sub)) -> sub.OnCompleted())
            subscribers := Map.empty)

        token.Register(fun () -> completed()) |> ignore //callback for when token is cancelled
            
        let count = ref 0
        let agent =
            MailboxProcessor.Start
                ((fun inbox ->
                    async {
                        while true do
                            let! msg = inbox.Receive()
                            publish msg} ),
                 token)
        let obs = 
            { new IObservable<'T> with 
                member this.Subscribe(obs) =
                    let key1 =
                        lock subscribers (fun () ->
                            if !finished then failwith "Observable has already completed"
                            let key1 = !count
                            count := !count + 1
                            subscribers := subscribers.Value.Add(key1, obs)
                            key1)
                    { new IDisposable with  
                        member this.Dispose() = 
                            lock subscribers (fun () -> 
                                subscribers := subscribers.Value.Remove(key1)) } }
        obs,agent.Post

    let till  (f:'T -> bool) (obs:IObservable<'T>) = 
        let subs = ref Unchecked.defaultof<IDisposable>
        subs := obs.Subscribe(fun x -> if f x then subs.Value.Dispose() else ())
        ()
