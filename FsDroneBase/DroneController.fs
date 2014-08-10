//implements the state machines for connection management and sending commands
//also provides monitoring of drone connection
namespace FsDrone
open Extensions
open System.Threading
open System
open FsDrone

type private ControllerConnectionState = Disconnected | Connecting of CancellationTokenSource | Connected of DroneConnection

type FDS = DroneState -> bool

type CommandScript = 
    | Single of Command
    | WhenRepeat of (FDS * Command * FDS) //if in start state, execute command till end state reached
    | Repeat of (Command * FDS)           //repeat till end state reached
    | Sequence of CommandScript list

module private ControllerServices = 

    let tryConnectAsync fMonitor =
        let rec loop() =
            async {
                let dc = ref None
                try dc := Some (new DroneConnection(fMonitor)) with ex -> logEx ex
                match !dc with 
                | Some c -> return c
                | None -> 
                    do! Async.Sleep 1000
                    return! loop() }
        loop()

    let inline singleStep fPost = function
        | Single cmd, _                                 -> fPost cmd; None
        | WhenRepeat (fds1,cmd,fds2), ds when (fds1 ds) -> fPost cmd; (Repeat(cmd,fds2) |> Some)
        | WhenRepeat _, _                               -> None
        | Repeat (_,fds), ds when (fds ds)              -> None
        | Repeat (cmd,_) as scr, _                      -> fPost cmd; (Some scr)
        | Sequence _, _                                 -> failwithf "sequence not expected"

    let errorNoConnection = ScriptError "No Connection for Script Command"

    let scriptRunner fConnection fDroneState fMonitor (inbox:Agent<CommandScript>)=
        let rec loop prevScr =
            async {
                let! newScr = inbox.TryReceive(30)
                match fConnection() with
                | Disconnected | Connecting _ -> return! loop None
                | Connected cnn ->
                    let scr = if newScr.IsSome then newScr else prevScr //new script overrides previous
                    let fPost = cnn.Cmds.Post
                    match scr, fDroneState() with
                    | None, Flying _  | Some (Sequence []), Flying _ -> fPost Hover; return! loop None              
                    | None, _         | Some (Sequence []), _        -> return! loop None
                    | Some scr, ds ->
                        match scr with
                        | Sequence (scr::rest) ->
                            match singleStep fPost (scr,ds) with
                            | None     -> return! loop (Some (Sequence rest))
                            | Some scr -> return! loop (Some (Sequence(scr::rest)))
                        | scr -> 
                            match singleStep fPost (scr,ds) with
                            | None -> return! loop None
                            | scr  -> return! loop scr
            }
        loop None

type DroneController() = 

    let cts = new System.Threading.CancellationTokenSource()

    let mutable connection = Disconnected
    let setConnection conn = connection <- Connected conn

    let mutable droneState = DroneState.Default

    let monitorObservable,fMonitor = Observable.createObservableAgent(cts.Token)

    let connectAsync (cts:CancellationTokenSource) = 
        async {
            if cts.IsCancellationRequested then failwith "Controller is disposed - please re-instantiate"
            match connection with 
            | Disconnected ->
                connection <- Connecting cts
                let! conn = ControllerServices.tryConnectAsync fMonitor
                do setConnection conn
                fMonitor (ConnectionState (ConnectionState.Connected conn.Telemetry ))
                return conn.Telemetry
            | Connecting  _ -> return failwith "Connection started on previous call - please disconnect first or wait for connection"
            | Connected c -> return c.Telemetry
        }
                
    let disconnect() =
        match connection with
        | Connected c       -> (c :> IDisposable).Dispose()
        | Connecting cts    -> cts.Cancel()
        | Disconnected      -> ()
        connection <- Disconnected
        fMonitor (ConnectionState ConnectionState.Disconnected)

    let fScriptRunner = ControllerServices.scriptRunner  (fun()->connection) (fun()->droneState) fMonitor

    let scriptAgent = Agent.Start(fScriptRunner, cts.Token)

    member x.ConnectAsync (cts:CancellationTokenSource) = connectAsync cts
    member x.Disconnect()   = disconnect()
    member x.Emergency()    = match connection with Connected c -> c.Cmds.Post Emergency | _ -> ()
    member x.Run script     = scriptAgent.Post script
    member x.Monitor        = monitorObservable

    interface IDisposable with
        member x.Dispose() =
            disconnect()
            cts.Cancel()
 
