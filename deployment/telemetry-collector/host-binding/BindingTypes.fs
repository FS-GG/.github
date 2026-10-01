namespace Fs.Gg.Telemetry.HostBinding

[<RequireQualifiedAccess>]
type BindingError =
    | Refused of string

exception BindingRefusal of string
