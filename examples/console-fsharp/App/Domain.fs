module rec Domain
open Protogen.FsharpTypes
type TrafficLight =
    | Unknown = 0
    | Red = 1
    | Yellow = 2
    | Green = 3
type LightStatus =
    | Unknown
    | Normal
    | Warning of errorsCount:int
    | OutOfOrder of since:System.DateTimeOffset
with
    static member MakeUnknownKey () = Key.Value "0"
    static member MakeNormalKey () = Key.Value "1"
    static member MakeWarningKey () = Key.Value "2"
    static member MakeOutOfOrderKey () = Key.Value "3"
    member x.Key =
        match x with
        | Unknown -> LightStatus.MakeUnknownKey ()
        | Normal -> LightStatus.MakeNormalKey ()
        | Warning (errorsCount') -> LightStatus.MakeWarningKey ()
        | OutOfOrder (since') -> LightStatus.MakeOutOfOrderKey ()
type Crossroad = {
    Id : int
    Street1 : string
    Street2 : string
    Light : Domain.TrafficLight
    LightStatus : Domain.LightStatus
}
type Crossroad2 = {
    Id : int
    LongId : int64
    AltId : System.Guid
    Street1 : string
    Street2 : string
    IsMonitored : bool
    Xpos : float32
    Ypos : float
    Ratio : decimal
    LastChecked : System.DateTimeOffset
    ServiceInterval : System.TimeSpan
    CurrentLight : Domain.TrafficLight
    Nickname : string option
    Img : byte array
    Notes : string array
    Props : Map<string,string>
}
