namespace ServiceBusGetDocIntelWorker

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Azure.Messaging.ServiceBus
open Azure.Storage.Blobs
open Shared.Messaging
open Shared.ZvrTypes
open Shared.Topology
open ServiceBusBlobWorker.GetZvrData

module processMessage =

    type ProcessingContext =
        { envelope : Envelope
          id : string
          uri : string }

    let extractMessage message =
        Envelope.decodeFromString message

    let payload envelope =
        match envelope.message with
        | GetDocIntelCommand m ->
            Ok (envelope, m)

        | _ ->
            Error "Wrong Message Type"

    let getUri (envelope, command: GetDocIntelCommand) =
        Ok {
            envelope = envelope
            id = command.zvr
            uri = command.uri
        }

    let responseMessage envelope created parsedDocIntelResult =
        {
            meta = {
                messageId = Guid.NewGuid().ToString()
                correlationId = envelope.meta.correlationId
                causationId = Some envelope.meta.messageId
                schemaVersion = 1
                createdAt = created
            }

            message =
                ZvrUpdatePostedEvent (CreateZvrUpdatePosted.createZvrObject parsedDocIntelResult)
        }

    let analyzeDocument context =
        task {

            let! result =
                context.uri
                |> CallZvrEndpoint.getDocumentIntelligenceResult 30


            return
                result
                |> Result.map (fun i ->
                    {envelope = responseMessage
                        context.envelope
                        DateTime.UtcNow
                        i;id=context.id;uri=context.uri})
        }

    let processMessage message =
        message
        |> extractMessage
        |> Result.bind payload
        |> Result.bind getUri
        |> TaskResult.ofResult
        |> TaskResult.bind analyzeDocument
        |> TaskResult.bind (fun p -> 
                task{ 
                    let! res = UploadToWix.postToCustomApi (Message.encodePayloadToString p.envelope.message) "upsertOrtsgruppe"
                    match res with
                    | Ok _ -> return Ok p
                    | Error x -> return Error x })
        |> TaskResult.map (fun y ->  Envelope.encodeToString y.envelope, y.id)

type Worker
    (
        logger: ILogger<Worker>,
        serviceBusClient: ServiceBusClient,
        blobServiceClient: BlobServiceClient
    ) =
    inherit BackgroundService()

    let topology = Topology.loadTopology (Path.Combine(AppContext.BaseDirectory, "topology.json"))
    let queueName = Topology.getIncomingQueue "GetDocIntelCommand" topology|> Option.defaultValue "error occurred or does not exists"
    let containerName=Topology.getArchiveContainer "GetDocIntelCommand" topology|> Option.defaultValue "error occurred or does not exists"


    let processor =
        serviceBusClient.CreateProcessor queueName

    override _.ExecuteAsync(stoppingToken: CancellationToken) =
        task {
            logger.LogInformation("Worker started")

            let containerClient =
                blobServiceClient.GetBlobContainerClient(containerName)

            let! _ =  containerClient.CreateIfNotExistsAsync()

            // --- Message Handler ---
            let handleMessage (args: ProcessMessageEventArgs) =
                task {
                    try
                        
                        let! result =
                            args.Message.Body.ToString()
                            |> processMessage.processMessage

                        match result with

                        | Ok outgoingMessage ->
                            
                            let fileName = DateTime.UtcNow.ToString() + "_" + snd outgoingMessage + ".json"
                            
                            let blobClient = containerClient.GetBlobClient(fileName)

                            let bytes = Encoding.UTF8.GetBytes(fst outgoingMessage)
                            use stream = new IO.MemoryStream(bytes)

                            let! _ = blobClient.UploadAsync(stream)

                            logger.LogInformation("Message saved to blob: {file}", fileName)

                            do! args.CompleteMessageAsync(args.Message)

                        | Error err ->

                            logger.LogError(
                                "Pipeline error: {error}",
                                err)

                            // ENTWEDER:
                            // complete -> damit keine retries

                            do! args.CompleteMessageAsync(args.Message)

                    with ex ->

                        logger.LogError(
                            ex,
                            "Unhandled worker exception")

                        do! args.AbandonMessageAsync(args.Message)

                } :> Task

            // --- Error Handler ---
            let handleError (args: ProcessErrorEventArgs) =
                logger.LogError(args.Exception, "ServiceBus error")
                Task.CompletedTask

            // --- Zuweisung (der einzige „C#-Teil“) ---
            processor.add_ProcessMessageAsync(Func<_, _>(handleMessage))
            processor.add_ProcessErrorAsync(Func<_, _>(handleError))

            do! processor.StartProcessingAsync(stoppingToken)

            logger.LogInformation("Listening for messages...")

            try
                while not stoppingToken.IsCancellationRequested do
                    do! Task.Delay(1000, stoppingToken)
            finally
                processor.StopProcessingAsync().GetAwaiter().GetResult()
        }