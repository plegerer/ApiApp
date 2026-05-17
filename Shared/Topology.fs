namespace Shared.Topology

open Thoth.Json.Net
open System.IO

type WorkerEntry =
    { Name : string
      IncomingQueue : string option
      OutgoingQueue : string option
      ArchiveContainer : string option }

type TopologyConfig =
    { Workers : WorkerEntry list }

module TopologyConfig =

    let workerDecoder : Decoder<WorkerEntry> =
        Decode.object (fun get ->
            { Name = get.Required.Field "Name" Decode.string
              IncomingQueue = get.Optional.Field "IncomingQueue" Decode.string
              OutgoingQueue = get.Optional.Field "OutgoingQueue" Decode.string
              ArchiveContainer = get.Optional.Field "ArchiveContainer" Decode.string })

    let decoder : Decoder<TopologyConfig> =
        Decode.object (fun get ->
            { Workers = get.Required.Field "Workers" (Decode.list workerDecoder) })

module Topology =

    let loadTopology path =
        let json = File.ReadAllText path

        match Decode.fromString TopologyConfig.decoder json with
        | Ok config ->
            config

        | Error error ->
        failwithf "Could not decode topology.json: %s" error

    let tryGetWorker name config =
        config.Workers
        |> List.tryFind (fun w -> w.Name = name)

    let getIncomingQueue name config =
        tryGetWorker name config
        |> Option.bind (fun w -> w.IncomingQueue)

    let getOutgoingQueue name config =
        tryGetWorker name config
        |> Option.bind (fun w -> w.OutgoingQueue)

    let getArchiveContainer name config =
        tryGetWorker name config
        |> Option.bind (fun w -> w.ArchiveContainer)