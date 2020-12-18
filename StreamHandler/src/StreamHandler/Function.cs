using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using System.Text.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace StreamHandler
{
    public class Function
    {
        private readonly AmazonDynamoDBClient amazonDynamoDB = new AmazonDynamoDBClient();
        private readonly AmazonCloudWatchClient amazonCloudWatch = new AmazonCloudWatchClient();

        public async Task FunctionHandlerAsync(DynamoDBEvent dynamoDBEvent, ILambdaContext context)
        {
            var tasks = dynamoDBEvent.Records.Select(record => ProcessRecordAsync(record, context));
            await Task.WhenAll(tasks);
        }

        private async Task ProcessRecordAsync(DynamoDBEvent.DynamodbStreamRecord record, ILambdaContext context)
        {
            if (record.EventName == OperationType.MODIFY)
            {
                var newImage = record.Dynamodb.NewImage;
                var messagesProcessed = int.Parse(newImage["MessageCount"].N);
                if (!newImage.ContainsKey("ElapsedTime") && newImage.ContainsKey("MessagesSent") && messagesProcessed >= int.Parse(newImage["MessagesSent"].N))
                {
                    // This means we're done processing all messages. Calculate the elapsed time.
                    var endTime = DateTimeOffset.Parse(newImage["EndTime"].S);
                    var startTime = DateTimeOffset.Parse(newImage["StartTime"].S);
                    var elapsed = endTime - startTime;
                    var secondsPerMessage = elapsed.TotalSeconds / messagesProcessed;
                    await amazonDynamoDB.UpdateItemAsync(new UpdateItemRequest
                    {
                        TableName = "message-processor",
                        Key = record.Dynamodb.Keys,
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            { ":elapsed", new AttributeValue { N = elapsed.TotalMilliseconds.ToString() }},
                            { ":rate", new AttributeValue { N = secondsPerMessage.ToString() }},
                        },
                        UpdateExpression = "SET ElapsedTime = :elapsed, SecondsPerMessage = :rate"
                    });

                    context.Logger.LogLine($"Processing finished. Waiting to get CloudWatch stats.");
                    await Task.Delay(1000 * 60 * 2); // Wait a couple minutes for CloudWatch stats to be aggregated
                    var functionName = newImage["pk"].S.Contains("Sqs") ? "MessageProcessor-SqsProcessor" : "MessageProcessor-SnsProcessor";
                    var stats = await GetStatisticsAsync(functionName, startTime, DateTimeOffset.UtcNow);
                    context.Logger.LogLine($"Stats: {JsonSerializer.Serialize(stats)}");

                    await amazonDynamoDB.UpdateItemAsync(new UpdateItemRequest
                    {
                        TableName = "message-processor",
                        Key = record.Dynamodb.Keys,
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            { ":stats", new AttributeValue { M = StatsToMap(stats) }},
                        },
                        UpdateExpression = "SET LambdaStats = :stats"
                    });
                }
            }
        }

        public async Task<Dictionary<string, double>> GetStatisticsAsync(string functionName, DateTimeOffset startTime, DateTimeOffset endTime)
        {
            var desiredStats = new List<Stat>
            {
                new Stat { Metric = "Duration", Statistic = "Average" },
                new Stat { Metric = "Invocations", Statistic = "Sum" },
                new Stat { Metric = "ConcurrentExecutions", Statistic = "Maximum" },
            };
            var statTasks = desiredStats.Select(s => amazonCloudWatch.GetMetricStatisticsAsync(new GetMetricStatisticsRequest
            {
                Namespace = "AWS/Lambda",
                MetricName = s.Metric,
                Statistics = new List<string> { s.Statistic },
                Dimensions = new List<Dimension>
                {
                    new Dimension { Name = "FunctionName", Value = functionName },
                },
                StartTimeUtc = startTime.UtcDateTime,
                EndTimeUtc = endTime.UtcDateTime,
                Period = 3600,
            }));
            var stats = await Task.WhenAll(statTasks);

            return new Dictionary<string, double>
            {
                { "DurationAverage", stats[0].Datapoints[0].Average },
                { "InvocationsSum", stats[1].Datapoints[0].Sum },
                { "ConcurrentExecutionsMax", stats[2].Datapoints[0].Maximum },
            };
        }

        private Dictionary<string, AttributeValue> StatsToMap(Dictionary<string, double> stats)
        {
            var map = new Dictionary<string, AttributeValue>();
            foreach (var item in stats)
            {
                map.Add(item.Key, new AttributeValue { N = item.Value.ToString() });
            }
            return map;
        }

        private class Stat
        {
            public string Metric { get; set; }
            public string Statistic { get; set; }
        }
    }
}
