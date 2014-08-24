//implements the state machines for connection management and sending commands
//also provides monitoring of drone connection
namespace FsDrone
open Extensions
open System.Threading
open System
open FsDrone

type private ControllerConnectionState = 
    | Disconnected 
    | Connecting of CancellationTokenSource 
    | Connected of DroneConnection

type FDS = DroneState       -> bool
type FTM = Telemetry        -> bool
type FCS = ConfigSetting    -> bool
type TimeoutMS = int

type StepResult<'T> = Continue of 'T| Done | Timeout

type CommandScript = 
    | Single of Command
    | WhenRepeat of (FDS * Command * FDS) //if in start state, execute command till end state reached
    | Repeat of (Command * FDS)           //repeat till end state reached
    | AwaitTelemetry of FTM * TimeoutMS
    | AwaitConfig of FCS * TimeoutMS
    | Sequence of CommandScript list

module private ControllerServices = 

    let tryConnectAsync fMonitor fTelemetry fConfig =
        let rec loop() =
            async {
                let dc = ref None
                try dc := Some (new DroneConnection(fMonitor,fTelemetry, fConfig)) with ex -> logEx ex
                match !dc with 
                | Some c -> return c
                | None -> 
                    do! Async.Sleep 1000
                    return! loop() }
        loop()

    let inline awaitTill ftm timeout token obs noTry =
        let step = async {obs |> Observable.till ftm }
        try 
            Async.RunSynchronously(step,timeout,token) 
        with ex -> 
            if noTry then raise ex else ()
        Done

    let inline singleStep fPost telemetryObs configObs token = function
        | Single cmd, _                                 -> fPost cmd; Done
        | WhenRepeat (fds1,cmd,fds2), ds when (fds1 ds) -> fPost cmd; (Repeat(cmd,fds2) |> Continue
        | WhenRepeat _, _                               -> Done
        | Repeat (_,fds), ds when (fds ds)              -> Done
        | Repeat (cmd,_) as scr, _                      -> fPost cmd; (Continue scr)
        | AwaitTelemetry (ftm,timeout),_                  -> awaitTill ftm timeout telemetryObs token false
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

    let monitorObservable , fMonitor    = Observable.createObservableAgent(cts.Token)
    let telemtryObservable, fTelemetry  = Observable.createObservableAgent(cts.Token)
    let configObservable  , fConfig     = Observable.createObservableAgent(cts.Token)

    let connectAsync (cts:CancellationTokenSource) = 
        async {
            if cts.IsCancellationRequested then failwith "Controller is disposed - please re-instantiate"
            match connection with 
            | Disconnected ->
                connection <- Connecting cts
                let! conn = ControllerServices.tryConnectAsync fMonitor fTelemetry fConfig
                connection <- Connected conn
                fMonitor (ConnectionState (ConnectionState.Connected conn.Cmds ))
            | Connecting  _ -> return failwith "Connection in progress - please wait or disconnect"
            | Connected c -> ()
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
    member x.Monitor        = monitorObservable
    member x.Telemetry      = telemtryObservable
    member x.ConfigObs      = configObservable
    member x.Run script     = scriptAgent.Post script

    interface IDisposable with
        member x.Dispose() =
            disconnect()
            cts.Cancel()
 
