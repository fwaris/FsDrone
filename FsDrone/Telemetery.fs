namespace FsDrone

//flying substate (minor state)
type Fs = 
    | Ok
    | LostAlt
    | LostAlt_GoDown
    | Alt_OutZone
    | CombinedYaw
    | Brake
    | NoVision

//controls state; major states
type Cs =
    | Default
    | Init
    | Landed
    | Flying of Fs
    | Hovering
    | Test
    | TakingOff
    | GoingToFix
    | Landing
    | Looping
 
type  Velocity      = {Vx:float; Vy:float; Vz:float}
type  RPY           = {Roll:float; Pitch:float; Yaw:float}
type  FlightSummary = {State:Cs; Velocity:Velocity; RPY:RPY; Altidute:float; BatteryLevel:float}
type  Magneto       = {Mx:int; My:int; Mz:int}

type Telemetery =
    | FlightSummary of FlightSummary
