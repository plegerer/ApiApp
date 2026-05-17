namespace Shared.ZvrTypes

open System.Text.RegularExpressions
open Thoth.Json.Net
open System.Threading.Tasks

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

module TaskResult =

    let bind (f : 'a -> Task<Result<'b,'e>>) (input : Task<Result<'a,'e>>) =
        task {
            let! result = input

            match result with
            | Ok value ->
                return! f value

            | Error err ->
                return Error err
        }

    let map (f : 'a -> 'b) (input : Task<Result<'a,'e>>) =
        task {
            let! result = input

            return Result.map f result
        }

    let ofResult result =
        task { return result }