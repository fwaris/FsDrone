//implements the 'scripting language' for executing drone control sequences
//the script can coordiate over telemetry configuration and command channels
namespace FsDrone
open Extensions
open FsDrone

type FTM = Telemetry        -> bool
type FCS = ConfigSetting    -> bool

type TimeoutMS = int

type ScriptResult = Done | Abort

type Repeater = {When:FTM; Send:Command; Till:FTM} //if in start state, execute command till end state reached

type ScriptCommand = 
    | Send of Command
    | Repeat of Repeater  
    | RepeatTill of Command * FTM               //repeat till end state reached
    | AwaitTelemetry of FTM * TimeoutMS         //wait till a Telemetry event occurs, script execution halts if timeout occurs first
    | AwaitConfig of FCS * TimeoutMS            //wait till a config event is received
    | Sequence of ScriptCommand list

type Script = {Name:string; Commands:ScriptCommand}

module ScriptServices =

    let retryDelayMS      = 200 //time to wait before retrying a command
    let maxCommandRetries = 10  //retry command this many times before giving up

    let awaitTelemetry telemetryObs inState timeout =
        async {
            let! r =
                telemetryObs
                |> Observable.filter inState
                |> Observable.awaitAsync timeout
            match r with
            | Some _ -> return Done
            | None   -> return Abort
        }

    let awaitConfig configObs inState timeout = //can inline to share awaitTelemetry but debugging is harder
        async {
            let! r =
                configObs
                |> Observable.filter inState
                |> Observable.awaitAsync timeout
            match r with
            | Some _ -> return Done
            | None   -> return Abort
        }

    let repeatTill fPost telemetryObs cmd endState =
        let rec loop retries =
            async {
                if retries > maxCommandRetries then 
                    return Abort
                else
                    fPost cmd
                    let! r =
                        telemetryObs 
//                        |> Observable.map (fun s->printfn "%A" s; s)
                        |> Observable.filter endState 
                        |> Observable.awaitAsync retryDelayMS
                    match r with
                    | Some _ -> return Done
                    | None   -> 
                        do! Async.Sleep 10
                        return! loop (retries + 1)
            }
        loop 0

    let whenInRepeat fPost telemetryObs {When=startState; Send=cmd; Till=endState} =
        async {
            let! r = 
                telemetryObs 
                |> Observable.filter startState 
                |> Observable.awaitAsync retryDelayMS
            match r with
            | Some _ -> return! repeatTill fPost telemetryObs cmd endState
            | None   -> return Abort
        }

    let singleStep fPost telemetryObs configObs step = 
        printfn "in step %A" step
        async {
            match step with
            | Send cmd                          -> fPost cmd; return Done
            | Repeat repeater                   -> return! whenInRepeat fPost telemetryObs repeater
            | RepeatTill (cmd,endState)         -> return! repeatTill fPost telemetryObs cmd endState
            | AwaitTelemetry (inState,timeout)  -> return! awaitTelemetry telemetryObs inState timeout
            | AwaitConfig (cfgFilter,timeout)   -> return! awaitConfig configObs cfgFilter timeout
            | Sequence _                        -> return failwithf "sequence not expected"
        }

    let executeScript fPost telemetryObs configObs commands =
        let step = singleStep fPost telemetryObs configObs 
        let rec loop cmd =
            async {
                match cmd with
                | Sequence []                 -> return Done
                | Sequence (singleCmd::rest)  ->
                    let! r = step singleCmd
                    match r with
                    | Done          -> return! loop (Sequence rest)
                    | Abort         -> return Abort
                | singleCmd -> return! step singleCmd
            }
        loop commands


