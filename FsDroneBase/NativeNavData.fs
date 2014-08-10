namespace FsDrone

type NavdataOption =
  | Demo          = 0us
  | DRTime        = 1us
  | RawMeasures   = 2us
  | PhsMeasures   = 3us
  | GyroOffsets   = 4us
  | EulerAngles   = 5us
  | References    = 6us
  | Trims         = 7us
  | RcReferences  = 8us
  | PWM           = 9us
  | Altitue       = 10us
  | VisionRaw     = 11us
  | VisionOf      = 12us
  | Vision        = 13us
  | VisionPerf    = 14us
  | TrackersSend  = 15us
  | VisionDetect  = 16us
  | Watchdog      = 17us
  | ADCDataFrame  = 18us
  | VideoStream   = 19us
  | Games         = 20us
  | PressureRaw   = 21us
  | Magneto       = 22us
  | Windspeed     = 23us
  | KalmanPressure= 24us
  | HDVideoStream = 25us
  | WiFi          = 26us
  | GPS           = 27us
  | Checksum      = 65535us
  //for internal use only, required for type saftey
  | Uknown        = 65534us