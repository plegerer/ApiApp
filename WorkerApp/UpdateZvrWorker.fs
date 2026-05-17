namespace ServiceBusUpdateZvrWorker

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
          id : string }

    let extractMessage message =
        Envelope.decodeFromString message

    let payload envelope =
        match envelope.message with
        | UdpdateZvrCommand m ->
            Ok (envelope, m)

        | _ ->
            Error "Wrong Message Type"

    let getZvr (envelope, command: UdpdateZvrCommand) =
        Ok {
            envelope = envelope
            id = command.zvr
        }

    let responseMessage envelope created zvr uri =
        {
            meta = {
                messageId = Guid.NewGuid().ToString()
                correlationId = envelope.meta.correlationId
                causationId = Some envelope.meta.messageId
                schemaVersion = 1
                createdAt = created
            }

            message =
                GetDocIntelCommand {
                    zvr = zvr
                    uri = uri
                }
        }

    let analyzeDocument (context:ProcessingContext) =
        task {

            let! result =
                context.id
                |> CallZvrEndpoint.getStreamFromHttp
                |> TaskResult.bind CallZvrEndpoint.sendToDocumentIntelligence

            return
                result
                |> Result.map (fun uri ->
                    {envelope = responseMessage
                        context.envelope
                        DateTime.UtcNow
                        context.id
                        uri; id = context.id})
        }

    let processMessage message =
        message
        |> extractMessage
        |> Result.bind payload
        |> Result.bind getZvr
        |> TaskResult.ofResult
        |> TaskResult.bind analyzeDocument
        |> TaskResult.map (fun y ->  Envelope.encodeToString y.envelope, y.id)


type Worker
    (
        logger: ILogger<Worker>,
        serviceBusClient: ServiceBusClient,
        blobServiceClient: BlobServiceClient
    ) =
    inherit BackgroundService()
    
    let topology = Topology.loadTopology (Path.Combine(AppContext.BaseDirectory, "topology.json"))
    let queueName = Topology.getIncomingQueue "UdpdateZvrCommand" topology|> Option.defaultValue "error occurred or does not exists"
    let containerName=Topology.getArchiveContainer "UdpdateZvrCommand" topology|> Option.defaultValue "error occurred or does not exists"
    let outputQueueName = Topology.getOutgoingQueue "UdpdateZvrCommand" topology|> Option.defaultValue "error occurred or does not exists"

    let sender =
        serviceBusClient.CreateSender outputQueueName

    let processor =
        serviceBusClient.CreateProcessor(queueName, ServiceBusProcessorOptions(MaxConcurrentCalls = 1,
            PrefetchCount = 0,
            AutoCompleteMessages = false))

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

                            let serviceBusMessage =
                                ServiceBusMessage(fst outgoingMessage)

                            do! sender.SendMessageAsync(serviceBusMessage)

                            logger.LogInformation(
                                "Outgoing message sent to queue")

                            let fileName = DateTime.UtcNow.ToString() + "_" + snd outgoingMessage + ".json"
                            
                            let blobClient = containerClient.GetBlobClient(fileName)

                            let bytes = Encoding.UTF8.GetBytes(fst outgoingMessage)
                            use stream = new IO.MemoryStream(bytes)

                            let! _ = blobClient.UploadAsync(stream)

                            logger.LogInformation("Message saved to blob: {file}", fileName)

                            do! args.CompleteMessageAsync(args.Message)

                            do! Task.Delay(2000)

                        | Error err ->

                            logger.LogError(
                                "Pipeline error: {error}",
                                err)

                            // ENTWEDER:
                            // complete -> damit keine retries

                            do! args.CompleteMessageAsync(args.Message)

                            // ODER:
                            // abandon -> retry
                            // deadletter -> poison queue

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