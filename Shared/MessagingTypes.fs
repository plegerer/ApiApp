namespace Shared.Messaging

open System
open Thoth.Json.Net

// ======================================================
// DOMAIN TYPES
// ======================================================

type UdpdateZvrCommand =
    { zvr : string }

module UdpdateZvrCommand =

    let encoder (x : UdpdateZvrCommand) =
        Encode.object
            [ "zvr", Encode.string x.zvr
              ]

    let decoder : Decoder<UdpdateZvrCommand> =
        Decode.object (fun get ->
            { zvr =
                get.Required.Field "zvr" Decode.string

            })

// ------------------------------------------------------

type GetDocIntelCommand =
    { zvr : string
      uri : string }

module GetDocIntelCommand =

    let encoder (x : GetDocIntelCommand) =
        Encode.object [ 
                "zvr", Encode.string x.zvr
                "uri", Encode.string x.uri ]

    let decoder : Decoder<GetDocIntelCommand> =
        Decode.object (fun get ->
            { uri = get.Required.Field "uri" Decode.string
              zvr = get.Required.Field "zvr" Decode.string })

// ------------------------------------------------------

type ZvrFunktionaere = {
    Funktion : string
    Nachname : string
    Vorname : string
    GueltigBis : DateTimeOffset
}

module ZvrFunktionaere =

    let encoder (x : ZvrFunktionaere) =
        Encode.object [
            "funktion", Encode.string x.Funktion
            "nachname", Encode.string x.Nachname
            "vorname", Encode.string x.Vorname
            "gueltigBis", Encode.datetimeOffset x.GueltigBis
        ]

    let decoder : Decoder<ZvrFunktionaere> =
        Decode.object (fun get ->
            {
                Funktion = get.Required.Field "funktion" Decode.string
                Nachname = get.Required.Field "nachname" Decode.string
                Vorname = get.Required.Field "vorname" Decode.string
                GueltigBis = get.Required.Field "gueltigBis" Decode.datetimeOffset
            })

type Address = {
    Formatted : string
    City: string option
    Country : string option
    PostalCode : string option
    StreetAddress : string option
}

module Address =

    let encoder (x : Address) =
        Encode.object [
            "formatted", Encode.string x.Formatted

            "city",
            x.City
            |> Option.map Encode.string
            |> Option.defaultValue Encode.nil

            "country",
            x.Country
            |> Option.map Encode.string
            |> Option.defaultValue Encode.nil

            "postalCode",
            x.PostalCode
            |> Option.map Encode.string
            |> Option.defaultValue Encode.nil

            "streetAddress",
            x.StreetAddress
            |> Option.map Encode.string
            |> Option.defaultValue Encode.nil
        ]

    let decoder : Decoder<Address> =
        Decode.object (fun get ->
            {
                Formatted = get.Required.Field "formatted" Decode.string
                City = get.Optional.Field "city" Decode.string
                Country = get.Optional.Field "country" Decode.string
                PostalCode = get.Optional.Field "postalCode" Decode.string
                StreetAddress = get.Optional.Field "streetAddress" Decode.string
            })

type ZvrUpdatePostedEvent = {
    Stichtag : DateTimeOffset
    Zvr : string
    Vereinsname : string
    Zustelladresse : Address option
    Co : string option
    Sitz : string
    ZvrFunktionaere : ZvrFunktionaere list
}

module ZvrUpdatePostedEvent =

    let encoder (x : ZvrUpdatePostedEvent) =
        Encode.object [

            "stichtag", Encode.datetimeOffset x.Stichtag
            "zvr", Encode.string x.Zvr
            "vereinsname", Encode.string x.Vereinsname
            "zustelladresse",
            x.Zustelladresse
            |> Option.map Address.encoder
            |> Option.defaultValue Encode.nil
            "co",
            x.Co
            |> Option.map Encode.string
            |> Option.defaultValue Encode.nil
            "sitz",
            Encode.string x.Sitz
            "zvrFunktionaere",
            x.ZvrFunktionaere
            |> List.map ZvrFunktionaere.encoder
            |> Encode.list
        ]

    let decoder : Decoder<ZvrUpdatePostedEvent> =
        Decode.object (fun get ->

            {
                Stichtag = get.Required.Field "stichtag" Decode.datetimeOffset
                Zvr = get.Required.Field "zvr" Decode.string

                Vereinsname =
                    get.Required.Field
                        "vereinsname"
                        Decode.string

                Zustelladresse =
                    get.Optional.Field
                        "zustelladresse"
                        Address.decoder

                Co =
                    get.Optional.Field
                        "co"
                        Decode.string

                Sitz =
                    get.Required.Field
                        "sitz"
                        Decode.string

                ZvrFunktionaere =
                    get.Required.Field
                        "zvrFunktionaere"
                        (Decode.list ZvrFunktionaere.decoder)
            })

