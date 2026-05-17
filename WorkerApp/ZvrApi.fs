namespace ServiceBusBlobWorker.GetZvrData
open FsHttp
open Thoth.Json.Net
open System
open System.IO
open System.Globalization
open Shared.Messaging
open System.Threading.Tasks

module Types =

    type StatusResult = {Status:string}
    module StatusResult = 
        let decoder : Decoder<StatusResult> =
            Decode.object (fun get ->
                {
                    Status = get.Required.Field "status" Decode.string
                }
            )
    type GenericStringType ={
        Type : string
        ValueString : string
        Content : string
        Confidence : decimal
    }
    module GenericStringType =
        let decoder : Decoder<GenericStringType> =
            
            Decode.object (fun get ->
                let content_val = get.Optional.Field "content" Decode.string
                let valueString_val = get.Optional.Field "valueString" Decode.string
                {
                    Type = get.Required.Field "type" Decode.string
                    ValueString = match valueString_val with | Some x -> x | None -> "no value"
                    Content = match content_val, valueString_val with | Some x, Some y -> x | None, Some y -> y | _,_ -> "no value"
                    Confidence = get.Required.Field "confidence" Decode.decimal
                }
            )
    type GenericDateType = {
        Type : string
        ValueDate : DateOnly
        Content : string
        Confidence : decimal
    }
    module GenericDateType =
        let decoder : Decoder<GenericDateType> =
            Decode.object (fun get ->
                {
                    Type = get.Required.Field "type" Decode.string
                    ValueDate = DateOnly.FromDateTime(get.Required.Field "valueDate" Decode.datetimeLocal)
                    Content = get.Required.Field "content" Decode.string
                    Confidence = get.Required.Field "confidence" Decode.decimal
                }
            )
    type FunctionLine = {
        Function : GenericStringType
        FirstName : GenericStringType
        LastName : GenericStringType
        ValidTo : GenericStringType//GenericDateType
    }
    module FunctionLine =
        let decoder : Decoder<FunctionLine> =
            Decode.object (fun get ->
                {
                    Function = get.Required.Field "function" GenericStringType.decoder
                    FirstName = get.Required.Field "firstName" GenericStringType.decoder
                    LastName = get.Required.Field "lastName" GenericStringType.decoder
                    ValidTo = get.Required.Field "validTo" GenericStringType.decoder
                }
            )
    type FunctionLineObject = {
        ValueObject : FunctionLine
    }
    module FunctionLineObject=
        let decoder : Decoder<FunctionLineObject> =
            Decode.object (fun get ->
                {
                    ValueObject = get.Required.Field "valueObject" FunctionLine.decoder
                }
            )
    type FunctionArray = {FunctionArray : FunctionLineObject List}
    module FunctionArray=
        let decoder : Decoder<FunctionArray> =
            Decode.object (fun get ->
                {
                    FunctionArray = get.Required.Field "valueArray" (Decode.list FunctionLineObject.decoder)
                }
            )
    type Fields = {
        AsOf : GenericStringType//GenericDateType
        Zvr:GenericStringType
        Name:GenericStringType
        Co:GenericStringType option
        Place:GenericStringType
        Street:GenericStringType
        StreetNumber:GenericStringType
        Zip:GenericStringType
        City:GenericStringType
        Functions: FunctionArray
    }
    module Fields =
        let decoder : Decoder<Fields> =
            Decode.object (fun get ->
                {
                    AsOf = get.Required.Field "asof" GenericStringType.decoder
                    Zvr = get.Required.Field "zvr" GenericStringType.decoder
                    Name = get.Required.Field "name" GenericStringType.decoder
                    Co = get.Optional.Field "co" GenericStringType.decoder
                    Place = get.Required.Field "place" GenericStringType.decoder
                    Street = get.Required.Field "street" GenericStringType.decoder
                    StreetNumber = get.Required.Field "streetnumber" GenericStringType.decoder
                    Zip = get.Required.Field "zip" GenericStringType.decoder
                    City = get.Required.Field "city" GenericStringType.decoder
                    Functions = get.Required.Field "functions" FunctionArray.decoder
                }
            )
    type Document = {
        DocType : string
        Fields : Fields
    }
    module Document =
        let decoder : Decoder<Document> =
            Decode.object (fun get ->
                {
                    DocType = get.Required.Field "docType" Decode.string
                    Fields = get.Required.Field "fields" Fields.decoder
                }
            )
    type AnalyzeResult ={
        ApiVersion : string
        ModelId : string
        Documents : Document List
    }
    module AnalyzeResult =
        let decoder : Decoder<AnalyzeResult> =
            Decode.object (fun get ->
                {
                    ApiVersion = get.Required.Field "apiVersion" Decode.string
                    ModelId = get.Required.Field "modelId" Decode.string
                    Documents = get.Required.Field "documents" (Decode.list Document.decoder)
                }
            )
    type ParsedDocIntelResult = {
        Status : string
        CreatedDateTime : DateTime
        LastUpdatedDateTime : DateTime
        AnalyzeResult : AnalyzeResult    
    }
    module ParsedDocIntelResult = 
        let decoder : Decoder<ParsedDocIntelResult> =
            Decode.object (fun get ->
                {
                    Status = get.Required.Field "status" Decode.string
                    CreatedDateTime = get.Required.Field "createdDateTime" Decode.datetimeUtc
                    LastUpdatedDateTime = get.Required.Field "lastUpdatedDateTime" Decode.datetimeUtc
                    AnalyzeResult = get.Required.Field "analyzeResult" AnalyzeResult.decoder
                }
            )




