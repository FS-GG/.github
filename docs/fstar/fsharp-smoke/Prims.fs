#light "off"
module Prims

type int = bigint
type nat = bigint
type bool = Microsoft.FSharp.Core.bool
type string = Microsoft.FSharp.Core.string

let parse_int (value:string) : int = bigint.Parse value
let op_Equals left right = left = right
