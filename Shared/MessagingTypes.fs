namespace Shared.Messaging

open System
open Thoth.Json.Net

// ======================================================
// DOMAIN TYPES
// ======================================================

type UdpdateZrvCommand =
    { zvr : string }

module UdpdateZrvCommand =

    let encoder (x : UdpdateZrvCommand) =
        Encode.object
            [ "zvr", Encode.string x.zvr
              ]

    let decoder : Decoder<UdpdateZrvCommand> =
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

// ======================================================
// MESSAGE
// ======================================================

type Message =
    | UdpdateZrvCommand of UdpdateZrvCommand
    | GetDocIntelCommand of GetDocIntelCommand

module Message =

    let messageType =
        function
        | UdpdateZrvCommand _ ->
            "UdpdateZrvCommand"

        | GetDocIntelCommand _ ->
            "GetDocIntelCommand"

    let payloadEncoder =
        function
        | UdpdateZrvCommand x ->
            UdpdateZrvCommand.encoder x

        | GetDocIntelCommand x ->
            GetDocIntelCommand.encoder x

    let payloadDecoder messageType : Decoder<Message> =

        match messageType with

        | "UdpdateZrvCommand" ->
            UdpdateZrvCommand.decoder
            |> Decode.map UdpdateZrvCommand

        | "GetDocIntelCommand" ->
            GetDocIntelCommand.decoder
            |> Decode.map GetDocIntelCommand

        | x ->
            Decode.fail $"Unknown messageType '{x}'"

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


