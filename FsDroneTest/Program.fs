open FsDrone
open System
open System.Threading
open System.Diagnostics

let cntlr = new DroneController()
let stop() = (cntlr :> IDisposable).Dispose()
(*
stop()
*)

let sub1 = cntlr.Monitor.Subscribe(fun msg -> Debug.WriteLine(sprintf "%A" msg))
let connectionCts = new CancellationTokenSource()
let sub2 = ref (None:IDisposable option)

let connect =
    async {
        do! cntlr.ConnectAsync(connectionCts)
        printfn "connected"
    }

Async.Start(connect)

System.Console.WriteLine("enter to send ack")
System.Console.ReadLine() |> ignore

let isLanded = function Landed      -> true | _ -> false
let isFlying = function Flying _    -> true | _ -> false

let takeoff = {Name="Takeoff"; Commands=WhenIn_RepeatTill (isLanded, Takeoff, isFlying)}
let ctrl = {Name="ControlAck"; Commands=One(Command.Ack)}

cntlr.Run ctrl

System.Console.WriteLine("enter to close")
System.Console.ReadLine() |> ignore
stop()


(*
cntlr.Emergency()
let isLanded = function Landed      -> true | _ -> false
let isFlying = function Flying _    -> true | _ -> false
System.Console.ReadLine() |> ignore

cntlr.Run (WhenRepeat (isLanded, Takeoff, isFlying))
*)