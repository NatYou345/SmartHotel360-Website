using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.ProjectOxford.Vision;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


namespace PetCheckerFunction
{
    public static class PetChecker
    {
        [Function("PetChecker")]
        [SignalROutput(HubName = "petcheckin", ConnectionStringSetting = "AzureSignalRConnectionString")]
        public static async Task<SignalRMessageAction?> RunPetChecker(
            [CosmosDBTrigger(
                databaseName: "pets",
                containerName: "checks",
                Connection = "constr",
                LeaseContainerName = "leases",
                CreateLeaseContainerIfNotExists = true)] PetDocument document,
            FunctionContext context)
        {
            var log = context.GetLogger(nameof(PetChecker));

            try
            {
                if (document.IsApproved.HasValue)
                {
                    return null;
                }

                var url = document.MediaUrl;
                var uploaded = document.Created;
                log.LogInformation(">>> Processing image in {Url} uploaded at {Uploaded}", url, uploaded);

                using var httpClient = new HttpClient();
                using var res = await httpClient.GetAsync(url);
                await using var stream = await res.Content.ReadAsStreamAsync();

                log.LogInformation("--- Image successfully downloaded from storage");
                var (allowed, message, tags) = await PassesImageModerationAsync(stream, log);
                log.LogInformation("--- Image analyzed. It was {ApprovalState} approved", allowed ? string.Empty : "NOT");

                document.IsApproved = allowed;
                document.Message = message;
                document.Analyzed = DateTime.UtcNow;

                log.LogInformation("--- Updating Cosmos DB document to have historical data");
                await UpsertDocument(document, log);
                log.LogInformation("--- Sending SignalR response.");
                log.LogInformation("--- Image tags: {Tags}", string.Join(",", tags));
                log.LogInformation("<<< Done! Image in {Url} processed!", url);

                return CreateSignalRResponse(allowed, message);
            }
            catch (Exception ex)
            {
                var msg = $"Error {ex.Message} ({ex.GetType().Name})";
                log.LogInformation("!!! {Message}", msg);

                if (ex is AggregateException aggex)
                {
                    foreach (var innex in aggex.InnerExceptions)
                    {
                        log.LogInformation("!!! (inner) Error {Message} ({Type})", innex.Message, innex.GetType().Name);
                    }
                }

                return CreateSignalRResponse(false, msg);
            }
        }

        private static SignalRMessageAction CreateSignalRResponse(bool isOk, string message)
        {
            return new SignalRMessageAction("ProcessDone")
            {
                Arguments = new[] { new {
                    processedAt = DateTime.UtcNow,
                    accepted = isOk,
                    message
                }}
            };
        }

        private static async Task UpsertDocument(PetDocument doc, ILogger log)
        {
            var endpoint = await GetSecret("cosmos_uri");
            var auth = await GetSecret("cosmos_key");

            using var client = new CosmosClient(endpoint, auth);
            var dbName = "pets";
            var colName = "checks";
            doc.Analyzed = DateTime.UtcNow;
            var container = client.GetContainer(dbName, colName);
            await container.UpsertItemAsync(doc, PartitionKey.None);
            log.LogInformation("--- Cosmos DB document updated.");
        }

        private static async Task<string> GetSecret(string secretName)
        {
            
            return Environment.GetEnvironmentVariable(secretName);
        }

        public static async Task<(bool allowd, string message, string[] tags)> PassesImageModerationAsync(Stream image, ILogger log)
        {
            try
            {
            log.LogInformation("--- Creating Vision API client and analyzing image");

                var key = await GetSecret("MicrosoftVisionApiKey");
                var endpoint = await GetSecret("MicrosoftVisionApiEndpoint");
                var numTags = await GetSecret("MicrosoftVisionNumTags");
                var client = new VisionServiceClient(key, endpoint);
                var features = new VisualFeature[] { VisualFeature.Description };
                var result = await client.AnalyzeImageAsync(image, features);

                log.LogInformation("--- Image analyzed with tags: {Tags}", string.Join(",", result.Description.Tags));
                if (!int.TryParse(numTags, out var tagsToFetch))
                {
                    tagsToFetch = 5;
                }
                var fetchedTags = result?.Description?.Tags.Take(tagsToFetch).ToArray() ?? new string[0];
                bool isAllowed = fetchedTags.Contains("dog");
                string message = result?.Description?.Captions.FirstOrDefault()?.Text;
                return (isAllowed, message, fetchedTags);
            }
            catch (Exception ex)
            {
                log.LogInformation("Vision API error! {Message}", ex.Message);
                return (false, "error " + ex.Message, new string[0]);
            }
        }

        [Function(nameof(SignalRInfo))]
        public static HttpResponseData SignalRInfo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
            [SignalRConnectionInfoInput(HubName = "petcheckin")] string connectionInfo)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            response.WriteString(connectionInfo);
            return response;
        }

    }
}