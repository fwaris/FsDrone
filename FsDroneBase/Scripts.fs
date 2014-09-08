//commonly used command scripts and state queries
namespace FsDrone
open FsDrone

module StateQueries =
    let inline (|Mask|) m e = int e &&& int m > 0

    //common queries for discerning drone state from incoming telemetry data

    let isFlying      = function NavState (Flying _) -> true | _ -> false
    let isLanded      = function NavState Landed -> true | _ -> false
    let haveNavData   = function FlightSummary _ -> true | _ -> false

    //drone state mask queries
    let inBootstrap      = function DroneState (Mask ArdroneState.ARDRONE_NAVDATA_BOOTSTRAP true) -> true | _ -> false
    let commandModeOn    = function DroneState (Mask ArdroneState.ARDRONE_COMMAND_MASK true) -> true | _ -> false
    let commandModeOff   = function DroneState (Mask ArdroneState.ARDRONE_COMMAND_MASK false) -> true | _ -> false
    let inComWatchdog    = function DroneState (Mask ArdroneState.ARDRONE_COM_WATCHDOG_MASK true) -> true | _ -> false

module CommonScripts =
    open StateQueries

    //scripts for commonly needed drone actions

    let hover     = {Name="Hover";    Commands = Sequence [AwaitTelemetry (isFlying, 100) ; Send Hover ]}
    let takeoff   = {Name="Takeoff";  Commands = Repeat {When=isLanded; Send=Takeoff; Till=isFlying} }
    let ``land``  = {Name="Land";     Commands = Repeat {When=isFlying; Send=Land; Till=isLanded} }

    let bootstrap session  =
        {
            Name = "Bootstrap"
            Commands = Sequence 
                [
                    Repeat {When=inBootstrap; Send=SessionConfig(session,{Name="general:navdata_demo"; Value="TRUE"}); Till=commandModeOn}
                    Repeat {When=commandModeOn; Send=Ack; Till=haveNavData}
                ]
        }

    let setConfig config =
        {
            Name = sprintf "SetConfig %A" config
            Commands = Sequence
                [
                    Send (Config(config))
                    Repeat {When=commandModeOn; Send=Ack; Till=commandModeOff}
                ]
        }

    let setSessionConfig session config =
        {
            Name = sprintf "SetConfig %A" config
            Commands = Sequence
                [
                    Send (SessionConfig(session,config))
                    Repeat {When=commandModeOn; Send=Ack; Till=commandModeOff}
                ]
        }