// ======================================================
// MESSAGE
// ======================================================

type Message =
    | UdpdateZvrCommand of UdpdateZvrCommand
    | GetDocIntelCommand of GetDocIntelCommand
    | ZvrUpdatePostedEvent of ZvrUpdatePostedEvent

module Message =

    let messageType =
        function
        | UdpdateZvrCommand _ ->
            "UdpdateZvrCommand"

        | GetDocIntelCommand _ ->
            "GetDocIntelCommand"

        | ZvrUpdatePostedEvent _ ->
            "ZvrUpdatePostedEvent"

    let payloadEncoder =
        function
        | UdpdateZvrCommand x ->
            UdpdateZvrCommand.encoder x

        | GetDocIntelCommand x ->
            GetDocIntelCommand.encoder x

        | ZvrUpdatePostedEvent x ->
            ZvrUpdatePostedEvent.encoder x

    let payloadDecoder messageType : Decoder<Message> =

        match messageType with

        | "UdpdateZvrCommand" ->
            UdpdateZvrCommand.decoder
            |> Decode.map UdpdateZvrCommand

        | "GetDocIntelCommand" ->
            GetDocIntelCommand.decoder
            |> Decode.map GetDocIntelCommand

        | "ZvrUpdatePostedEvent" ->
            ZvrUpdatePostedEvent.decoder
            |> Decode.map ZvrUpdatePostedEvent

        | x ->
            Decode.fail $"Unknown messageType '{x}'"

    let encodePayloadToString (m:Message) = payloadEncoder m |> Encode.toString 4

// ======================================================
// METADATA
// ======================================================

type Metadata =
    { messageId : string
      correlationId : string
      causationId : string option
      createdAt : DateTime
      schemaVersion : int }

module Metadata =

    let encoder (x : Metadata) =
        Encode.object
            [ "messageId",
              Encode.string x.messageId

              "correlationId",
              Encode.string x.correlationId

              "causationId",
              Encode.option Encode.string x.causationId

              "createdAt",
              Encode.datetime x.createdAt

              "schemaVersion",
              Encode.int x.schemaVersion ]

    let decoder : Decoder<Metadata> =
        Decode.object (fun get ->
            { messageId =
                get.Required.Field
                    "messageId"
                    Decode.string

              correlationId =
                get.Required.Field
                    "correlationId"
                    Decode.string

              causationId =
                get.Optional.Field
                    "causationId"
                    Decode.string

              createdAt =
                get.Required.Field
                    "createdAt"
                    Decode.datetimeUtc

              schemaVersion =
                get.Required.Field
                    "schemaVersion"
                    Decode.int })

// ======================================================
// ENVELOPE
// ======================================================

type Envelope =
    { meta : Metadata
      message : Message }

module Envelope =

    let encoder (x : Envelope) =

        let messageType =
            Message.messageType x.message

        let payload =
            Message.payloadEncoder x.message

        Encode.object
            [ "meta",
              Encode.object
                  [ "messageType",
                    Encode.string messageType

                    "messageId",
                    Encode.string x.meta.messageId

                    "correlationId",
                    Encode.string x.meta.correlationId

                    "causationId",
                    Encode.option
                        Encode.string
                        x.meta.causationId

                    "createdAt",
                    Encode.datetime x.meta.createdAt

                    "schemaVersion",
                    Encode.int x.meta.schemaVersion ]

              "payload", payload ]

    let decoder : Decoder<Envelope> =

        Decode.object (fun get ->

            let meta =
                get.Required.Field "meta" Metadata.decoder

            let messageType =
                get.Required.At
                    [ "meta"; "messageType" ]
                    Decode.string

            let payload =
                get.Required.Field
                    "payload"
                    (Message.payloadDecoder messageType)

            { meta = meta
              message = payload })

    let encodeToString
        (x : Envelope) =

        encoder x
        |> Encode.toString 4

    let decodeFromString
        (json : string) =

        Decode.fromString decoder json

    


