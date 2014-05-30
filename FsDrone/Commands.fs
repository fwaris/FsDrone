namespace FsDrone
open Extensions
open System

type seconds = int
type animation_id = int

type Progress = 
    {
        Yaw         : float32
        Pitch       : float32
        Roll        : float32
        Lift        : float32
        Psi         : float32
        PsiAccuracy : float32
     }

type BlinkAnimation = {Animation:animation_id; Frequency:float32; Duration:seconds} //
 
type Command =
    | Land
    | Takeoff
    | Emergency
    | Hover
    | Progress of Progress
    | Flattrim
    | Calibrate of int
    | Config of (string * string)
    | Watchdog
    | Ctrl   of int
