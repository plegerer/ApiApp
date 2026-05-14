namespace Shared.ZvrTypes

open System.Text.RegularExpressions
open Thoth.Json.Net

type ZvrImportRequest =
    { Zvrs : string list }

module ZvrImportRequest =

    let private zvrRegex =
        Regex("^\d{9,10}$", RegexOptions.Compiled)

    let isValidZvr (value : string) =
        not (System.String.IsNullOrWhiteSpace value)
        && zvrRegex.IsMatch(value)

    let validate (request : ZvrImportRequest) =
        request.Zvrs
        |> List.forall isValidZvr

    let decoder : Decoder<ZvrImportRequest> =
            Decode.object (fun get ->
                        { Zvrs = get.Required.Field "zvrs" (Decode.list Decode.string) })

    let decodeFromString jsonString = Decode.fromString decoder jsonString

    let encoder (request : ZvrImportRequest) =
        Encode.object
            [ "zvrs",
            Encode.list (List.map Encode.string request.Zvrs) ]
