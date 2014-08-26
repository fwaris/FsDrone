//implements the 'scripting language' for executing drone control sequences
//the script can coordiate over command and telemetry channels or command and config channels
namespace FsDrone
open Extensions
open System
open FsDrone

type FDS = DroneState       -> bool
type FTM = Telemetry        -> bool
type FCS = ConfigSetting    -> bool

type TimeoutMS = int

type StepResult<'T> = Continue of 'T| Done | Abort

type ScriptCommand = 
    | One of Command
    | WhenIn_RepeatTill of FDS * Command * FDS  //if in start state, execute command till end state reached
    | RepeatTill of Command * FDS               //repeat till end state reached
    | AwaitTelemetry of FTM * TimeoutMS         //wait till a Telemetry event occurs, script execution halts if timeout occurs first
    | AwaitConfig of FCS * TimeoutMS            //wait till a config event is received
    | Sequence of ScriptCommand list

type Script = {Name:string; Commands:ScriptCommand}

module ScriptServices =

    let inline waitTill ftm timeout continueOnTimeout fError obs =
        async {
            try 
                let! r = Async.StartChild(async {obs |> Observable.till ftm },timeout) 
                do! r
                return Done
            with ex -> 
                fError "await timeout"
                if continueOnTimeout then return Done else return Abort
        }

    let inline singleStep fPost telemetryObs configObs fError cmd_State = 
        async {
            match cmd_State with
            | One cmd, _                                            -> fPost cmd; return Done
            | WhenIn_RepeatTill (fds1,cmd,fds2), ds when (fds1 ds)  -> fPost cmd; return (Continue (RepeatTill(cmd,fds2)))
            | WhenIn_RepeatTill _, _                                -> return Done
            | RepeatTill (_,fds), ds when (fds ds)                  -> return Done
            | RepeatTill (cmd,_) as scr, _                          -> fPost cmd; return (Continue scr)
            | AwaitTelemetry (ftm,timeout),_                        -> return! waitTill ftm timeout false fError telemetryObs
            | AwaitConfig (fcm,timeout),_                           -> return! waitTill fcm timeout false fError configObs
            | Sequence _, _                                         -> return failwithf "sequence not expected"
        }

    let executeScript fDroneState fPost telemetryObs configObs fMonitor script =
        let fError s = (script.Name,s) |> ScriptError  |> fMonitor
        let step = singleStep fPost telemetryObs configObs fError
        let rec loop cmd =
            async {
                match cmd,fDroneState() with
                | Sequence [],_                      -> ()
                | Sequence (singleCmd::rest) , ds    ->
                    let! r = step (singleCmd,ds)
                    match r with
                    | Done          -> return! loop (Sequence rest)
                    | Abort         -> ()
                    | Continue cmd  -> return! loop (Sequence (cmd::rest))
                | singleCmd,ds ->
                    let! r = step (singleCmd,ds)
                    match r with
                    | Done          -> ()
                    | Abort         -> ()
                    | Continue cmd  -> return! loop cmd
            }
        loop script.Commands


