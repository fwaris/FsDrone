namespace FsDrone
open Extensions
open System

type seconds = int
type animation_id = int

[<Flags>]
type FlightMode = Progess = 1 | CombinedYaw = 2 | AbsoluteCtrl = 4 

type Progress = 
    {
        Mode        : FlightMode
        Yaw         : float32
        Pitch       : float32
        Roll        : float32
        Lift        : float32
        Psi         : float32
        PsiAccuracy : float32
     }

type BlinkAnimation = {Animation:animation_id; Frequency:float32; Duration:seconds}

type ConfigSetting = {Name:string; Value:string}

type Session = {ApplicationId:string; UserId:string; SessionId:string}
 
type Command =
    | Land
    | Takeoff
    | Emergency
    | Hover
    | Progress of Progress
    | Flattrim
    | Calibrate of int
    | Config of ConfigSetting
    | SessionConfig of (Session * ConfigSetting)
    | Watchdog
    | Ack
    | GetConfig

module CommandUtils =
    let _takeoff = 0b00010001010101000000000100000000
    let _land    = 0b00010001010101000000000000000000
    let _emrgncy = 0b00010001010101000000000010000000

    //fprintf is not the fastest but generates least garbage
    let toATCommand txw seq_no = function
        | Land          -> fprintf txw "AT*REF%d,%d\r" !seq_no _land
        | Takeoff       -> fprintf txw "AT*REF%d,%d\r" !seq_no _takeoff
        | Emergency     -> fprintf txw "AT*REF%d,%d\r" !seq_no _emrgncy
        | Hover         -> fprintf txw "AT*PCMD=%d,0,0,0,0,0\r" !seq_no
        | Progress (p)  -> fprintf txw "AT*PCMD_MAG=%d,%d,%f,%f,%f,%f,%f,%f\r" !seq_no (int p.Mode) p.Roll p.Pitch p.Lift p.Yaw p.Psi p.PsiAccuracy
        | Flattrim      -> fprintf txw "AT*FTRIM=%d,\r" !seq_no
        | Calibrate i   -> fprintf txw "AT*CALIB=%d,%d,\r" !seq_no i
        | Watchdog      -> fprintf txw "AT*COMWDG=%d\r" !seq_no
        | Ack           -> fprintf txw "AT*CTRL=%d,5,0\r" !seq_no
        | GetConfig     -> fprintf txw "AT*CTRL=%d,4,0\r" !seq_no
        | Config {Name=k;Value=v} -> fprintf txw "AT*CONFIG=%d,\"%s\",\"%s\"\r" !seq_no k v
        | SessionConfig ({ApplicationId=appId; UserId=uid; SessionId=sid}, {Name=k;Value=v})  -> 
            let s1  = !seq_no
            seq_no := !seq_no + 1
            let s2  = !seq_no
            fprintf txw "AT*CONFIG_IDS=%d,%s,%s,%s\rAT*CONFIG=%d,\"%s\",\"%s\"\r" s1 appId uid sid s2 k v 