module CallZvrEndpoint =
    let documentIntelligenceEndpoint =Environment.GetEnvironmentVariable "DOCINTELENDPOINT"
    let apiKey = Environment.GetEnvironmentVariable "DOCINTELAPIKEY"

    let baseUrl = "https://citizen.bmi.gv.at/at.gv.bmi.zvnsrv-p/zvrlink/"

    let getStreamFromHttp zvr =
        task{
            let httpResponse = 
                http {
                    GET (baseUrl + zvr)
                }
                |>Request.send
            match httpResponse.statusCode with
            | Net.HttpStatusCode.OK -> return Ok (httpResponse.content.ReadAsStream())
            | _ -> return Error "Error querying pdf" 
        }
    let sendToDocumentIntelligence (fileStream: Stream) =
        let contentType:FsHttp.Domain.ContentType = {value = "application/pdf"; charset = None}
        let contentData = StreamContent fileStream
        task{
            let httpResponse =
                http {
                    POST documentIntelligenceEndpoint
                    header "Ocp-Apim-Subscription-Key" apiKey
                    body 
                    content
                        contentType 
                        contentData
                    }
                    |> Request.send
            match httpResponse.statusCode with
            | Net.HttpStatusCode.Accepted ->        
                        match httpResponse.headers.TryGetValues "Operation-Location" with
                        | true,  values when Seq.isEmpty values |> not ->
                                    let operationLocation = values |> Seq.head
                                    return Ok operationLocation
                        | _ , _->
                                    return Error "No Operation-Location-Header found."
            | _ ->  return Error (httpResponse.statusCode.ToString())
        }
    
    open Types
    let getDocumentIntelligenceResult maxRetry operationUrl =

        let rec pollOperationResult currentTry =
            task {

                match currentTry with

                | c when c < maxRetry ->

                    do! Task.Delay 2000

                    let httpResponse =
                        http {
                            GET operationUrl
                            header "Ocp-Apim-Subscription-Key" apiKey
                        }
                        |> Request.send

                    match httpResponse.statusCode with

                    | Net.HttpStatusCode.OK ->

                        let str =
                            httpResponse
                            |> Response.toText

                        match str |> Decode.fromString StatusResult.decoder with

                        | Ok s ->

                            match s.Status with

                            | "succeeded" ->

                                return
                                    str
                                    |> Decode.fromString ParsedDocIntelResult.decoder

                            | "failed" ->

                                return Error "Document Intelligence failed"

                            | _ ->

                                return!
                                    pollOperationResult (currentTry + 1)

                        | Error e ->
                            return Error e

                    | _ ->
                        return Error (httpResponse.statusCode.ToString())

                | _ ->
                    return Error "Maximum number of retries reached"
            }

        pollOperationResult 0

module CreateZvrUpdatePosted =
    open Types 
    let parseToDate (s: string) =
    // String "dd.MM.yyyy" → DateTime ohne Uhrzeit
        let dt = DateTime.ParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture)
        // Hole die Europe/Vienna TimeZoneInfo
        let tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna")
        // Rechne dt als "unspecified" in Vienna-Zeit
        let viennaTime = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified)
        // Bestimme den Offset für genau dieses Datum in Vienna
        let offset = tz.GetUtcOffset(viennaTime)
        // Gib ein DateTimeOffset zurück (inkl. +01:00 oder +02:00)
        DateTimeOffset(viennaTime, offset)

    let createZvrObject (zvr:ParsedDocIntelResult) =
        let zvrObj = zvr.AnalyzeResult.Documents.Head.Fields
        let street = zvrObj.Street.ValueString.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim([| ' '; ',' ;'"'|]) 
        let name = zvrObj.Name.ValueString.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim([| ' '; ',';'"' |]) 
        let city = zvrObj.City.ValueString.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim([| ' '; ',';'"' |])
        let number = zvrObj.StreetNumber.ValueString.Trim([| ' '; ',' |])
        let zip = zvrObj.Zip.ValueString.Trim([| ' '; ',' |])
        let queryStreetAddress = zip + " " + city + ", " + street + " " + number
        
        let functionaere = List.map (fun x->
                                            {
                                                Funktion=x.ValueObject.Function.ValueString
                                                Nachname = x.ValueObject.LastName.ValueString
                                                Vorname = x.ValueObject.FirstName.ValueString
                                                GueltigBis = parseToDate x.ValueObject.ValidTo.ValueString
                                            }
                                            ) zvrObj.Functions.FunctionArray
        {
            Zvr = zvrObj.Zvr.ValueString
            Stichtag = parseToDate zvrObj.AsOf.ValueString
            Vereinsname = name
            Zustelladresse = Some
                {
                    Formatted = street + " " + number + ", " + zip + " " + city + ", Österreich"
                    City = Some city
                    Country = Some "Österreich"
                    PostalCode = Some zip
                    StreetAddress = Some (street + " " + number)
                }
            Co = 
                let res = match zvrObj.Co with   
                            | Some y -> match y.ValueString with | "no value" -> None | x -> Some x
                            | None -> None
                res
            Sitz = zvrObj.Place.ValueString
            ZvrFunktionaere = functionaere
        }

module UploadToWix = 

    let apiKey = Environment.GetEnvironmentVariable "WIXCUSTOMAPIKEY"

    let baseUrl = "https://www.hunde-oerv.at/_functions/"

    let postToCustomApi jsonString functionName =
        let endpoint = baseUrl + functionName
        task {
            let httpResponse =
                http {
                    POST endpoint
                    header "x-api-key" apiKey
                    body
                    json jsonString
                }
                |> Request.send
            match httpResponse.statusCode with 
            | Net.HttpStatusCode.Created -> return httpResponse|> Response.toText|> Ok //|> Decode.Auto.fromString<inserted>
            | _ -> return Error (httpResponse.statusCode.ToString())
        }